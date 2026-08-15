using System.Runtime.InteropServices;
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
    private const int DefaultWindowWidth = 1109;
    private const int DefaultWindowHeight = 698;
    private bool defaultSizeApplied;
    private double defaultPlacementScale;
    private static readonly Uri WindowsTerminalStoreUri = new("ms-windows-store://pdp/?ProductId=9N0DX20HK701");
    private static readonly Uri WindowsTerminalStoreWebUri = new("https://apps.microsoft.com/detail/9n0dx20hk701");
    private bool terminalRequirementChecked;
    private bool isClosingAfterFlush;

    public MainWindow()
    {
        App.SetMain(this);
        ViewModel = new MainViewModel(App.LocalStateDirectory);
        InitializeComponent();
        ApplyMinimumSize();
        TryApplyMicaBackdrop();
        ExtendIntoTitleBar();
        Title = ViewModel.WindowTitle;
        Activated += MainWindow_Activated;
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
            if (!defaultSizeApplied)
            {
                DispatcherQueue.TryEnqueue(PlaceDefaultWindow);
            }
            if (RootNav.SelectedItem is null)
            {
                RootNav.SelectedItem = MenuNavItem;
            }
            await ViewModel.LoadAsync();
            Bindings.Update();
            UpdateStatusInfoBar();
            Title = ViewModel.WindowTitle;
            RootNav.IsPaneOpen = true;
            DispatcherQueue.TryEnqueue(() => RootNav.IsPaneOpen = true);
            await PromptForWindowsTerminalAsync();
        }
        catch (Exception exception)
        {
            ShowStatus(InfoBarSeverity.Error, $"{ViewModel.SaveFailedLabel}: {exception.Message}");
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Title = ViewModel.WindowTitle;
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(PlaceDefaultWindow);
    }

    private void ApplyMinimumSize()
    {
        var scale = GetRasterizationScale();
        if (scale <= 0 || AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.PreferredMinimumWidth = (int)Math.Ceiling(MinimumWindowWidth * scale);
        presenter.PreferredMinimumHeight = (int)Math.Ceiling(MinimumWindowHeight * scale);
    }

    private void PlaceDefaultWindow()
    {
        var scale = GetRasterizationScale();
        if (scale <= 0)
        {
            return;
        }

        ApplyMinimumSize();

        if (defaultSizeApplied && RootGrid.XamlRoot is not null
            && Math.Abs(scale - defaultPlacementScale) <= 0.01)
        {
            return;
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var workLeft = workArea.X;
        var workTop = workArea.Y;
        var workWidth = workArea.Width;
        var workHeight = workArea.Height;

        var width = (int)Math.Round(DefaultWindowWidth * scale);
        var height = (int)Math.Round(DefaultWindowHeight * scale);
        width = Math.Clamp(width, (int)Math.Ceiling(MinimumWindowWidth * scale), workWidth);
        height = Math.Clamp(height, (int)Math.Ceiling(MinimumWindowHeight * scale), workHeight);
        var x = workLeft + Math.Max(0, (workWidth - width) / 2);
        var y = workTop + Math.Max(0, (workHeight - height) / 2);
        AppWindow.Move(new PointInt32(x, y));
        AppWindow.Resize(new SizeInt32(width, height));
        defaultSizeApplied = true;
        defaultPlacementScale = scale;
    }

    private double GetRasterizationScale()
    {
        if (RootGrid.XamlRoot?.RasterizationScale is > 0 and var xamlScale)
        {
            return xamlScale;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return 0;
        }

        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96d : 0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Agents", "rightagent.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

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
