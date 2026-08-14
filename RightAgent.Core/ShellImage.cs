using System.Runtime.InteropServices;

namespace RightAgent.Core;

public static class ShellImage
{
    private const int SiigbfResizeToFit = 0x00;
    private const int SiigbfBiggerSizeOk = 0x01;
    private const int DibRgbColors = 0;

    public static bool TryGetBgra32(string path, int size, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(path) || size <= 0 || !File.Exists(path))
        {
            return false;
        }

        var com = CoInitializeEx(IntPtr.Zero, 0);
        var initializedApartment = com is 0 or 1;
        try
        {
        var iid = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");
        var result = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        if (result != 0 || factory is null)
        {
            return false;
        }

        var bitmap = IntPtr.Zero;
        try
        {
            factory.GetImage(new SIZE { cx = size, cy = size }, SiigbfResizeToFit | SiigbfBiggerSizeOk, out bitmap);
            if (bitmap == IntPtr.Zero)
            {
                return false;
            }

            return TryCopyBitmap(bitmap, out pixels, out width, out height);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            Marshal.ReleaseComObject(factory);
        }
        }
        finally
        {
            if (initializedApartment)
            {
                CoUninitialize();
            }
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint apartment);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private static bool TryCopyBitmap(IntPtr bitmap, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            if (GetDIBits(screen, bitmap, 0, 0, null, ref info, DibRgbColors) == 0
                || info.bmiHeader.biWidth <= 0
                || info.bmiHeader.biHeight == 0)
            {
                return false;
            }

            width = info.bmiHeader.biWidth;
            height = Math.Abs(info.bmiHeader.biHeight);
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = 0;
            var buffer = new byte[checked(width * height * 4)];
            if (GetDIBits(screen, bitmap, 0, (uint)height, buffer, ref info, DibRgbColors) == 0)
            {
                return false;
            }

            pixels = buffer;
            return true;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid riid,
        out IShellItemImageFactory item);

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr deviceContext,
        IntPtr bitmap,
        uint startScan,
        uint scanLines,
        byte[]? bits,
        ref BITMAPINFO info,
        uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);
}
