namespace RightAgent.Core;

public static class AppPaths
{
    public const string ProductDirectoryName = "RightAgent";
    public const string SettingsFileName = "settings.json";
    public const string InstallRecordFileName = "install.json";
    public const string SettingsPathEnvironmentVariable = "RIGHTAGENT_SETTINGS_PATH";

    public static string GetLocalStateDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(SettingsPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var fullPath = Path.GetFullPath(overridePath);
            return Path.GetFileName(fullPath).Equals(SettingsFileName, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(fullPath)!
                : fullPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductDirectoryName);
    }

    public static string GetSettingsPath() => Path.Combine(GetLocalStateDirectory(), SettingsFileName);

    public static string GetInstallRecordPath() => Path.Combine(GetLocalStateDirectory(), InstallRecordFileName);

    public static string GetCommandPackageCacheDirectory() =>
        Path.Combine(GetLocalStateDirectory(), CommandSlotPlanner.CommandPackageCacheDirectoryName);

    public static string GetDefaultInstallDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            ProductDirectoryName);
}
