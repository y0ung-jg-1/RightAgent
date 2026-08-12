using Microsoft.UI.Xaml;

namespace RightAgent.App;

/// <summary>
/// Static visibility helpers for x:Bind function bindings. A Window-rooted page cannot use
/// IValueConverter instances through StaticResource, so bindings call these functions instead.
/// </summary>
public static class VisibilityConverters
{
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}

public static class EnabledAppearance
{
    public static double OpacityFor(bool enabled) => enabled ? 1.0 : 0.55;
}
