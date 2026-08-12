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
        AppWindow.Resize(new SizeInt32(1040, 860));
        TryApplyMicaBackdrop();
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

    private void MenuModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (synchronizing)
        {
            return;
        }
        ViewModel.MenuMode = DirectModeRadio.IsChecked == true
            ? RightAgent.Core.SettingsContract.DirectMenu
            : RightAgent.Core.SettingsContract.GroupedMenu;
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
            DirectAgentComboBox.SelectedValue = ViewModel.DirectAgentId;
            GroupedModeRadio.IsChecked = !ViewModel.IsDirectMode;
            DirectModeRadio.IsChecked = ViewModel.IsDirectMode;
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
