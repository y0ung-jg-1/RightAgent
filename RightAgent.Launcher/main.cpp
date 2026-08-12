#include "NativeSettings.h"
#include "ProcessHelpers.h"

#include <windows.h>
#include <commctrl.h>
#include <shellapi.h>

#include <filesystem>
#include <optional>
#include <string>
#include <utility>
#include <vector>

#pragma comment(linker, "/manifestdependency:\"type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")

namespace
{
    constexpr int kOpenSettingsButton = 100;

    struct LaunchRequest
    {
        std::wstring agentId;
        std::filesystem::path workingDirectory;
    };

    std::wstring FormatWindowsError(const DWORD error)
    {
        PWSTR message = nullptr;
        const DWORD length = FormatMessageW(
            FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr,
            error,
            0,
            reinterpret_cast<PWSTR>(&message),
            0,
            nullptr);
        if (length == 0 || message == nullptr)
        {
            return L"Windows error " + std::to_wstring(error);
        }

        std::wstring result(message, length);
        LocalFree(message);
        while (!result.empty() && (result.back() == L'\r' || result.back() == L'\n' || result.back() == L' '))
        {
            result.pop_back();
        }
        return result;
    }

    void OpenSettings()
    {
        ShellExecuteW(nullptr, L"open", L"rightagent://settings", nullptr, nullptr, SW_SHOWNORMAL);
    }

    void ShowError(const rightagent::Settings& settings, const std::wstring& details)
    {
        const bool chinese = rightagent::IsChinese(settings);
        const wchar_t* instruction = chinese ? L"无法使用 RightAgent 打开" : L"RightAgent could not open this agent";
        const wchar_t* openSettings = chinese ? L"打开设置" : L"Open settings";
        const wchar_t* close = chinese ? L"关闭" : L"Close";
        const TASKDIALOG_BUTTON buttons[] =
        {
            {kOpenSettingsButton, openSettings},
            {IDCANCEL, close}
        };

        TASKDIALOGCONFIG config{};
        config.cbSize = sizeof(config);
        config.dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_SIZE_TO_CONTENT;
        config.pszWindowTitle = L"RightAgent";
        config.pszMainInstruction = instruction;
        config.pszContent = details.c_str();
        config.pszMainIcon = TD_WARNING_ICON;
        config.cButtons = ARRAYSIZE(buttons);
        config.pButtons = buttons;
        config.nDefaultButton = kOpenSettingsButton;

        int selected = 0;
        if (SUCCEEDED(TaskDialogIndirect(&config, &selected, nullptr, nullptr)))
        {
            if (selected == kOpenSettingsButton)
            {
                OpenSettings();
            }
            return;
        }

        MessageBoxW(nullptr, details.c_str(), L"RightAgent", MB_OK | MB_ICONWARNING | MB_SETFOREGROUND);
    }

    std::optional<LaunchRequest> ParseRequest()
    {
        int argumentCount = 0;
        LPWSTR* rawArguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
        if (rawArguments == nullptr)
        {
            return std::nullopt;
        }

        LaunchRequest request;
        for (int index = 1; index < argumentCount; ++index)
        {
            const std::wstring_view argument(rawArguments[index]);
            if (argument == L"--agent" && index + 1 < argumentCount)
            {
                request.agentId = rawArguments[++index];
            }
            else if (argument == L"--cwd" && index + 1 < argumentCount)
            {
                request.workingDirectory = rawArguments[++index];
            }
        }
        LocalFree(rawArguments);

        std::error_code error;
        if (request.agentId.empty()
            || request.workingDirectory.empty()
            || !std::filesystem::is_directory(request.workingDirectory, error))
        {
            return std::nullopt;
        }
        return request;
    }

    std::filesystem::path FindOnPath(const wchar_t* executable)
    {
        const DWORD required = SearchPathW(nullptr, executable, nullptr, 0, nullptr, nullptr);
        if (required == 0)
        {
            return {};
        }

        std::wstring path(required + 1, L'\0');
        const DWORD written = SearchPathW(nullptr, executable, nullptr, static_cast<DWORD>(path.size()), path.data(), nullptr);
        if (written == 0 || written >= path.size())
        {
            return {};
        }
        path.resize(written);
        return path;
    }

