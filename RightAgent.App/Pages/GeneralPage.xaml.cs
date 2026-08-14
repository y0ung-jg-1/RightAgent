using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RightAgent.App.ViewModels;

namespace RightAgent.App.Pages;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();
        Loaded += (_, _) => SettingsLayout.PreventPrematureWrap(this);
    }

    public MainViewModel ViewModel => App.Main?.ViewModel
        ?? throw new InvalidOperationException("The settings window is not available.");
}
