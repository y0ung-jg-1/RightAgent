#include "NativeSettings.h"
#include "ProcessHelpers.h"
#include "ShellExports.h"

#include <windows.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <winrt/base.h>

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
        VerifyRoundTrip({L"RightAgent.Launcher.exe", L"--cwd", L"C:\\普通目录\\space & (test)\\", L"--agent", L"kimi-web"});
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
        Expect(rightagent::CreateDefaultSettings().terminalShell == rightagent::TerminalShell::Automatic, "Default terminal shell should be automatic");

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
        Expect(rightagent::LoadSettingsFromPath(path).terminalShell == rightagent::TerminalShell::Automatic,
            "Settings without terminalShell should use automatic mode");

        std::error_code error;
        std::filesystem::remove_all(root, error);
    }

    void TestSimpleTokenDetection()
    {
        Expect(rightagent::FirstSimpleCommandToken(L"  kimi web") == L"kimi", "Simple command token not detected");
        Expect(rightagent::FirstSimpleCommandToken(L"& 'C:\\Tools\\agent.exe'").empty(), "Complex command should not be prevalidated");
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
    {"id":"kimi-web","name":"Kimi Web","enabled":true,"sort":1,"iconPath":"builtin:kimi","action":{"type":"url","value":"https://www.kimi.com"}}
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

        IClassFactory* factory = nullptr;
        Expect(SUCCEEDED(getClassObject(CLSID_RightAgentExplorerCommand, IID_PPV_ARGS(&factory))), "Could not create shell class factory");
        IExplorerCommand* command = nullptr;
        Expect(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&command))), "Could not create root explorer command");
        factory->Release();

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
        TestSimpleTokenDetection();
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
