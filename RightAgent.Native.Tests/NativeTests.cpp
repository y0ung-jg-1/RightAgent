#include "NativeSettings.h"
#include "ProcessHelpers.h"
#include "ShellExports.h"

#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <winrt/base.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <chrono>
#include <stdexcept>
#include <vector>

namespace
{
    void Expect(const bool condition, const char* message)
    {
        if (!condition)
        {
            throw std::runtime_error(message);
        }
    }

    void VerifyRoundTrip(const std::vector<std::wstring>& arguments)
    {
        const auto commandLine = rightagent::BuildCommandLine(arguments);
        int count = 0;
        LPWSTR* parsed = CommandLineToArgvW(commandLine.c_str(), &count);
        Expect(parsed != nullptr, "CommandLineToArgvW failed");
        Expect(count == static_cast<int>(arguments.size()), "Argument count changed during quoting");
        for (int index = 0; index < count; ++index)
        {
            Expect(arguments[index] == parsed[index], "Argument content changed during quoting");
        }
        LocalFree(parsed);
    }

    void TestQuoting()
    {
        VerifyRoundTrip({L"RightAgent.Launcher.exe", L"--cwd", L"C:\\普通目录\\space & (test)\\", L"--agent", L"kimi"});
        VerifyRoundTrip({L"", L"quote\"inside", L"trailing\\", L"single'quote"});
        Expect(
            rightagent::EncodePowerShellCommand(L"Write-Output hello; exit")
                == L"VwByAGkAdABlAC0ATwB1AHQAcAB1AHQAIABoAGUAbABsAG8AOwAgAGUAeABpAHQA",
            "PowerShell command encoding changed");
    }

