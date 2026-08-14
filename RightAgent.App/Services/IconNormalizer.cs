using System.Runtime.InteropServices;
using RightAgent.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace RightAgent.App.Services;

internal static class IconNormalizer
{
    private const uint IconSize = 256;

    public static async Task NormalizeAsync(StorageFile source, string destination)
    {
        if (Path.GetExtension(source.Name).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            await using var input = await source.OpenStreamForReadAsync();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output);
            await output.FlushAsync();
            return;
        }

        var canvas = await TryDecodeRasterAsync(source);
        if (canvas.Length == 0
            || Path.GetExtension(source.Name).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (!ShellImage.TryGetBgra32(source.Path, (int)IconSize, out var pixels, out var width, out var height))
            {
                throw new InvalidOperationException("The selected image could not be converted to an icon.");
            }

            canvas = PadToSquare(pixels, width, height, (int)IconSize);
        }

        await WritePngIcoAsync(canvas, destination);
    }

    private static async Task<byte[]> TryDecodeRasterAsync(StorageFile source)
    {
        try
        {
            using var sourceStream = await source.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(sourceStream);
            var ratio = Math.Min((double)IconSize / decoder.PixelWidth, (double)IconSize / decoder.PixelHeight);
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
            return PadToSquare(pixelData.DetachPixelData(), (int)scaledWidth, (int)scaledHeight, (int)IconSize);
        }
        catch (Exception exception) when (exception is ArgumentException or NotImplementedException or COMException)
        {
            return [];
        }
    }

    private static byte[] PadToSquare(byte[] source, int width, int height, int size)
    {
        var canvas = new byte[checked(size * size * 4)];
        var offsetX = (size - width) / 2;
        var offsetY = (size - height) / 2;
        for (var row = 0; row < height; ++row)
        {
            System.Buffer.BlockCopy(
                source,
                checked(row * width * 4),
                canvas,
                checked(((row + offsetY) * size + offsetX) * 4),
                checked(width * 4));
        }

        return canvas;
    }

    private static async Task WritePngIcoAsync(byte[] canvas, string destination)
    {
        using var pngStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, IconSize, IconSize, 96, 96, canvas);
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
