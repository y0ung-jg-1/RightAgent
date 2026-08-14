using System.Text.Json;

namespace RightAgent.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public SettingsStore(string localStateDirectory)
    {
        if (string.IsNullOrWhiteSpace(localStateDirectory))
        {
            throw new ArgumentException("A LocalState directory is required.", nameof(localStateDirectory));
        }

        LocalStateDirectory = Path.GetFullPath(localStateDirectory);
        SettingsPath = Path.Combine(LocalStateDirectory, "settings.json");
    }

    public string LocalStateDirectory { get; }

    public string SettingsPath { get; }

    public async Task<RightAgentSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalStateDirectory);
        if (!File.Exists(SettingsPath))
        {
            var defaults = SettingsDefaults.Create();
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            await using var stream = new FileStream(
                SettingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous);
            var settings = await JsonSerializer.DeserializeAsync<RightAgentSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return SettingsValidator.Normalize(settings);
        }
        catch (JsonException)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            var backup = Path.Combine(LocalStateDirectory, $"settings.corrupt-{timestamp}.json");
            try
            {
                File.Copy(SettingsPath, backup, overwrite: false);
            }
            catch (IOException)
            {
                // Preserving the original settings is best-effort; never block recovery.
            }

            var defaults = SettingsDefaults.Create();
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }
    }

    public async Task SaveAsync(RightAgentSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalStateDirectory);
        var normalized = SettingsValidator.Normalize(settings);
        var tempPath = SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(SettingsPath))
            {
                await ReplaceWithRetryAsync(tempPath, SettingsPath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                File.Move(tempPath, SettingsPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task ReplaceWithRetryAsync(string tempPath, string settingsPath, CancellationToken cancellationToken)
    {
        const int attempts = 8;
        for (var attempt = 0; ; ++attempt)
        {
            try
            {
                File.Replace(tempPath, settingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
