using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RightAgent.App.Pages;
using RightAgent.App.ViewModels;
using RightAgent.Core;
using Windows.Graphics;

namespace RightAgent.App;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 640;
    private const int MinimumWindowHeight = 540;
    private const int DefaultWindowWidth = 1240;
    private const int DefaultWindowHeight = 880;
    private static readonly Uri WindowsTerminalStoreUri = new("ms-windows-store://pdp/?ProductId=9N0DX20HK701");
    private static readonly Uri WindowsTerminalStoreWebUri = new("https://apps.microsoft.com/detail/9n0dx20hk701");
    private bool terminalRequirementChecked;
    private bool isClosingAfterFlush;

    public MainWindow()
    {
        App.SetMain(this);
        ViewModel = new MainViewModel(App.LocalStateDirectory);
        InitializeComponent();
        ApplyWindowMetrics();
        TryApplyMicaBackdrop();
        ExtendIntoTitleBar();
        Title = ViewModel.WindowTitle;
        Activated += (_, _) => Title = ViewModel.WindowTitle;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.PersistFailed += (_, message) =>
            ShowStatus(InfoBarSeverity.Error, $"{ViewModel.SaveFailedLabel}: {message}");
        AppWindow.Closing += AppWindow_Closing;
        if (Content is FrameworkElement root)
        {
            root.Loaded += MainWindow_Loaded;
        }
    }

    public MainViewModel ViewModel { get; }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyMinimumSize();
            await ViewModel.LoadAsync();
            Bindings.Update();
            UpdateStatusInfoBar();
            Title = ViewModel.WindowTitle;
            RootNav.IsPaneOpen = true;
            if (RootNav.SelectedItem is null)
            {
                RootNav.SelectedItem = MenuNavItem;
            }
            DispatcherQueue.TryEnqueue(() => RootNav.IsPaneOpen = true);
            await PromptForWindowsTerminalAsync();
        }
        catch (Exception exception)
        {
            ShowStatus(InfoBarSeverity.Error, $"{ViewModel.SaveFailedLabel}: {exception.Message}");
        }
    }

    private void ApplyWindowMetrics()
    {
        ApplyMinimumSize();
        AppWindow.Resize(new SizeInt32(DefaultWindowWidth, DefaultWindowHeight));
    }

    private void ApplyMinimumSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        // PreferredMinimum* is not DPI-aware; pass physical pixels so 640x540 DIP still holds.
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1d;
        presenter.PreferredMinimumWidth = (int)Math.Ceiling(MinimumWindowWidth * scale);
        presenter.PreferredMinimumHeight = (int)Math.Ceiling(MinimumWindowHeight * scale);
    }

    private async Task PromptForWindowsTerminalAsync()
    {
        if (terminalRequirementChecked)
        {
            return;
        }
        terminalRequirementChecked = true;

        if (WindowsTerminalLocator.IsAvailable())
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = ViewModel.TerminalRequiredTitle,
            Content = new TextBlock
            {
                MaxWidth = 420,
                Text = ViewModel.TerminalRequiredBody,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = ViewModel.InstallFromStoreLabel,
            CloseButtonText = ViewModel.InstallLaterLabel,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var launched = await Windows.System.Launcher.LaunchUriAsync(WindowsTerminalStoreUri);
            if (!launched)
            {
                launched = await Windows.System.Launcher.LaunchUriAsync(WindowsTerminalStoreWebUri);
            }
            if (!launched)
            {
                ShowStatus(InfoBarSeverity.Error, ViewModel.TerminalStoreOpenFailedMessage);
            }
        }
        catch
        {
            ShowStatus(InfoBarSeverity.Error, ViewModel.TerminalStoreOpenFailedMessage);
        }
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (isClosingAfterFlush)
        {
            return;
        }

        args.Cancel = true;
        isClosingAfterFlush = true;
        try
        {
            await ViewModel.FlushAutoSaveAsync();
        }
        catch (Exception)
        {
            // The next launch reloads last valid settings.
        }

        Close();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        RootNav.IsPaneOpen = !RootNav.IsPaneOpen;
    }

    private void RootNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        var pageType = tag switch
        {
            "general" => typeof(GeneralPage),
            _ => typeof(MenuPage)
        };
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.WindowTitle))
        {
            Title = ViewModel.WindowTitle;
        }

        if (e.PropertyName is nameof(MainViewModel.ValidationSummary) or nameof(MainViewModel.HasValidationErrors))
        {
            UpdateStatusInfoBar();
        }
    }

    private void UpdateStatusInfoBar()
    {
        if (ViewModel.HasValidationErrors)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Title = ViewModel.ValidationTitle;
            StatusInfoBar.Message = ViewModel.ValidationSummary;
            StatusInfoBar.IsClosable = false;
            StatusInfoBar.IsOpen = true;
        }
        else if (!StatusInfoBar.IsClosable)
        {
            StatusInfoBar.IsOpen = false;
            StatusInfoBar.Title = string.Empty;
            StatusInfoBar.Message = string.Empty;
        }
    }

    public void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = string.Empty;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsClosable = true;
        StatusInfoBar.IsOpen = true;
    }

    private void TryApplyMicaBackdrop()
    {
        if (!MicaController.IsSupported())
        {
            return;
        }

        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void ExtendIntoTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(AppTitleBar);
        titleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x26, 0x80, 0x80, 0x80);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80);
    }
}
