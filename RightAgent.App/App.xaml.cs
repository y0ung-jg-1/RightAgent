using Microsoft.UI.Xaml;
using RightAgent.Core;

namespace RightAgent.App;

public partial class App : Application
{
    public static MainWindow? Main { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    public static string LocalStateDirectory => AppPaths.GetLocalStateDirectory();

    internal static void SetMain(MainWindow window) => Main = window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Main = new MainWindow();
        Main.Activate();
    }
}
