namespace RightAgent.Core;

public static class CommandLocator
{
    private static readonly string[] ExecutableExtensions = [".exe", ".cmd", ".bat", ".com"];

    public static bool Exists(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.Trim().Trim('"');
        if (Path.IsPathFullyQualified(trimmed))
        {
            return File.Exists(trimmed) || ExecutableExtensions.Any(ext => File.Exists(trimmed + ext));
        }

        foreach (var directory in CandidateDirectories())
        {
            foreach (var candidate in CandidateNames(trimmed))
            {
                try
                {
                    if (File.Exists(Path.Combine(directory, candidate)))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // Ignore malformed PATH entries and keep checking.
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var item in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = item.Trim('"');
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                yield return normalized;
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var extra in new[]
                 {
                     Path.Combine(localAppData, "Microsoft", "WindowsApps"),
                     Path.Combine(home, ".local", "bin"),
                     Path.Combine(home, ".kimi-code", "bin")
                 })
        {
            if (seen.Add(extra))
            {
                yield return extra;
            }
        }
    }

    private static IEnumerable<string> CandidateNames(string command)
    {
        if (Path.HasExtension(command))
        {
            yield return command;
            yield break;
        }

        yield return command;
        foreach (var extension in ExecutableExtensions)
        {
            yield return command + extension;
        }
    }
}