    void TestSettingsParsing()
    {
        const auto root = std::filesystem::temp_directory_path() / L"RightAgent.Native.Tests" / std::to_wstring(GetCurrentProcessId());
        std::filesystem::create_directories(root);
        const auto path = root / L"settings.json";
        {
            std::ofstream output(path, std::ios::binary);
            output << R"({
  "schemaVersion": 1,
  "language": "zh-CN",
  "menuMode": "direct",
  "directAgentId": "web",
  "terminalShell": "cmd",
  "agents": [
    {"id":"web","name":"Kimi Web","enabled":true,"sort":5,"iconPath":"local:Icons/web.ico","action":{"type":"url","value":"https://www.kimi.com"}},
    {"id":"bad","name":"Bad URL","enabled":true,"sort":2,"iconPath":"local:../outside.ico","action":{"type":"url","value":"file:///C:/secret.txt"}}
  ]
})";
        }

        const auto settings = rightagent::LoadSettingsFromPath(path);
        Expect(settings.menuMode == rightagent::MenuMode::Direct, "Direct mode was not parsed");
        Expect(settings.terminalShell == rightagent::TerminalShell::CommandPrompt, "Terminal shell was not parsed");
        Expect(rightagent::FindDirectAgent(settings) != nullptr, "Direct agent was not resolved");
        Expect(rightagent::FindDirectAgent(settings)->id == L"web", "Wrong direct agent");
        Expect(settings.agents.size() == 2, "Agent count changed");
        Expect(!settings.agents[0].enabled, "Unsafe URL should be disabled after sorting");
        Expect(settings.agents[0].iconPath == L"builtin:rightagent", "Unsafe icon path should be replaced");
        const auto defaultSettings = rightagent::CreateDefaultSettings();
        Expect(defaultSettings.terminalShell == rightagent::TerminalShell::Automatic, "Default terminal shell should be automatic");
        const auto cursor = std::find_if(defaultSettings.agents.begin(), defaultSettings.agents.end(), [](const rightagent::AgentDefinition& agent)
        {
            return agent.id == L"cursor-agent";
        });
        Expect(cursor != defaultSettings.agents.end(), "Cursor Agent default is missing");
        Expect(cursor->iconPath == L"builtin:cursor", "Cursor Agent default icon changed");
        Expect(cursor->actionValue == L"cursor-agent", "Cursor Agent command changed");
        Expect(
            rightagent::ResolveIconPath(L"builtin:cursor", L"C:\\RightAgent", L"C:\\LocalState")
                == std::filesystem::path(L"C:\\RightAgent\\Assets\\Agents\\cursor.ico"),
            "Cursor built-in icon was not resolved");

        const auto benchmarkStart = std::chrono::steady_clock::now();
        constexpr int iterations = 200;
        for (int iteration = 0; iteration < iterations; ++iteration)
        {
            Expect(rightagent::LoadSettingsFromPath(path).agents.size() == 2, "Repeated settings load failed");
        }
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - benchmarkStart);
        const auto averageMilliseconds = static_cast<double>(elapsed.count()) / iterations;
        Expect(averageMilliseconds < 50.0, "Average local settings load exceeded 50 ms");

        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output << R"({"schemaVersion":1,"agents":[]})";
        }
        Expect(rightagent::LoadSettingsFromPath(path).agents.empty(),
            "Settings without agents should not invent built-in commands");
        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
        }
        Expect(rightagent::LoadSettingsFromPath(path).agents.empty(),
            "A missing settings payload should hide the menu instead of probing PATH");

        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output << R"({
  "schemaVersion": 1,
  "menuMode": "multiDirect",
  "agents": [
    {"id":"disabled","name":"Disabled","enabled":false,"sort":0,"action":{"type":"terminalCommand","value":"disabled"}},
    {"id":"codex","name":"Codex","enabled":true,"sort":1,"action":{"type":"terminalCommand","value":"codex"}},
    {"id":"kimi","name":"Kimi","enabled":true,"sort":2,"action":{"type":"terminalCommand","value":"kimi"}}
  ]
})";
        }
        const auto multiDirectSettings = rightagent::LoadSettingsFromPath(path);
        Expect(multiDirectSettings.menuMode == rightagent::MenuMode::MultiDirect, "Multi-direct mode was not parsed");
        Expect(rightagent::FindEnabledAgent(multiDirectSettings, L"codex") != nullptr, "First multi-direct agent was not resolved");
        Expect(rightagent::FindEnabledAgent(multiDirectSettings, L"kimi") != nullptr, "Second multi-direct agent was not resolved");
        Expect(rightagent::FindEnabledAgent(multiDirectSettings, L"disabled") == nullptr, "Disabled multi-direct agent should stay hidden");

        std::error_code error;
        std::filesystem::remove_all(root, error);
    }

    void TestUnpackagedSettingsPath()
    {
        Expect(
            rightagent::GetSettingsPackageFamilyName(L"RightAgent_fqe37b9yktg1e") == L"RightAgent_fqe37b9yktg1e",
            "Main package family name changed while resolving settings");
        Expect(
            rightagent::GetSettingsPackageFamilyName(L"RightAgent.Command00_fqe37b9yktg1e") == L"RightAgent_fqe37b9yktg1e",
            "Release command package did not resolve the main package settings family");
        Expect(
            rightagent::GetSettingsPackageFamilyName(L"RightAgent.Dev.Command15_123456789abcd") == L"RightAgent.Dev_123456789abcd",
            "Development command package did not resolve the main package settings family");
        Expect(
            rightagent::GetSettingsPackageFamilyName(L"invalid") == L"",
            "Malformed package family names must not resolve a settings family");

        const auto root = std::filesystem::temp_directory_path() / L"RightAgent.SettingsPath.Tests" / std::to_wstring(GetCurrentProcessId());
        const auto settingsPath = root / L"settings.json";
        SetEnvironmentVariableW(L"RIGHTAGENT_SETTINGS_PATH", settingsPath.c_str());
        Expect(rightagent::GetLocalStateDirectory() == root, "Settings override directory was not used");
        Expect(rightagent::GetSettingsPath() == settingsPath, "Settings override file was not used");
        SetEnvironmentVariableW(L"RIGHTAGENT_SETTINGS_PATH", nullptr);

        const auto unpackagedPath = rightagent::GetSettingsPath();
        Expect(
            unpackagedPath.filename() == L"settings.json" && unpackagedPath.parent_path().filename() == L"RightAgent",
            "Unpackaged settings should use the LocalAppData RightAgent fallback");
        wchar_t userProfile[MAX_PATH]{};
        Expect(GetEnvironmentVariableW(L"USERPROFILE", userProfile, MAX_PATH) > 0,
            "USERPROFILE is required to locate unpackaged settings");
        const std::filesystem::path expectedSettingsPath =
            std::filesystem::path(userProfile) / L"AppData" / L"Local" / L"RightAgent" / L"settings.json";
        Expect(
            unpackagedPath == expectedSettingsPath,
            "Settings must use the real user LocalAppData, not a packaged LocalCache redirect");
    }

    void TestUtf8BomSettings()
    {
        const auto root = std::filesystem::temp_directory_path() / L"RightAgent.BomSettings.Tests" / std::to_wstring(GetCurrentProcessId());
        std::error_code error;
        std::filesystem::create_directories(root, error);
        {
            std::ofstream output(root / L"settings.json", std::ios::binary | std::ios::trunc);
            output << "\xEF\xBB\xBF" << R"({
  "schemaVersion": 1,
  "menuEnabled": true,
  "language": "en-US",
  "menuMode": "direct",
  "directAgentId": "codex",
  "agents": [
    {"id":"codex","name":"Codex","enabled":true,"sort":0,"iconPath":"builtin:codex","action":{"type":"terminalCommand","value":"codex"}}
  ]
})";
        }
        {
            std::ofstream output(root / L"install.json", std::ios::binary | std::ios::trunc);
            output << "\xEF\xBB\xBF" << R"({"packageName":"RightAgent","publisher":"CN=RightAgent","appPath":"C:\\Windows\\System32\\notepad.exe","version":"1.1.4.0"})";
        }
        SetEnvironmentVariableW(L"RIGHTAGENT_SETTINGS_PATH", (root / L"settings.json").c_str());
        const auto settings = rightagent::LoadSettings();
        SetEnvironmentVariableW(L"RIGHTAGENT_SETTINGS_PATH", nullptr);
        std::filesystem::remove_all(root, error);
        Expect(settings.menuEnabled, "UTF-8 BOM settings must not disable the menu");
        Expect(rightagent::FindDirectAgent(settings) != nullptr &&
                rightagent::FindDirectAgent(settings)->id == L"codex",
            "UTF-8 BOM settings must still parse the selected agent");
    }

    void TestSimpleTokenDetection()
    {
        Expect(rightagent::FirstSimpleCommandToken(L"  kimi web") == L"kimi", "Simple command token not detected");
        Expect(rightagent::FirstSimpleCommandToken(L"& 'C:\\Tools\\agent.exe'").empty(), "Complex command should not be prevalidated");
    }

    void TestWindowsTerminalDefaultProfile()
    {
        Expect(rightagent::ReadWindowsTerminalDefaultProfile({}).empty(),
            "An empty Terminal settings path should not invent a profile");
        Expect(
            rightagent::ReadWindowsTerminalDefaultProfile(L"C:\\RightAgent.Missing\\settings.json").empty(),
            "A missing Terminal settings file should not invent a profile");
        Expect(rightagent::ResolveWindowsTerminalProfile(L"  Ubuntu  ") == L"Ubuntu",
            "An explicit Terminal profile must win over the default");

        const auto root = std::filesystem::temp_directory_path()
            / L"RightAgent.WindowsTerminal.Tests" / std::to_wstring(GetCurrentProcessId());
        std::filesystem::create_directories(root);
        const auto path = root / L"settings.json";
        {
            std::ofstream output(path, std::ios::binary);
            output << "{\n"
                "// Startup default\n"
                "  \"defaultProfile\": \"{574e775e-4f2a-5b96-ac1e-a2962a402336}\",\n"
                "  \"profiles\": { \"list\": [] }\n"
                "}\n";
        }
        Expect(
            rightagent::ReadWindowsTerminalDefaultProfile(path)
                == L"{574e775e-4f2a-5b96-ac1e-a2962a402336}",
            "JSONC Terminal settings must still yield defaultProfile");

        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output << "{ \"name\": \"PowerShell\" }\n";
        }
        Expect(rightagent::ReadWindowsTerminalDefaultProfile(path).empty(),
            "Terminal settings without defaultProfile should yield an empty profile");

        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output << R"({
  "defaultProfile": "{574e775e-4f2a-5b96-ac1e-a2962a402336}",
  "profiles": {
    "list": [
      { "guid": "{61c54bbd-c2c6-5271-96e7-009a87ff44bf}", "name": "Windows PowerShell", "commandline": "powershell.exe" },
      { "guid": "{0caa0dad-35be-5f56-a8ff-afceeeaa6101}", "name": "Command Prompt", "commandline": "cmd.exe" },
      { "guid": "{2ece5bfe-50ed-5f3a-ab87-5cd4baafed2b}", "name": "Git Bash", "source": "Git", "commandline": "bash.exe -i -l" },
      { "guid": "{332430fd-5b8f-5556-9c97-4d1e16e2b6c2}", "name": "Developer Command Prompt for VS 2022", "source": "Windows.Terminal.VisualStudio" },
      { "guid": "{574e775e-4f2a-5b96-ac1e-a2962a402336}", "name": "PowerShell", "source": "Windows.Terminal.PowershellCore" }
    ]
  }
})";
        }
        const auto catalog = rightagent::ReadWindowsTerminalProfileCatalog(path);
        Expect(catalog.profiles.size() == 5, "Terminal profile catalog count changed");
        const auto* powershell = rightagent::FindWindowsTerminalProfile(catalog, L"PowerShell");
        Expect(powershell != nullptr && powershell->source == L"Windows.Terminal.PowershellCore",
            "PowerShell profile was not resolved by name");
        Expect(
            rightagent::ClassifyWindowsTerminalShell(powershell->name, powershell->source, powershell->commandline)
                == rightagent::WindowsTerminalShellFamily::PowerShell,
            "PowerShell Core should stay a PowerShell family profile");
        Expect(
            rightagent::ClassifyWindowsTerminalShell(L"Git Bash", L"Git", L"bash.exe -i -l")
                == rightagent::WindowsTerminalShellFamily::Bash,
            "Git Bash should classify as Bash");
        Expect(
            rightagent::ClassifyWindowsTerminalShell(L"Developer Command Prompt for VS 2022", L"Windows.Terminal.VisualStudio", L"")
                == rightagent::WindowsTerminalShellFamily::CommandPrompt,
            "VS Developer Command Prompt should classify as CMD");
        Expect(
            rightagent::ClassifyWindowsTerminalShell(L"Ubuntu", L"Windows.Terminal.Wsl", L"wsl.exe -d Ubuntu")
                == rightagent::WindowsTerminalShellFamily::Wsl,
            "WSL should classify as Wsl");
        Expect(
            rightagent::BuildWindowsTerminalAppendCommandLine(
                rightagent::WindowsTerminalShellFamily::CommandPrompt, L"hostname", L"cmd.exe")
                == std::vector<std::wstring>({L"/D", L"/K", L"hostname"}),
            "A bare CMD profile should keep the agent alive with /K");
        Expect(
            rightagent::BuildWindowsTerminalAppendCommandLine(
                rightagent::WindowsTerminalShellFamily::CommandPrompt, L"hostname", L"C:/kits/cmd.exe")
                == std::vector<std::wstring>({L"/D", L"/K", L"hostname"}),
            "A CMD path that contains /c must not be treated as /c");
        Expect(
            rightagent::BuildWindowsTerminalAppendCommandLine(
                rightagent::WindowsTerminalShellFamily::CommandPrompt,
                L"hostname",
                L"cmd.exe /k VsDevCmd.bat")
                == std::vector<std::wstring>({L"&&", L"hostname"}),
            "A VS CMD profile should append the agent after VsDevCmd");
        Expect(
            rightagent::BuildWindowsTerminalAppendCommandLine(
                rightagent::WindowsTerminalShellFamily::Bash, L"hostname", L"bash.exe -i -l")
                == std::vector<std::wstring>({L"-c", L"hostname; exec bash -i -l"}),
            "Bash should run the agent then keep an interactive shell");
        const auto powershellAppend = rightagent::BuildWindowsTerminalAppendCommandLine(
            rightagent::WindowsTerminalShellFamily::PowerShell, L"hostname", L"");
        Expect(
            powershellAppend
                == std::vector<std::wstring>(
                    {L"-NoLogo", L"-NoExit", L"-EncodedCommand", rightagent::EncodePowerShellCommand(L"hostname")}),
            "PowerShell profiles should append encoded keep-alive flags as separate tokens");

        std::error_code error;
        std::filesystem::remove_all(root, error);
    }

    void TestShellComSurface()
    {
        const auto root = std::filesystem::temp_directory_path() / L"RightAgent.Shell.Tests" / std::to_wstring(GetCurrentProcessId());
        std::filesystem::create_directories(root);
        const auto settingsPath = root / L"settings.json";
        {
            std::ofstream output(settingsPath, std::ios::binary);
            output << R"({
  "schemaVersion": 1,
  "language": "en-US",
  "menuMode": "grouped",
  "directAgentId": "codex",
  "agents": [
    {"id":"codex","name":"Codex","enabled":true,"sort":0,"iconPath":"builtin:codex","action":{"type":"terminalCommand","value":"codex"}},
    {"id":"kimi","name":"Kimi","enabled":true,"sort":1,"iconPath":"builtin:kimi","action":{"type":"url","value":"https://www.kimi.com"}}
  ]
})";
        }
        SetEnvironmentVariableW(L"RIGHTAGENT_SETTINGS_PATH", settingsPath.c_str());

        const auto shellPath = rightagent::GetModuleDirectory(GetModuleHandleW(nullptr)) / L"RightAgent.Shell.dll";
        HMODULE shell = LoadLibraryW(shellPath.c_str());
        Expect(shell != nullptr, "Could not load RightAgent.Shell.dll");

        using GetClassObject = HRESULT(__stdcall*)(const CLSID&, const IID&, void**);
        const auto getClassObject = reinterpret_cast<GetClassObject>(GetProcAddress(shell, "DllGetClassObject"));
        Expect(getClassObject != nullptr, "DllGetClassObject export is missing");

        const auto createRootCommand = [&](const std::size_t slot)
        {
            Expect(slot < RightAgentExplorerCommandSlotCount, "Explorer command slot is out of range");
            IClassFactory* slotFactory = nullptr;
            Expect(SUCCEEDED(getClassObject(
                CLSID_RightAgentExplorerCommandSlots[slot],
                IID_PPV_ARGS(&slotFactory))), "Could not create shell class factory");
            IExplorerCommand* result = nullptr;
            Expect(SUCCEEDED(slotFactory->CreateInstance(nullptr, IID_PPV_ARGS(&result))),
                "Could not create root explorer command");
            slotFactory->Release();
            return result;
        };

        IShellItem* folderItem = nullptr;
        Expect(SUCCEEDED(SHCreateItemFromParsingName(root.c_str(), nullptr, IID_PPV_ARGS(&folderItem))),
            "Could not create test folder shell item");
        IShellItemArray* selection = nullptr;
        Expect(SUCCEEDED(SHCreateShellItemArrayFromShellItem(folderItem, IID_PPV_ARGS(&selection))),
            "Could not create test folder selection");
        folderItem->Release();

        IExplorerCommand* command = createRootCommand(0);

        PWSTR title = nullptr;
        Expect(SUCCEEDED(command->GetTitle(nullptr, &title)), "Root title failed");
        Expect(std::wstring(title) == L"Open with RightAgent", "Unexpected grouped root title");
        CoTaskMemFree(title);

        EXPCMDFLAGS flags = ECF_DEFAULT;
        Expect(SUCCEEDED(command->GetFlags(&flags)) && (flags & ECF_HASSUBCOMMANDS) != 0, "Grouped flag is missing");
        IEnumExplorerCommand* enumerator = nullptr;
        Expect(SUCCEEDED(command->EnumSubCommands(&enumerator)), "Could not enumerate subcommands");
        IExplorerCommand* child = nullptr;
        ULONG fetched = 0;
        Expect(enumerator->Next(1, &child, &fetched) == S_OK && fetched == 1, "First subcommand is missing");
        title = nullptr;
        Expect(SUCCEEDED(child->GetTitle(nullptr, &title)) && std::wstring(title) == L"Codex", "Unexpected child title");
        CoTaskMemFree(title);
        child->Release();
        enumerator->Release();
        command->Release();

        {
            std::ofstream output(settingsPath, std::ios::binary | std::ios::trunc);
            output << R"({
  "schemaVersion": 1,
  "language": "en-US",
  "menuMode": "direct",
  "directAgentId": "codex",
  "agents": [
    {"id":"codex","name":"Codex","enabled":true,"sort":0,"iconPath":"builtin:codex","action":{"type":"terminalCommand","value":"codex"}},
    {"id":"kimi","name":"Kimi","enabled":true,"sort":1,"iconPath":"builtin:kimi","action":{"type":"url","value":"https://www.kimi.com"}}
  ]
})";
        }

        command = createRootCommand(0);
        title = nullptr;
        Expect(SUCCEEDED(command->GetTitle(nullptr, &title)) && std::wstring(title) == L"Open with Codex",
            "Unexpected direct-mode title");
        CoTaskMemFree(title);
        flags = ECF_HASSUBCOMMANDS;
        Expect(SUCCEEDED(command->GetFlags(&flags)) && flags == ECF_DEFAULT,
            "Direct mode should expose one invokable root command");
        IEnumExplorerCommand* directEnumerator = nullptr;
        Expect(command->EnumSubCommands(&directEnumerator) == E_NOTIMPL && directEnumerator == nullptr,
            "Direct mode should not enumerate child commands");
        command->Release();

        IExplorerCommand* hiddenDirectSlot = createRootCommand(1);
        EXPCMDSTATE state = ECS_ENABLED;
        Expect(SUCCEEDED(hiddenDirectSlot->GetState(selection, FALSE, &state)) && state == ECS_HIDDEN,
            "Non-primary slots must stay hidden in single-direct mode");
        title = nullptr;
        Expect(hiddenDirectSlot->GetTitle(nullptr, &title) == E_FAIL && title == nullptr,
            "Hidden slots must not advertise a title");
        hiddenDirectSlot->Release();

        {
            std::ofstream output(settingsPath, std::ios::binary | std::ios::trunc);
            output << R"({
  "schemaVersion": 1,
  "menuEnabled": false,
  "language": "en-US",
  "menuMode": "direct",
  "directAgentId": "codex",
  "agents": [
    {"id":"codex","name":"Codex","enabled":true,"sort":0,"iconPath":"builtin:codex","action":{"type":"terminalCommand","value":"codex"}}
  ]
})";
        }
        command = createRootCommand(0);
        Expect(command->Invoke(selection, nullptr) == HRESULT_FROM_WIN32(ERROR_ACCESS_DISABLED_BY_POLICY),
            "A cached command must not launch while the menu is disabled");
        command->Release();

        {
            std::ofstream output(settingsPath, std::ios::binary | std::ios::trunc);
            output << R"({
  "schemaVersion": 1,
  "language": "en-US",
  "menuMode": "multiDirect",
  "agents": [
    {"id":"codex","name":"Codex","enabled":true,"sort":0,"iconPath":"builtin:codex","action":{"type":"terminalCommand","value":"codex"}},
    {"id":"kimi","name":"Kimi","enabled":true,"sort":1,"iconPath":"builtin:kimi","action":{"type":"url","value":"https://www.kimi.com"}}
  ]
})";
        }

        for (std::size_t slot = 0; slot < RightAgentExplorerCommandSlotCount; ++slot)
        {
            IClassFactory* registeredFactory = nullptr;
            Expect(SUCCEEDED(getClassObject(
                CLSID_RightAgentExplorerCommandSlots[slot],
                IID_PPV_ARGS(&registeredFactory))), "A multi-direct class slot is not registered");
            registeredFactory->Release();
        }

        for (std::size_t slot = 0; slot < 2; ++slot)
        {
            command = createRootCommand(slot);
            title = nullptr;
            const std::wstring expectedTitle = slot == 0 ? L"Open with Codex" : L"Open with Kimi";
            Expect(SUCCEEDED(command->GetTitle(nullptr, &title)) && std::wstring(title) == expectedTitle,
                "Unexpected multi-direct root title");
            CoTaskMemFree(title);
            flags = ECF_HASSUBCOMMANDS;
            Expect(SUCCEEDED(command->GetFlags(&flags)) && flags == ECF_DEFAULT,
                "Multi-direct slots must be independent invokable root commands");
            state = ECS_HIDDEN;
            Expect(SUCCEEDED(command->GetState(selection, FALSE, &state)) && state == ECS_ENABLED,
                "Enabled multi-direct root command was not visible");
            GUID canonicalName{};
            Expect(SUCCEEDED(command->GetCanonicalName(&canonicalName))
                && canonicalName == CLSID_RightAgentExplorerCommandSlots[slot],
                "Multi-direct root command canonical name changed");
            enumerator = nullptr;
            Expect(command->EnumSubCommands(&enumerator) == E_NOTIMPL && enumerator == nullptr,
                "Multi-direct root commands must not enumerate children");
            command->Release();
        }

        IExplorerCommand* unusedSlot = createRootCommand(2);
        state = ECS_ENABLED;
        Expect(SUCCEEDED(unusedSlot->GetState(selection, FALSE, &state)) && state == ECS_HIDDEN,
            "Unused multi-direct slots must stay hidden");
        title = nullptr;
        Expect(unusedSlot->GetTitle(nullptr, &title) == E_FAIL && title == nullptr,
            "Unused multi-direct slots must not advertise a title");
        PWSTR unusedIcon = nullptr;
        Expect(unusedSlot->GetIcon(nullptr, &unusedIcon) == E_NOTIMPL && unusedIcon == nullptr,
            "Unused multi-direct slots must not resolve an icon");
        unusedSlot->Release();

        GUID unknownClassId = CLSID_RightAgentExplorerCommand;
        unknownClassId.Data1 += static_cast<unsigned long>(RightAgentExplorerCommandSlotCount);
        IClassFactory* factory = nullptr;
        Expect(getClassObject(unknownClassId, IID_PPV_ARGS(&factory)) == CLASS_E_CLASSNOTAVAILABLE,
            "An unregistered shell class should not expose a class factory");
        selection->Release();

        SetEnvironmentVariableW(L"RIGHTAGENT_SETTINGS_PATH", nullptr);
        FreeLibrary(shell);
        std::error_code error;
        std::filesystem::remove_all(root, error);
    }
}

int wmain()
{
    try
    {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        TestQuoting();
        TestSettingsParsing();
        TestUnpackagedSettingsPath();
        TestUtf8BomSettings();
        TestSimpleTokenDetection();
        TestWindowsTerminalDefaultProfile();
        TestShellComSurface();
        std::wcout << L"RightAgent native tests passed.\n";
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << "RightAgent native tests failed: " << exception.what() << '\n';
        return 1;
    }
}
