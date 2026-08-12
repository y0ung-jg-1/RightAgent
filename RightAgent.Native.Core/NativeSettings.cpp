#include "NativeSettings.h"

#include <windows.h>
#include <appmodel.h>
#include <shlobj.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Data.Json.h>

#include <algorithm>
#include <cwctype>
#include <fstream>
#include <optional>
#include <set>
#include <system_error>

namespace
{
    using namespace rightagent;
    using winrt::Windows::Data::Json::JsonArray;
    using winrt::Windows::Data::Json::JsonObject;
    using winrt::Windows::Data::Json::JsonValueType;

    std::wstring ToLower(std::wstring value)
    {
        std::transform(value.begin(), value.end(), value.begin(), [](const wchar_t value)
        {
            return static_cast<wchar_t>(std::towlower(value));
        });
        return value;
    }

    std::wstring Trim(std::wstring value)
    {
        const auto isNotSpace = [](const wchar_t character) { return !std::iswspace(character); };
        const auto start = std::find_if(value.begin(), value.end(), isNotSpace);
        const auto end = std::find_if(value.rbegin(), value.rend(), isNotSpace).base();
        return start < end ? std::wstring(start, end) : std::wstring{};
    }

    std::wstring GetEnvironmentValue(const wchar_t* name)
    {
        const DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
        if (required == 0)
        {
            return {};
        }

        std::wstring value(required, L'\0');
        const DWORD written = GetEnvironmentVariableW(name, value.data(), required);
        if (written == 0 || written >= required)
        {
            return {};
        }
        value.resize(written);
        return value;
    }

    std::filesystem::path GetLocalAppDataDirectory()
    {
        PWSTR rawPath = nullptr;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &rawPath)))
        {
            return {};
        }

        const std::filesystem::path path(rawPath);
        CoTaskMemFree(rawPath);
        return path;
    }

    std::wstring GetPackageFamilyNameValue()
    {
        UINT32 length = 0;
        const LONG probe = GetCurrentPackageFamilyName(&length, nullptr);
        if (probe != ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return {};
        }

        std::wstring value(length, L'\0');
        if (GetCurrentPackageFamilyName(&length, value.data()) != ERROR_SUCCESS)
        {
            return {};
        }

        if (!value.empty() && value.back() == L'\0')
        {
            value.pop_back();
        }
        return value;
    }

    std::wstring ReadUtf8File(const std::filesystem::path& path)
    {
        std::ifstream stream(path, std::ios::binary);
        if (!stream)
        {
            return {};
        }

        const std::string bytes((std::istreambuf_iterator<char>(stream)), std::istreambuf_iterator<char>());
        if (bytes.empty())
        {
            return {};
        }
        return std::wstring(winrt::to_hstring(bytes));
    }

    std::wstring GetString(const JsonObject& object, const wchar_t* key, std::wstring fallback = {})
    {
        if (!object.HasKey(key))
        {
            return fallback;
        }

        const auto value = object.GetNamedValue(key);
        return value.ValueType() == JsonValueType::String ? std::wstring(value.GetString()) : fallback;
    }

    bool GetBoolean(const JsonObject& object, const wchar_t* key, const bool fallback)
    {
        if (!object.HasKey(key))
        {
            return fallback;
        }

        const auto value = object.GetNamedValue(key);
        return value.ValueType() == JsonValueType::Boolean ? value.GetBoolean() : fallback;
    }

    int GetInteger(const JsonObject& object, const wchar_t* key, const int fallback)
    {
        if (!object.HasKey(key))
        {
            return fallback;
        }

        const auto value = object.GetNamedValue(key);
        return value.ValueType() == JsonValueType::Number ? static_cast<int>(value.GetNumber()) : fallback;
    }

    bool IsValidHttpUrl(const std::wstring& value)
    {
        const auto lower = ToLower(Trim(value));
        return lower.starts_with(L"https://") || lower.starts_with(L"http://");
    }

    std::wstring NormalizeIcon(std::wstring value)
    {
        value = Trim(std::move(value));
        const auto lower = ToLower(value);
        if (lower.starts_with(L"builtin:"))
        {
            return value;
        }
        if (lower.starts_with(L"local:"))
        {
            auto relative = value.substr(6);
            std::replace(relative.begin(), relative.end(), L'\\', L'/');
            while (!relative.empty() && relative.front() == L'/')
            {
                relative.erase(relative.begin());
            }
            const std::filesystem::path relativePath(relative);
            if (!relativePath.is_absolute())
            {
                for (const auto& segment : relativePath)
                {
                    if (segment == L"..")
                    {
                        return L"builtin:rightagent";
                    }
                }
                return L"local:" + relative;
            }
        }
        return L"builtin:rightagent";
    }

    Settings Normalize(Settings settings)
    {
        settings.schemaVersion = kSettingsSchemaVersion;
        if (settings.language != L"zh-CN" && settings.language != L"en-US")
        {
            settings.language = L"system";
        }

        std::stable_sort(settings.agents.begin(), settings.agents.end(), [](const AgentDefinition& left, const AgentDefinition& right)
        {
            return left.sort < right.sort;
        });

        std::set<std::wstring, std::less<>> ids;
        std::vector<AgentDefinition> normalized;
        for (auto agent : settings.agents)
        {
            agent.id = Trim(std::move(agent.id));
            agent.name = Trim(std::move(agent.name));
            agent.actionValue = Trim(std::move(agent.actionValue));
            agent.iconPath = NormalizeIcon(std::move(agent.iconPath));
            if (agent.id.empty() || agent.name.empty() || !ids.insert(ToLower(agent.id)).second)
            {
                continue;
            }

            const bool actionValid = agent.actionType == ActionType::Url
                ? IsValidHttpUrl(agent.actionValue)
                : !agent.actionValue.empty();
            agent.enabled = agent.enabled && actionValid;
            agent.sort = static_cast<int>(normalized.size());
            normalized.push_back(std::move(agent));
        }
        settings.agents = std::move(normalized);

        if (FindEnabledAgent(settings, settings.directAgentId) == nullptr)
        {
            settings.directAgentId.clear();
            for (const auto& agent : settings.agents)
            {
                if (agent.enabled)
                {
                    settings.directAgentId = agent.id;
                    break;
                }
            }
        }
        return settings;
    }

    AgentDefinition BuiltIn(
        const wchar_t* id,
        const wchar_t* name,
        const wchar_t* icon,
        const wchar_t* command,
        const int sort,
        const bool enabled)
    {
        AgentDefinition agent;
        agent.id = id;
        agent.name = name;
        agent.enabled = enabled;
        agent.sort = sort;
        agent.iconPath = icon;
        agent.actionType = ActionType::TerminalCommand;
        agent.actionValue = command;
        return agent;
    }
}