    std::filesystem::path GetEnvironmentPath(const wchar_t* name)
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

    bool FileExists(const std::filesystem::path& path)
    {
        std::error_code error;
        return !path.empty() && std::filesystem::exists(path, error);
    }

    std::filesystem::path FindWindowsTerminal()
    {
        if (const auto terminal = FindOnPath(L"wt.exe"); !terminal.empty())
        {
            return terminal;
        }
        if (const auto terminal = FindOnPath(L"wt"); !terminal.empty())
        {
            return terminal;
        }

        const auto alias = GetEnvironmentPath(L"LOCALAPPDATA") / L"Microsoft" / L"WindowsApps" / L"wt.exe";
        return FileExists(alias) ? alias : std::filesystem::path{};
    }

    std::filesystem::path FindPowerShell7()
    {
        if (const auto executable = FindOnPath(L"pwsh.exe"); !executable.empty())
        {
            return executable;
        }

        const std::filesystem::path candidates[] =
        {
            GetEnvironmentPath(L"ProgramFiles") / L"PowerShell" / L"7" / L"pwsh.exe",
            GetEnvironmentPath(L"LOCALAPPDATA") / L"Microsoft" / L"WindowsApps" / L"pwsh.exe"
        };
        for (const auto& candidate : candidates)
        {
            if (FileExists(candidate))
            {
                return candidate;
            }
        }
        return {};
    }

    std::filesystem::path FindWindowsPowerShell()
    {
        wchar_t windowsDirectory[MAX_PATH]{};
        const UINT length = GetWindowsDirectoryW(windowsDirectory, ARRAYSIZE(windowsDirectory));
        if (length > 0 && length < ARRAYSIZE(windowsDirectory))
        {
            const auto executable = std::filesystem::path(windowsDirectory) / L"System32" / L"WindowsPowerShell" / L"v1.0" / L"powershell.exe";
            if (FileExists(executable))
            {
                return executable;
            }
        }
        return FindOnPath(L"powershell.exe");
    }

    std::filesystem::path FindCommandPrompt()
    {
        wchar_t systemDirectory[MAX_PATH]{};
        const UINT length = GetSystemDirectoryW(systemDirectory, ARRAYSIZE(systemDirectory));
        if (length > 0 && length < ARRAYSIZE(systemDirectory))
        {
            const auto executable = std::filesystem::path(systemDirectory) / L"cmd.exe";
            if (FileExists(executable))
            {
                return executable;
            }
        }
        return FindOnPath(L"cmd.exe");
    }

    struct ShellLaunch
    {
        std::filesystem::path executable;
        std::vector<std::wstring> arguments;
    };

    std::optional<ShellLaunch> ResolveShellLaunch(
        const rightagent::TerminalShell configuredShell,
        const std::wstring& command)
    {
        std::filesystem::path executable;
        switch (configuredShell)
        {
        case rightagent::TerminalShell::Automatic:
            executable = FindPowerShell7();
            if (executable.empty())
            {
                executable = FindWindowsPowerShell();
            }
            break;
        case rightagent::TerminalShell::PowerShell7:
            executable = FindPowerShell7();
            break;
        case rightagent::TerminalShell::WindowsPowerShell:
            executable = FindWindowsPowerShell();
            break;
        case rightagent::TerminalShell::CommandPrompt:
            executable = FindCommandPrompt();
            break;
        }

        if (executable.empty())
        {
            return std::nullopt;
        }
        if (configuredShell == rightagent::TerminalShell::CommandPrompt)
        {
            return ShellLaunch{std::move(executable), {L"/D", L"/K", command}};
        }
        // Windows Terminal treats semicolons as its own command separators. Base64 keeps
        // the PowerShell script opaque until the selected shell receives and decodes it.
        return ShellLaunch{
            std::move(executable),
            {L"-NoLogo", L"-NoExit", L"-EncodedCommand", rightagent::EncodePowerShellCommand(command)}};
    }

