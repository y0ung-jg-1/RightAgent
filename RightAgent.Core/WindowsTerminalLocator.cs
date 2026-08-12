namespace RightAgent.Core;

public static class WindowsTerminalLocator
{
    private const string ExecutableName = "wt.exe";

    public static bool IsAvailable() => IsAvailable(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("LOCALAPPDATA"),
        File.Exists);

    public static bool IsAvailable(
        string? path,
        string? localAppData,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (path ?? string.Empty).Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            if (directory.Length > 0)
            {
                AddCandidate(candidates, directory);
            }
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            AddCandidate(candidates, Path.Combine(localAppData, "Microsoft", "WindowsApps"));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (fileExists(candidate))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Ignore malformed, inaccessible, or stale PATH entries and keep checking.
            }
        }
        return false;
    }

    private static void AddCandidate(ISet<string> candidates, string directory)
    {
        try
        {
            candidates.Add(Path.Combine(directory, ExecutableName));
        }
        catch (ArgumentException)
        {
            // Ignore malformed PATH entries.
        }
    }
}
