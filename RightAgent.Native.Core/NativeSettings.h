#pragma once

#include <filesystem>
#include <string>
#include <string_view>
#include <vector>

namespace rightagent
{
    inline constexpr int kSettingsSchemaVersion = 1;
    inline constexpr wchar_t kSettingsFileName[] = L"settings.json";

    enum class MenuMode
    {
        Grouped,
        Direct
    };

    enum class ActionType
    {
        TerminalCommand,
        Url
    };

    enum class TerminalShell
    {
        Automatic,
        PowerShell7,
        WindowsPowerShell,
        CommandPrompt
    };

    struct AgentDefinition
    {
        std::wstring id;
        std::wstring name;
        bool enabled{};
        int sort{};
        std::wstring iconPath{L"builtin:rightagent"};
        ActionType actionType{ActionType::TerminalCommand};
        std::wstring actionValue;
    };

    struct Settings
    {
        int schemaVersion{kSettingsSchemaVersion};
        bool menuEnabled{true};
        std::wstring language{L"system"};
        MenuMode menuMode{MenuMode::Grouped};
        std::wstring directAgentId;
        TerminalShell terminalShell{TerminalShell::Automatic};
        std::wstring terminalProfile;
        std::vector<AgentDefinition> agents;
    };

    [[nodiscard]] std::filesystem::path GetLocalStateDirectory();
    [[nodiscard]] std::filesystem::path GetSettingsPath();
    [[nodiscard]] Settings LoadSettings();
    [[nodiscard]] Settings LoadSettingsFromPath(const std::filesystem::path& path);
    [[nodiscard]] Settings CreateDefaultSettings();
    [[nodiscard]] bool CommandExists(std::wstring_view command);
    [[nodiscard]] bool IsChinese(const Settings& settings);
    [[nodiscard]] const AgentDefinition* FindEnabledAgent(const Settings& settings, std::wstring_view id);
    [[nodiscard]] const AgentDefinition* FindDirectAgent(const Settings& settings);
    [[nodiscard]] std::filesystem::path ResolveIconPath(
        std::wstring_view iconPath,
        const std::filesystem::path& moduleDirectory,
        const std::filesystem::path& localStateDirectory = GetLocalStateDirectory());
}
