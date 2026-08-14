using Microsoft.UI.Xaml.Controls;
using RightAgent.App.ViewModels;

namespace RightAgent.App.Pages;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel => App.Main?.ViewModel
        ?? throw new InvalidOperationException("The settings window is not available.");
}
