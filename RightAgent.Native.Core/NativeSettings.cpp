#include "NativeSettings.h"
#include "ProcessHelpers.h"

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

    bool LooksLikePackageRedirectedLocalAppData(const std::filesystem::path& path)
    {
        const auto text = ToLower(path.lexically_normal().wstring());
        return text.find(L"\\packages\\") != std::wstring::npos &&
            text.find(L"\\localcache\\local") != std::wstring::npos;
    }

    std::filesystem::path GetLocalAppDataDirectory()
    {
        // Packaged COM surrogates redirect FOLDERID_LocalAppData (and often
        // KF_FLAG_NO_PACKAGE_REDIRECTION still fails). Settings live in the
        // real user profile, next to the unpackaged app.
        const auto userProfile = GetEnvironmentValue(L"USERPROFILE");
        if (!userProfile.empty())
        {
            const auto fromProfile = std::filesystem::path(userProfile) / L"AppData" / L"Local";
            std::error_code error;
            if (std::filesystem::is_directory(fromProfile, error) &&
                !LooksLikePackageRedirectedLocalAppData(fromProfile))
            {
                return fromProfile;
            }
        }

        PWSTR rawPath = nullptr;
        if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_Profile, KF_FLAG_DEFAULT, nullptr, &rawPath)) &&
            rawPath != nullptr)
        {
            const auto fromKnownFolder = std::filesystem::path(rawPath) / L"AppData" / L"Local";
            CoTaskMemFree(rawPath);
            rawPath = nullptr;
            if (!LooksLikePackageRedirectedLocalAppData(fromKnownFolder))
            {
                return fromKnownFolder;
            }
        }
        else if (rawPath != nullptr)
        {
            CoTaskMemFree(rawPath);
            rawPath = nullptr;
        }

        if (SUCCEEDED(SHGetKnownFolderPath(
                FOLDERID_LocalAppData,
                KF_FLAG_NO_PACKAGE_REDIRECTION,
                nullptr,
                &rawPath)) &&
            rawPath != nullptr)
        {
            const std::filesystem::path unredirected(rawPath);
            CoTaskMemFree(rawPath);
            rawPath = nullptr;
            if (!LooksLikePackageRedirectedLocalAppData(unredirected))
            {
                return unredirected;
            }
        }
        else if (rawPath != nullptr)
        {
            CoTaskMemFree(rawPath);
            rawPath = nullptr;
        }

        if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &rawPath)) ||
            rawPath == nullptr)
        {
            return {};
        }
        const std::filesystem::path fallback(rawPath);
        CoTaskMemFree(rawPath);
        return fallback;
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

    std::filesystem::path GetDefaultLocalStateDirectory()
    {
        const auto localAppData = GetLocalAppDataDirectory();
        return localAppData.empty() ? std::filesystem::path{} : localAppData / L"RightAgent";
    }

    bool IsSettingsPackageRegistered()
    {
        const auto currentPackageFamilyName = GetPackageFamilyNameValue();
        if (currentPackageFamilyName.empty())
        {
            return false;
        }

        const auto settingsPackageFamilyName = rightagent::GetSettingsPackageFamilyName(currentPackageFamilyName);
        if (settingsPackageFamilyName.empty())
        {
            return false;
        }
        if (settingsPackageFamilyName == currentPackageFamilyName)
        {
            return true;
        }

        UINT32 packageCount = 0;
        UINT32 bufferLength = 0;
        const LONG result = GetPackagesByPackageFamily(
            settingsPackageFamilyName.c_str(),
            &packageCount,
            nullptr,
            &bufferLength,
            nullptr);
        return packageCount > 0 && (result == ERROR_INSUFFICIENT_BUFFER || result == ERROR_SUCCESS);
    }

    std::wstring ReadUtf8File(const std::filesystem::path& path)
    {
        std::ifstream stream(path, std::ios::binary);
        if (!stream)
        {
            return {};
        }

        std::string bytes((std::istreambuf_iterator<char>(stream)), std::istreambuf_iterator<char>());
        if (bytes.empty())
        {
            return {};
        }
        if (bytes.size() >= 3 &&
            static_cast<unsigned char>(bytes[0]) == 0xEF &&
            static_cast<unsigned char>(bytes[1]) == 0xBB &&
            static_cast<unsigned char>(bytes[2]) == 0xBF)
        {
            bytes.erase(0, 3);
        }
        return std::wstring(winrt::to_hstring(bytes));
    }

    std::wstring StripJsonComments(std::wstring_view text)
    {
        std::wstring result;
        result.reserve(text.size());
        enum class State
        {
            Normal,
            String,
            LineComment,
            BlockComment
        };
        auto state = State::Normal;
        for (std::size_t index = 0; index < text.size(); ++index)
        {
            const wchar_t current = text[index];
            const wchar_t next = index + 1 < text.size() ? text[index + 1] : L'\0';
            switch (state)
            {
            case State::Normal:
                if (current == L'"')
                {
                    state = State::String;
                    result.push_back(current);
                }
                else if (current == L'/' && next == L'/')
                {
                    state = State::LineComment;
                    ++index;
                }
                else if (current == L'/' && next == L'*')
                {
                    state = State::BlockComment;
                    ++index;
                }
                else
                {
                    result.push_back(current);
                }
                break;
            case State::String:
                result.push_back(current);
                if (current == L'\\' && next != L'\0')
                {
                    result.push_back(next);
                    ++index;
                }
                else if (current == L'"')
                {
                    state = State::Normal;
                }
                break;
            case State::LineComment:
                if (current == L'\n' || current == L'\r')
                {
                    state = State::Normal;
                    result.push_back(current);
                }
                break;
            case State::BlockComment:
                if (current == L'*' && next == L'/')
                {
                    state = State::Normal;
                    ++index;
                }
                break;
            }
        }
        return result;
    }

    std::wstring ReadJsonStringProperty(std::wstring_view json, std::wstring_view key)
    {
        const std::wstring quotedKey = L"\"" + std::wstring(key) + L"\"";
        std::size_t position = 0;
        while ((position = json.find(quotedKey, position)) != std::wstring_view::npos)
        {
            std::size_t cursor = position + quotedKey.size();
            while (cursor < json.size() && std::iswspace(json[cursor]))
            {
                ++cursor;
            }
            if (cursor >= json.size() || json[cursor] != L':')
            {
                ++position;
                continue;
            }
            ++cursor;
            while (cursor < json.size() && std::iswspace(json[cursor]))
            {
                ++cursor;
            }
            if (cursor >= json.size() || json[cursor] != L'"')
            {
                return {};
            }

            std::wstring value;
            ++cursor;
            while (cursor < json.size() && json[cursor] != L'"')
            {
                if (json[cursor] == L'\\' && cursor + 1 < json.size())
                {
                    value.push_back(json[cursor + 1]);
                    cursor += 2;
                    continue;
                }
                value.push_back(json[cursor]);
                ++cursor;
            }
            return value;
        }
        return {};
    }

    std::filesystem::path FindWindowsTerminalSettingsPath()
    {
        const auto localAppData = GetLocalAppDataDirectory();
        if (localAppData.empty())
        {
            return {};
        }

        const std::filesystem::path candidates[] =
        {
            localAppData / L"Packages" / L"Microsoft.WindowsTerminal_8wekyb3d8bbwe" / L"LocalState" / L"settings.json",
            localAppData / L"Packages" / L"Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe" / L"LocalState" / L"settings.json",
            localAppData / L"Packages" / L"Microsoft.WindowsTerminalCanary_8wekyb3d8bbwe" / L"LocalState" / L"settings.json",
            localAppData / L"Microsoft" / L"Windows Terminal" / L"settings.json"
        };
        for (const auto& candidate : candidates)
        {
            std::error_code error;
            if (std::filesystem::is_regular_file(candidate, error))
            {
                return candidate;
            }
        }
        return {};
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

    bool IsInstallRecordAppPresent(const std::filesystem::path& localStateDirectory)
    {
        const auto text = ReadUtf8File(localStateDirectory / rightagent::kInstallRecordFileName);
        if (text.empty())
        {
            return false;
        }

        try
        {
            const auto root = JsonObject::Parse(text);
            const auto appPath = Trim(GetString(root, L"appPath"));
            std::error_code error;
            return !appPath.empty() && std::filesystem::is_regular_file(appPath, error);
        }
        catch (const winrt::hresult_error&)
        {
            return false;
        }
    }

    bool IsProductInstalled(const std::filesystem::path& localStateDirectory)
    {
        // Unpackaged settings/test hosts have no package identity.
        if (GetPackageFamilyNameValue().empty())
        {
            return true;
        }

        if (IsInstallRecordAppPresent(localStateDirectory))
        {
            return true;
        }

        return IsSettingsPackageRegistered();
    }

    bool IsValidHttpUrl(const std::wstring& value)
    {
        const auto lower = ToLower(Trim(value));
        return lower.starts_with(L"https://") || lower.starts_with(L"http://");
    }

    TerminalShell ParseTerminalShell(std::wstring value)
    {
        value = ToLower(Trim(std::move(value)));
        if (value == L"pwsh")
        {
            return TerminalShell::PowerShell7;
        }
        if (value == L"windowspowershell")
        {
            return TerminalShell::WindowsPowerShell;
        }
        if (value == L"cmd")
        {
            return TerminalShell::CommandPrompt;
        }
        return TerminalShell::Automatic;
    }

    MenuMode ParseMenuMode(std::wstring value)
    {
        value = ToLower(Trim(std::move(value)));
        if (value == L"direct")
        {
            return MenuMode::Direct;
        }
        if (value == L"multidirect")
        {
            return MenuMode::MultiDirect;
        }
        return MenuMode::Grouped;
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
    std::wstring GetSettingsPackageFamilyName(const std::wstring_view currentPackageFamilyName)
    {
        const auto separator = currentPackageFamilyName.rfind(L'_');
        if (separator == std::wstring_view::npos || separator == 0 || separator + 1 >= currentPackageFamilyName.size())
        {
            return {};
        }

        auto packageName = std::wstring(currentPackageFamilyName.substr(0, separator));
        constexpr std::wstring_view commandMarker = L".Command";
        const auto marker = packageName.rfind(commandMarker);
        if (marker != std::wstring::npos &&
            marker + commandMarker.size() + 2 == packageName.size() &&
            std::iswdigit(packageName[marker + commandMarker.size()]) &&
            std::iswdigit(packageName[marker + commandMarker.size() + 1]))
        {
            packageName.erase(marker);
        }

        return packageName + std::wstring(currentPackageFamilyName.substr(separator));
    }

    std::filesystem::path GetLocalStateDirectory()
    {
        const auto overridePath = GetEnvironmentValue(L"RIGHTAGENT_SETTINGS_PATH");
        if (!overridePath.empty())
        {
            const std::filesystem::path path(overridePath);
            return path.has_filename() && ToLower(path.filename().wstring()) == L"settings.json" ? path.parent_path() : path;
        }

        return GetDefaultLocalStateDirectory();
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
        // Companion command packages are intentionally independent so Explorer
        // attributes each multi-direct verb at the menu root. If the unpackaged
        // settings app (or a leftover packaged settings identity) is gone, keep
        // any surviving command package inert instead of falling back to defaults.
        const auto localStateDirectory = GetLocalStateDirectory();
        const auto settingsPath = localStateDirectory / kSettingsFileName;
        std::error_code error;
        if (std::filesystem::is_regular_file(settingsPath, error))
        {
            return LoadSettingsFromPath(settingsPath);
        }
        if (!IsProductInstalled(localStateDirectory))
        {
            Settings settings;
            settings.menuEnabled = false;
            return settings;
        }
        return LoadSettingsFromPath(settingsPath);
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
            settings.menuMode = ParseMenuMode(GetString(root, L"menuMode", L"grouped"));
            settings.directAgentId = GetString(root, L"directAgentId");
            settings.terminalShell = ParseTerminalShell(GetString(root, L"terminalShell", L"auto"));
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
            BuiltIn(L"kimi", L"Kimi", L"builtin:kimi", L"kimi", 2, CommandExists(L"kimi")),
            BuiltIn(L"grok", L"Grok", L"builtin:grok", L"grok", 3, CommandExists(L"grok")),
            BuiltIn(L"opencode", L"opencode", L"builtin:opencode", L"opencode", 4, CommandExists(L"opencode")),
            BuiltIn(L"cursor-agent", L"Cursor Agent", L"builtin:cursor", L"cursor-agent", 5, CommandExists(L"cursor-agent"))
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
            std::filesystem::path(localAppData) / L"cursor-agent",
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
        if (key != L"claude" && key != L"codex" && key != L"kimi" && key != L"grok" && key != L"opencode" && key != L"cursor")
        {
            key = L"rightagent";
        }
        return moduleDirectory / L"Assets" / L"Agents" / (key + L".ico");
    }

    std::wstring CanonicalProfileId(std::wstring value)
    {
        value = Trim(std::move(value));
        if (value.size() >= 2 && value.front() == L'{' && value.back() == L'}')
        {
            return ToLower(value.substr(1, value.size() - 2));
        }
        return ToLower(value);
    }

    bool ProfileIdsEqual(std::wstring_view left, std::wstring_view right)
    {
        return CanonicalProfileId(std::wstring(left)) == CanonicalProfileId(std::wstring(right));
    }

    bool ContainsToken(const std::wstring& haystack, const std::wstring& needle)
    {
        return haystack.find(needle) != std::wstring::npos;
    }

    std::wstring ReadWindowsTerminalDefaultProfile(const std::filesystem::path& settingsPath)
    {
        if (settingsPath.empty())
        {
            return {};
        }
        return Trim(ReadJsonStringProperty(StripJsonComments(ReadUtf8File(settingsPath)), L"defaultProfile"));
    }

    std::wstring ResolveWindowsTerminalProfile(const std::wstring_view configuredProfile)
    {
        auto configured = Trim(std::wstring(configuredProfile));
        if (!configured.empty())
        {
            return configured;
        }
        return ReadWindowsTerminalDefaultProfile(FindWindowsTerminalSettingsPath());
    }

    WindowsTerminalProfileCatalog ReadWindowsTerminalProfileCatalog(const std::filesystem::path& settingsPath)
    {
        WindowsTerminalProfileCatalog catalog;
        if (settingsPath.empty())
        {
            return catalog;
        }

        const auto text = StripJsonComments(ReadUtf8File(settingsPath));
        catalog.defaultProfileId = Trim(ReadJsonStringProperty(text, L"defaultProfile"));
        if (text.empty())
        {
            return catalog;
        }

        try
        {
            const auto root = JsonObject::Parse(text);
            if (!root.HasKey(L"profiles"))
            {
                return catalog;
            }

            const auto profilesValue = root.GetNamedValue(L"profiles");
            JsonArray list;
            if (profilesValue.ValueType() == JsonValueType::Array)
            {
                list = profilesValue.GetArray();
            }
            else if (profilesValue.ValueType() == JsonValueType::Object)
            {
                const auto object = profilesValue.GetObject();
                if (object.HasKey(L"list") && object.GetNamedValue(L"list").ValueType() == JsonValueType::Array)
                {
                    list = object.GetNamedArray(L"list");
                }
            }

            for (const auto& value : list)
            {
                if (value.ValueType() != JsonValueType::Object)
                {
                    continue;
                }

                const auto object = value.GetObject();
                WindowsTerminalProfileInfo profile;
                profile.id = Trim(GetString(object, L"guid"));
                profile.name = Trim(GetString(object, L"name"));
                profile.source = Trim(GetString(object, L"source"));
                profile.commandline = Trim(GetString(object, L"commandline"));
                profile.hidden = GetBoolean(object, L"hidden", false);
                if (profile.id.empty())
                {
                    profile.id = profile.name;
                }
                if (!profile.id.empty())
                {
                    catalog.profiles.push_back(std::move(profile));
                }
            }
        }
        catch (const winrt::hresult_error&)
        {
        }

        return catalog;
    }

    WindowsTerminalProfileCatalog LoadWindowsTerminalProfileCatalog()
    {
        return ReadWindowsTerminalProfileCatalog(FindWindowsTerminalSettingsPath());
    }

    const WindowsTerminalProfileInfo* FindWindowsTerminalProfile(
        const WindowsTerminalProfileCatalog& catalog,
        const std::wstring_view idOrName)
    {
        const auto value = Trim(std::wstring(idOrName));
        if (value.empty())
        {
            return nullptr;
        }

        for (const auto& profile : catalog.profiles)
        {
            if (ProfileIdsEqual(profile.id, value))
            {
                return &profile;
            }
        }
        const auto lowered = ToLower(value);
        for (const auto& profile : catalog.profiles)
        {
            if (ToLower(profile.name) == lowered)
            {
                return &profile;
            }
        }
        return nullptr;
    }

    WindowsTerminalProfileInfo ResolveWindowsTerminalLaunchProfile(const std::wstring_view configuredProfile)
    {
        const auto catalog = LoadWindowsTerminalProfileCatalog();
        const auto configured = Trim(std::wstring(configuredProfile));
        if (const auto* profile = FindWindowsTerminalProfile(catalog, configured.empty() ? catalog.defaultProfileId : configured))
        {
            return *profile;
        }

        WindowsTerminalProfileInfo fallback;
        fallback.id = configured.empty() ? catalog.defaultProfileId : configured;
        fallback.name = fallback.id;
        return fallback;
    }

    WindowsTerminalShellFamily ClassifyWindowsTerminalShell(
        const std::wstring_view name,
        const std::wstring_view source,
        const std::wstring_view commandline)
    {
        const auto haystack = ToLower(
            Trim(std::wstring(name)) + L'\n' + Trim(std::wstring(source)) + L'\n' + Trim(std::wstring(commandline)));
        if (ContainsToken(haystack, L"wsl.exe") || ContainsToken(haystack, L"windows.terminal.wsl"))
        {
            return WindowsTerminalShellFamily::Wsl;
        }
        if (ContainsToken(haystack, L"bash.exe")
            || ContainsToken(haystack, L"git bash")
            || ToLower(Trim(std::wstring(source))) == L"git")
        {
            return WindowsTerminalShellFamily::Bash;
        }
        if (ContainsToken(haystack, L"cmd.exe")
            || ContainsToken(haystack, L"command prompt")
            || ContainsToken(haystack, L"命令提示符"))
        {
            return WindowsTerminalShellFamily::CommandPrompt;
        }
        return WindowsTerminalShellFamily::PowerShell;
    }

    std::vector<std::wstring> BuildWindowsTerminalAppendCommandLine(
        const WindowsTerminalShellFamily family,
        const std::wstring_view command,
        const std::wstring_view profileCommandline)
    {
        const auto commandText = std::wstring(command);
        switch (family)
        {
        case WindowsTerminalShellFamily::CommandPrompt:
        {
            const auto lowerCommandline = ToLower(std::wstring(profileCommandline));
            if (ContainsToken(lowerCommandline, L"/k") || ContainsToken(lowerCommandline, L"/c"))
            {
                return {L"&&", commandText};
            }
            return {L"/D", L"/K", commandText};
        }
        case WindowsTerminalShellFamily::Bash:
            return {L"-c", commandText + L"; exec bash -i -l"};
        case WindowsTerminalShellFamily::Wsl:
            return {L"--", L"bash", L"-lc", commandText + L"; exec bash"};
        case WindowsTerminalShellFamily::PowerShell:
        default:
            // Separate tokens so pwsh 7 does not treat the whole suffix as -File.
            // EncodedCommand keeps semicolons out of Windows Terminal's splitter.
            return {L"-NoLogo", L"-NoExit", L"-EncodedCommand", EncodePowerShellCommand(commandText)};
        }
    }
}
