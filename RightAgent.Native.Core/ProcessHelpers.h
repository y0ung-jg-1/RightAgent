#pragma once

#include <filesystem>
#include <string>
#include <string_view>
#include <vector>

namespace rightagent
{
    [[nodiscard]] std::wstring QuoteCommandLineArgument(std::wstring_view argument);
    [[nodiscard]] std::wstring BuildCommandLine(const std::vector<std::wstring>& arguments);
    [[nodiscard]] std::wstring EncodePowerShellCommand(std::wstring_view command);
    [[nodiscard]] std::filesystem::path GetModuleDirectory(void* moduleHandle);
    [[nodiscard]] std::wstring FirstSimpleCommandToken(std::wstring_view command);
    [[nodiscard]] bool LaunchProcess(
        const std::filesystem::path& executable,
        const std::vector<std::wstring>& arguments,
        const std::filesystem::path& workingDirectory,
        unsigned long creationFlags,
        unsigned long* errorCode = nullptr);
}
