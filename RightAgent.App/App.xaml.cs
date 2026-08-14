using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RightAgent.Core;

namespace RightAgent.App;

public partial class App : Application
{
    public static MainWindow? Main { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            args.Handled = true;
            Main?.ShowStatus(InfoBarSeverity.Error, args.Message);
        };
    }

    public static string LocalStateDirectory => AppPaths.GetLocalStateDirectory();

    internal static void SetMain(MainWindow window) => Main = window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Main = new MainWindow();
        Main.Activate();
    }
}
