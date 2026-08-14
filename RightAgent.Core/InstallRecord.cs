using System.Text.Json;
using System.Text.Json.Serialization;

namespace RightAgent.Core;

public sealed class InstallRecord
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = SettingsContract.ReleasePackageName;

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = SettingsContract.ReleasePublisher;

    [JsonPropertyName("appPath")]
    public string AppPath { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonIgnore]
    public bool AppExists => !string.IsNullOrWhiteSpace(AppPath) && File.Exists(AppPath);

    public static InstallRecord? TryLoad(string? localStateDirectory = null)
    {
        var path = Path.Combine(
            localStateDirectory ?? AppPaths.GetLocalStateDirectory(),
            AppPaths.InstallRecordFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstallRecord>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string? localStateDirectory = null)
    {
        var directory = localStateDirectory ?? AppPaths.GetLocalStateDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AppPaths.InstallRecordFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
