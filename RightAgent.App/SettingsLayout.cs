using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RightAgent.App;

internal static class SettingsLayout
{
    // SettingsCard wrap resources in generic.xaml win over App.xaml.
    // Patch the live ControlSizeTrigger after the template is applied.
    private const double WrapThreshold = 220;
    private const double WrapNoIconThreshold = 120;

    public static void PreventPrematureWrap(DependencyObject root)
    {
        if (root is Grid grid)
        {
            foreach (var group in VisualStateManager.GetVisualStateGroups(grid))
            {
                foreach (var state in group.States)
                {
                    foreach (var trigger in state.StateTriggers)
                    {
                        if (trigger.GetType().Name != "ControlSizeTrigger")
                        {
                            continue;
                        }

                        var type = trigger.GetType();
                        if (state.Name == "RightWrapped")
                        {
                            type.GetProperty("MinWidth")?.SetValue(trigger, WrapNoIconThreshold);
                            type.GetProperty("MaxWidth")?.SetValue(trigger, WrapThreshold);
                        }
                        else if (state.Name == "RightWrappedNoIcon")
                        {
                            type.GetProperty("MaxWidth")?.SetValue(trigger, WrapNoIconThreshold);
                        }
                    }
                }
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            PreventPrematureWrap(VisualTreeHelper.GetChild(root, i));
        }
    }
}
