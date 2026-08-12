using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RightAgent.App.ViewModels;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace RightAgent.App;

public sealed partial class MainWindow : Window
{
    private bool synchronizing;

    public MainWindow()
    {
        ViewModel = new MainViewModel(App.LocalStateDirectory);
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1200, 880));
        TryApplyMicaBackdrop();
        ExtendIntoTitleBar();
        Title = ViewModel.WindowTitle;
        Activated += (_, _) => Title = ViewModel.WindowTitle;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
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
            await ViewModel.LoadAsync();
            Bindings.Update();
            SynchronizeSelectors();
            UpdateStatusInfoBar();
            Title = ViewModel.WindowTitle;
        }
        catch (Exception exception)
        {
            ShowStatus(InfoBarSeverity.Error, $"{ViewModel.SaveFailedLabel}: {exception.Message}");
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasValidationErrors)
        {
            UpdateStatusInfoBar();
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await ViewModel.SaveAsync();
            SynchronizeSelectors();
            ShowStatus(InfoBarSeverity.Success, ViewModel.SavedMessage);
        }
        catch (Exception exception)
        {
            ShowStatus(InfoBarSeverity.Error, $"{ViewModel.SaveFailedLabel}: {exception.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void AddAgent_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddAgent();
    }

    private void AgentExpander_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The Expander template reserves a 20px margin before its chevron border
        // ("ExpandCollapseChevronBorder", Margin 20,0,8,0), which leaves the toggle
        // looking stranded. Tighten that margin directly on the template part, then
        // stretch the header grid to the exact right edge of the content column —
        // anything wider gets layout-clipped by the chevron slot.
        if (sender is not Expander { Header: FrameworkElement header } expander)
        {
            return;
        }
        var border = FindDescendantByName(expander, "ExpandCollapseChevronBorder");
        if (border is null)
        {
            return;
        }
        if (border.Margin.Left != 2 || border.Margin.Right != 4)
        {
            border.Margin = new Thickness(2, 0, 4, 0);
        }
        // The margin change does not alter the Expander's own size, so SizeChanged will
        // not fire again — force a synchronous layout pass and measure the new geometry.
        expander.UpdateLayout();
        var headerLeft = header.TransformToVisual(expander).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        var columnRight = border.TransformToVisual(expander).TransformPoint(new Windows.Foundation.Point(0, 0)).X - border.Margin.Left;
        var width = columnRight - headerLeft;
        if (width > 0 && System.Math.Abs(header.Width - width) > 0.5)
        {
            header.Width = width;
        }
    }

    private static FrameworkElement? FindDescendantByName(Microsoft.UI.Xaml.DependencyObject root, string name)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; ++index)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }
            var found = FindDescendantByName(child, name);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
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
            XamlRoot = Content.XamlRoot,
            Title = ViewModel.DeleteTitle,
            Content = ViewModel.DeleteBody,
            PrimaryButtonText = ViewModel.DeleteLabel,
            CloseButtonText = ViewModel.CancelLabel,
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RemoveAgent(agent);
            SynchronizeSelectors();
        }
    }

    private async void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || ViewModel.FindAgent(id) is not { } agent)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".ico");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

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
            await NormalizeIconAsync(file, destination);
            ViewModel.SetAgentIcon(agent, Path.Combine("Icons", fileName));
        }
        catch (Exception exception)
        {
            ShowStatus(InfoBarSeverity.Error, exception.Message);
        }
    }

    private void MenuMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizing || sender is not ComboBox { SelectedValue: string value })
        {
            return;
        }
        ViewModel.MenuMode = value;
    }

    private void DirectAgent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizing || sender is not ComboBox { SelectedValue: string value })
        {
            return;
        }
        ViewModel.DirectAgentId = value;
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizing || sender is not ComboBox { SelectedValue: string value })
        {
            return;
        }
        ViewModel.Language = value;
        Title = ViewModel.WindowTitle;
        Bindings.Update();
        SynchronizeSelectors();
        UpdateStatusInfoBar();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ValidationSummary) or nameof(MainViewModel.HasValidationErrors))
        {
            UpdateStatusInfoBar();
        }
    }

    private void SynchronizeSelectors()
    {
        synchronizing = true;
        try
        {
            LanguageComboBox.SelectedValue = ViewModel.Language;
            MenuModeComboBox.SelectedValue = ViewModel.MenuMode;
            DirectAgentComboBox.SelectedValue = ViewModel.DirectAgentId;
        }
        finally
        {
            synchronizing = false;
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
            // The live validation banner is showing and the problems are gone.
            StatusInfoBar.IsOpen = false;
            StatusInfoBar.Title = string.Empty;
            StatusInfoBar.Message = string.Empty;
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
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
        // Draw the title area ourselves (icon + name) over the Mica backdrop and keep
        // only the system caption buttons, with theme-neutral hover feedback.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var titleBar = AppWindow.TitleBar;
        titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x26, 0x80, 0x80, 0x80);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80);
    }

    private static async Task NormalizeIconAsync(StorageFile source, string destination)
    {
        if (Path.GetExtension(source.Name).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            await using var input = await source.OpenStreamForReadAsync();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output);
            await output.FlushAsync();
            return;
        }

        using var sourceStream = await source.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(sourceStream);
        const uint iconSize = 256;
        var ratio = Math.Min((double)iconSize / decoder.PixelWidth, (double)iconSize / decoder.PixelHeight);
        var scaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * ratio));
        var scaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * ratio));
        var transform = new BitmapTransform
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        var scaledPixels = pixelData.DetachPixelData();
        var canvas = new byte[checked((int)(iconSize * iconSize * 4))];
        var offsetX = (iconSize - scaledWidth) / 2;
        var offsetY = (iconSize - scaledHeight) / 2;
        for (uint row = 0; row < scaledHeight; ++row)
        {
            System.Buffer.BlockCopy(
                scaledPixels,
                checked((int)(row * scaledWidth * 4)),
                canvas,
                checked((int)(((row + offsetY) * iconSize + offsetX) * 4)),
                checked((int)(scaledWidth * 4)));
        }

        using var pngStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, iconSize, iconSize, 96, 96, canvas);
        await encoder.FlushAsync();
        pngStream.Seek(0);
        using var reader = new DataReader(pngStream.GetInputStreamAt(0));
        await reader.LoadAsync(checked((uint)pngStream.Size));
        var png = new byte[checked((int)pngStream.Size)];
        reader.ReadBytes(png);

        await using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fileStream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)1); // one image
        writer.Write((byte)0);   // 256 pixels
        writer.Write((byte)0);   // 256 pixels
        writer.Write((byte)0);   // palette
        writer.Write((byte)0);   // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)32);
        writer.Write((uint)png.Length);
        writer.Write((uint)22);
        writer.Write(png);
        writer.Flush();
        await fileStream.FlushAsync();
    }
}
