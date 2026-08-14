namespace RightAgent.Core;

public static class CommandSlotPlanner
{
    public const string CommandPackageCacheDirectoryName = "CommandPackages";

    public static int RequiredSlotCount(RightAgentSettings settings)
    {
        var normalized = SettingsValidator.Normalize(settings);
        if (!normalized.MenuEnabled)
        {
            return 0;
        }

        var enabledCount = normalized.Agents.Count(agent => agent.Enabled);
        if (enabledCount == 0)
        {
            return 0;
        }

        return normalized.MenuMode == SettingsContract.MultiDirectMenu
            ? Math.Min(SettingsContract.MaxMultiDirectAgents, enabledCount)
            : 1;
    }

    public static string CommandPackageFileName(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SettingsContract.MaxMultiDirectAgents);
        return $"{slot:D2}.msix";
    }

    public static bool CachedPackageExists(string cacheDirectory, int slot)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            return false;
        }

        var info = new FileInfo(Path.Combine(cacheDirectory, CommandPackageFileName(slot)));
        return info.Exists && info.Length > 0;
    }

    public static bool CacheIsComplete(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            return false;
        }

        for (var slot = 0; slot < SettingsContract.MaxMultiDirectAgents; ++slot)
        {
            if (!CachedPackageExists(cacheDirectory, slot))
            {
                return false;
            }
        }

        return true;
    }

    public static OccupancyPlan Plan(
        int requiredSlots,
        IReadOnlyDictionary<int, string> installed,
        Func<int, bool> cacheExists)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requiredSlots);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredSlots, SettingsContract.MaxMultiDirectAgents);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(cacheExists);

        var toAdd = new List<int>();
        var toRemove = new List<string>();
        var missingCachedAdds = false;
        for (var slot = 0; slot < SettingsContract.MaxMultiDirectAgents; ++slot)
        {
            var isInstalled = installed.TryGetValue(slot, out var fullName);
            if (slot < requiredSlots && !isInstalled)
            {
                if (cacheExists(slot))
                {
                    toAdd.Add(slot);
                }
                else
                {
                    missingCachedAdds = true;
                }
            }
            else if (slot >= requiredSlots && isInstalled && !string.IsNullOrWhiteSpace(fullName))
            {
                toRemove.Add(fullName);
            }
        }

        return new OccupancyPlan(toAdd, toRemove, missingCachedAdds);
    }
}

public sealed record OccupancyPlan(
    IReadOnlyList<int> SlotsToAdd,
    IReadOnlyList<string> PackagesToRemove,
    bool CacheMissingRequiredSlots);