    int LaunchTerminalAgent(
        const rightagent::Settings& settings,
        const rightagent::AgentDefinition& agent,
        const std::filesystem::path& workingDirectory)
    {
        const auto terminal = FindWindowsTerminal();
        if (terminal.empty())
        {
            ShowError(settings, rightagent::IsChinese(settings)
                ? L"未找到 Windows Terminal（wt.exe）。请先安装或修复 Windows Terminal。"
                : L"Windows Terminal (wt.exe) was not found. Install or repair Windows Terminal first.");
            return 3;
        }

        const auto simpleToken = rightagent::FirstSimpleCommandToken(agent.actionValue);
        if (!simpleToken.empty() && !rightagent::CommandExists(simpleToken))
        {
            const auto message = rightagent::IsChinese(settings)
                ? L"找不到命令“" + simpleToken + L"”。请在 RightAgent 设置中修改该 Agent 的实际命令。"
                : L"The command “" + simpleToken + L"” was not found. Update this agent's command in RightAgent settings.";
            ShowError(settings, message);
            return 4;
        }

        const auto shell = ResolveShellLaunch(settings.terminalShell, agent.actionValue);
        if (!shell)
        {
            const auto message = rightagent::IsChinese(settings)
                ? L"找不到所选的命令 Shell。请安装该 Shell，或在 RightAgent 设置中选择其他选项。"
                : L"The selected command shell was not found. Install it or choose another shell in RightAgent settings.";
            ShowError(settings, message);
            return 5;
        }

        std::vector<std::wstring> arguments = {L"-w", L"new", L"new-tab"};
        if (!settings.terminalProfile.empty())
        {
            arguments.emplace_back(L"-p");
            arguments.push_back(settings.terminalProfile);
        }
        arguments.emplace_back(L"-d");
        arguments.push_back(workingDirectory.wstring());
        arguments.push_back(shell->executable.wstring());
        arguments.insert(arguments.end(), shell->arguments.begin(), shell->arguments.end());

        DWORD error = ERROR_SUCCESS;
        if (!rightagent::LaunchProcess(terminal, arguments, workingDirectory, CREATE_NEW_PROCESS_GROUP, &error))
        {
            const auto prefix = rightagent::IsChinese(settings) ? L"无法启动 Windows Terminal：" : L"Could not start Windows Terminal: ";
            ShowError(settings, prefix + FormatWindowsError(error));
            return 5;
        }
        return 0;
    }

    int LaunchUrl(
        const rightagent::Settings& settings,
        const rightagent::AgentDefinition& agent,
        const std::filesystem::path& workingDirectory)
    {
        const auto result = reinterpret_cast<INT_PTR>(ShellExecuteW(
            nullptr,
            L"open",
            agent.actionValue.c_str(),
            nullptr,
            workingDirectory.c_str(),
            SW_SHOWNORMAL));
        if (result <= 32)
        {
            const auto prefix = rightagent::IsChinese(settings) ? L"无法打开网页：" : L"Could not open the web page: ";
            ShowError(settings, prefix + agent.actionValue);
            return 6;
        }
        return 0;
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    const auto request = ParseRequest();
    const auto settings = rightagent::LoadSettings();
    if (!request)
    {
        ShowError(settings, rightagent::IsChinese(settings)
            ? L"启动参数无效，或目标目录不是本地文件夹。"
            : L"The launch arguments are invalid, or the target is not a local folder.");
        return 2;
    }

    const auto* agent = rightagent::FindEnabledAgent(settings, request->agentId);
    if (agent == nullptr)
    {
        ShowError(settings, rightagent::IsChinese(settings)
            ? L"找不到已启用的 Agent。配置可能已损坏，或该 Agent 已被关闭。"
            : L"No enabled agent was found. The configuration may be damaged, or this agent was disabled.");
        return 2;
    }

    return agent->actionType == rightagent::ActionType::Url
        ? LaunchUrl(settings, *agent, request->workingDirectory)
        : LaunchTerminalAgent(settings, *agent, request->workingDirectory);
}