namespace rightagent
{
    std::filesystem::path GetLocalStateDirectory()
    {
        const auto overridePath = GetEnvironmentValue(L"RIGHTAGENT_SETTINGS_PATH");
        if (!overridePath.empty())
        {
            const std::filesystem::path path(overridePath);
            return path.has_filename() && ToLower(path.filename().wstring()) == L"settings.json" ? path.parent_path() : path;
        }

        const auto localAppData = GetLocalAppDataDirectory();
        const auto packageFamilyName = GetPackageFamilyNameValue();
        if (!localAppData.empty() && !packageFamilyName.empty())
        {
            return localAppData / L"Packages" / packageFamilyName / L"LocalState";
        }
        return localAppData / L"RightAgent";
    }

    std::filesystem::path GetSettingsPath()
    {
        const auto overridePath = GetEnvironmentValue(L"RIGHTAGENT_SETTINGS_PATH");
        if (!overridePath.empty())
        {
            const std::filesystem::path path(overridePath);
            return ToLower(path.filename().wstring()) == L"settings.json" ? path : path / kSettingsFileName;
        }
        return GetLocalStateDirectory() / kSettingsFileName;
    }

    Settings LoadSettings()
    {
        return LoadSettingsFromPath(GetSettingsPath());
    }

    Settings LoadSettingsFromPath(const std::filesystem::path& path)
    {
        const auto text = ReadUtf8File(path);
        if (text.empty())
        {
            return CreateDefaultSettings();
        }

        try
        {
            const auto root = JsonObject::Parse(text);
            Settings settings;
            settings.schemaVersion = GetInteger(root, L"schemaVersion", kSettingsSchemaVersion);
            settings.menuEnabled = GetBoolean(root, L"menuEnabled", true);
            settings.language = GetString(root, L"language", L"system");
            settings.menuMode = GetString(root, L"menuMode", L"grouped") == L"direct" ? MenuMode::Direct : MenuMode::Grouped;
            settings.directAgentId = GetString(root, L"directAgentId");
            settings.terminalProfile = GetString(root, L"terminalProfile");

            if (root.HasKey(L"agents") && root.GetNamedValue(L"agents").ValueType() == JsonValueType::Array)
            {
                const JsonArray agents = root.GetNamedArray(L"agents");
                for (const auto& value : agents)
                {
                    if (value.ValueType() != JsonValueType::Object)
                    {
                        continue;
                    }
                    const JsonObject object = value.GetObject();
                    AgentDefinition agent;
                    agent.id = GetString(object, L"id");
                    agent.name = GetString(object, L"name");
                    agent.enabled = GetBoolean(object, L"enabled", false);
                    agent.sort = GetInteger(object, L"sort", static_cast<int>(settings.agents.size()));
                    agent.iconPath = GetString(object, L"iconPath", L"builtin:rightagent");
                    if (object.HasKey(L"action") && object.GetNamedValue(L"action").ValueType() == JsonValueType::Object)
                    {
                        const auto action = object.GetNamedObject(L"action");
                        agent.actionType = GetString(action, L"type", L"terminalCommand") == L"url"
                            ? ActionType::Url
                            : ActionType::TerminalCommand;
                        agent.actionValue = GetString(action, L"value");
                    }
                    settings.agents.push_back(std::move(agent));
                }
            }
            return Normalize(std::move(settings));
        }
        catch (const winrt::hresult_error&)
        {
            return {};
        }
    }

