using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RightAgent.App.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace RightAgent.App.Pages;

public sealed partial class MenuPage : Page
{
    private const double PreviewBreakpointWidth = 1000;
    private bool? narrowLayoutActive;

    public MenuPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel => App.Main?.ViewModel
        ?? throw new InvalidOperationException("The settings window is not available.");

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var useNarrowLayout = e.NewSize.Width < PreviewBreakpointWidth;
        if (narrowLayoutActive == useNarrowLayout)
        {
            return;
        }

        narrowLayoutActive = useNarrowLayout;
        PreviewCard.Visibility = useNarrowLayout ? Visibility.Collapsed : Visibility.Visible;
        PreviewColumn.Width = new GridLength(useNarrowLayout ? 0 : 320);
        PageRoot.ColumnSpacing = useNarrowLayout ? 0 : 24;
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
            await NormalizeIconAsync(file, destination);
            ViewModel.SetAgentIcon(agent, Path.Combine("Icons", fileName));
        }
        catch (Exception exception)
        {
            App.Main.ShowStatus(InfoBarSeverity.Error, exception.Message);
        }
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
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)png.Length);
        writer.Write((uint)22);
        writer.Write(png);
        writer.Flush();
        await fileStream.FlushAsync();
    }
}
