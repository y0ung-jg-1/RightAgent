using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RightAgent.App.Services;
using RightAgent.App.ViewModels;
using Windows.Storage.Pickers;

namespace RightAgent.App.Pages;

public sealed partial class MenuPage : Page
{
    private const double PreviewColumnWidth = 280;
    private const double PreviewSpacing = 16;
    private const double MinSettingsColumnWidth = 360;
    private bool? narrowLayoutActive;

    public MenuPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyResponsiveLayout(PageRoot.ActualWidth);
    }

    public MainViewModel ViewModel => App.Main?.ViewModel
        ?? throw new InvalidOperationException("The settings window is not available.");

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double pageWidth)
    {
        if (pageWidth <= 0)
        {
            return;
        }

        var innerWidth = pageWidth - PageRoot.Padding.Left - PageRoot.Padding.Right;
        var useNarrowLayout = innerWidth < MinSettingsColumnWidth + PreviewSpacing + PreviewColumnWidth;
        if (narrowLayoutActive == useNarrowLayout)
        {
            return;
        }

        narrowLayoutActive = useNarrowLayout;
        PreviewCard.Visibility = useNarrowLayout ? Visibility.Collapsed : Visibility.Visible;
        PreviewColumn.Width = new GridLength(useNarrowLayout ? 0 : PreviewColumnWidth);
        PageRoot.ColumnSpacing = useNarrowLayout ? 0 : PreviewSpacing;
    }

    private void AddAgent_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddAgent();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && ViewModel.FindAgent(id) is { } agent)
        {
            ViewModel.MoveAgent(agent, -1);
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && ViewModel.FindAgent(id) is { } agent)
        {
            ViewModel.MoveAgent(agent, 1);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || ViewModel.FindAgent(id) is not { } agent)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ViewModel.DeleteTitle,
            Content = ViewModel.DeleteBody,
            PrimaryButtonText = ViewModel.DeleteLabel,
            CloseButtonText = ViewModel.CancelLabel,
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RemoveAgent(agent);
        }
    }

    private async void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || ViewModel.FindAgent(id) is not { } agent || App.Main is null)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".ico");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Main));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var iconDirectory = Path.Combine(App.LocalStateDirectory, "Icons");
            Directory.CreateDirectory(iconDirectory);
            var fileName = agent.Id + ".ico";
            var destination = Path.Combine(iconDirectory, fileName);
            await IconNormalizer.NormalizeAsync(file, destination);
            ViewModel.SetAgentIcon(agent, Path.Combine("Icons", fileName));
        }
        catch (Exception exception)
        {
            App.Main.ShowStatus(InfoBarSeverity.Error, exception.Message);
        }
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && ViewModel.FindAgent(id) is { } agent)
        {
            ViewModel.ResetAgentIcon(agent);
        }
    }
}