    Settings CreateDefaultSettings()
    {
        Settings settings;
        settings.agents =
        {
            BuiltIn(L"claude-code", L"Claude Code", L"builtin:claude", L"claude", 0, CommandExists(L"claude")),
            BuiltIn(L"codex", L"Codex", L"builtin:codex", L"codex", 1, CommandExists(L"codex")),
            BuiltIn(L"kimi-web", L"Kimi Web", L"builtin:kimi", L"kimi web", 2, CommandExists(L"kimi")),
            BuiltIn(L"grok", L"Grok", L"builtin:grok", L"grok", 3, CommandExists(L"grok")),
            BuiltIn(L"opencode", L"opencode", L"builtin:opencode", L"opencode", 4, CommandExists(L"opencode"))
        };
        for (const auto& agent : settings.agents)
        {
            if (agent.enabled)
            {
                settings.directAgentId = agent.id;
                break;
            }
        }
        return settings;
    }

    bool CommandExists(std::wstring_view command)
    {
        auto value = Trim(std::wstring(command));
        if (value.empty())
        {
            return false;
        }
        if (value.front() == L'"' && value.back() == L'"' && value.size() > 1)
        {
            value = value.substr(1, value.size() - 2);
        }

        const std::filesystem::path commandPath(value);
        std::error_code error;
        if (commandPath.is_absolute())
        {
            if (std::filesystem::is_regular_file(commandPath, error))
            {
                return true;
            }
        }

        constexpr const wchar_t* extensions[] = {L"", L".exe", L".cmd", L".bat", L".com"};
        for (const auto* extension : extensions)
        {
            DWORD required = SearchPathW(nullptr, value.c_str(), extension, 0, nullptr, nullptr);
            if (required > 0)
            {
                return true;
            }
        }

        const auto home = GetEnvironmentValue(L"USERPROFILE");
        const auto localAppData = GetEnvironmentValue(L"LOCALAPPDATA");
        const std::filesystem::path extras[] =
        {
            std::filesystem::path(localAppData) / L"Microsoft" / L"WindowsApps",
            std::filesystem::path(home) / L".local" / L"bin",
            std::filesystem::path(home) / L".kimi-code" / L"bin"
        };
        for (const auto& directory : extras)
        {
            for (const auto* extension : extensions)
            {
                if (std::filesystem::is_regular_file(directory / (value + extension), error))
                {
                    return true;
                }
                error.clear();
            }
        }
        return false;
    }

    bool IsChinese(const Settings& settings)
    {
        if (settings.language == L"zh-CN")
        {
            return true;
        }
        if (settings.language == L"en-US")
        {
            return false;
        }

        wchar_t localeName[LOCALE_NAME_MAX_LENGTH]{};
        return GetUserDefaultLocaleName(localeName, LOCALE_NAME_MAX_LENGTH) > 0
            && ToLower(localeName).starts_with(L"zh");
    }

    const AgentDefinition* FindEnabledAgent(const Settings& settings, const std::wstring_view id)
    {
        if (id.empty())
        {
            return nullptr;
        }
        const auto lowered = ToLower(std::wstring(id));
        const auto found = std::find_if(settings.agents.begin(), settings.agents.end(), [&](const AgentDefinition& agent)
        {
            return agent.enabled && ToLower(agent.id) == lowered;
        });
        return found == settings.agents.end() ? nullptr : &*found;
    }

    const AgentDefinition* FindDirectAgent(const Settings& settings)
    {
        if (const auto* direct = FindEnabledAgent(settings, settings.directAgentId))
        {
            return direct;
        }
        const auto found = std::find_if(settings.agents.begin(), settings.agents.end(), [](const AgentDefinition& agent)
        {
            return agent.enabled;
        });
        return found == settings.agents.end() ? nullptr : &*found;
    }

    std::filesystem::path ResolveIconPath(
        const std::wstring_view iconPath,
        const std::filesystem::path& moduleDirectory,
        const std::filesystem::path& localStateDirectory)
    {
        const auto value = NormalizeIcon(std::wstring(iconPath));
        const auto lower = ToLower(value);
        if (lower.starts_with(L"local:"))
        {
            const auto relative = std::filesystem::path(value.substr(6)).lexically_normal();
            return localStateDirectory / relative;
        }

        auto key = lower.substr(8);
        if (key != L"claude" && key != L"codex" && key != L"kimi" && key != L"grok" && key != L"opencode")
        {
            key = L"rightagent";
        }
        return moduleDirectory / L"Assets" / L"Agents" / (key + L".ico");
    }
}
