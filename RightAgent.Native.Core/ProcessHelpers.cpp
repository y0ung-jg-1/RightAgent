#include "ProcessHelpers.h"

#include <windows.h>

#include <cwctype>
#include <system_error>

namespace rightagent
{
    std::wstring QuoteCommandLineArgument(const std::wstring_view argument)
    {
        if (!argument.empty()
            && argument.find_first_of(L" \t\n\v\"") == std::wstring_view::npos)
        {
            return std::wstring(argument);
        }

        std::wstring result(1, L'"');
        std::size_t backslashes = 0;
        for (const wchar_t character : argument)
        {
            if (character == L'\\')
            {
                ++backslashes;
                continue;
            }

            if (character == L'"')
            {
                result.append(backslashes * 2 + 1, L'\\');
                result.push_back(L'"');
            }
            else
            {
                result.append(backslashes, L'\\');
                result.push_back(character);
            }
            backslashes = 0;
        }
        result.append(backslashes * 2, L'\\');
        result.push_back(L'"');
        return result;
    }

    std::wstring BuildCommandLine(const std::vector<std::wstring>& arguments)
    {
        std::wstring result;
        for (const auto& argument : arguments)
        {
            if (!result.empty())
            {
                result.push_back(L' ');
            }
            result.append(QuoteCommandLineArgument(argument));
        }
        return result;
    }

    std::filesystem::path GetModuleDirectory(void* moduleHandle)
    {
        std::wstring path(512, L'\0');
        while (true)
        {
            const DWORD written = GetModuleFileNameW(static_cast<HMODULE>(moduleHandle), path.data(), static_cast<DWORD>(path.size()));
            if (written == 0)
            {
                return {};
            }
            if (written < path.size() - 1)
            {
                path.resize(written);
                return std::filesystem::path(path).parent_path();
            }
            path.resize(path.size() * 2);
        }
    }

    std::wstring FirstSimpleCommandToken(const std::wstring_view command)
    {
        std::size_t start = 0;
        while (start < command.size() && std::iswspace(command[start]))
        {
            ++start;
        }
        if (start == command.size() || command[start] == L'"' || command[start] == L'&' || command[start] == L'.')
        {
            return {};
        }

        std::size_t end = start;
        while (end < command.size() && !std::iswspace(command[end]))
        {
            const wchar_t value = command[end];
            if (!(std::iswalnum(value) || value == L'-' || value == L'_' || value == L'.'))
            {
                return {};
            }
            ++end;
        }
        return std::wstring(command.substr(start, end - start));
    }

    bool LaunchProcess(
        const std::filesystem::path& executable,
        const std::vector<std::wstring>& arguments,
        const std::filesystem::path& workingDirectory,
        const unsigned long creationFlags,
        unsigned long* errorCode)
    {
        std::vector<std::wstring> commandArguments;
        commandArguments.reserve(arguments.size() + 1);
        commandArguments.push_back(executable.wstring());
        commandArguments.insert(commandArguments.end(), arguments.begin(), arguments.end());
        auto commandLine = BuildCommandLine(commandArguments);

        STARTUPINFOW startupInfo{};
        startupInfo.cb = sizeof(startupInfo);
        PROCESS_INFORMATION processInfo{};
        const std::wstring workingDirectoryText = workingDirectory.empty() ? std::wstring{} : workingDirectory.wstring();
        const BOOL created = CreateProcessW(
            executable.c_str(),
            commandLine.data(),
            nullptr,
            nullptr,
            FALSE,
            creationFlags | CREATE_UNICODE_ENVIRONMENT,
            nullptr,
            workingDirectoryText.empty() ? nullptr : workingDirectoryText.c_str(),
            &startupInfo,
            &processInfo);

        if (!created)
        {
            if (errorCode != nullptr)
            {
                *errorCode = GetLastError();
            }
            return false;
        }

        CloseHandle(processInfo.hThread);
        CloseHandle(processInfo.hProcess);
        if (errorCode != nullptr)
        {
            *errorCode = ERROR_SUCCESS;
        }
        return true;
    }
}
