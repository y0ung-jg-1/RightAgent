using Microsoft.UI.Xaml;
using Windows.Storage;

namespace RightAgent.App;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    public static string LocalStateDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("RIGHTAGENT_SETTINGS_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                var fullPath = Path.GetFullPath(overridePath);
                return Path.GetFileName(fullPath).Equals("settings.json", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(fullPath)!
                    : fullPath;
            }

            try
            {
                return ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RightAgent");
            }
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
