using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopCarousel;

internal readonly record struct WallpaperStyle(Color Foreground, Color CaptionShadow, Color CaptionBackground, Color IconShadow)
{
    public static WallpaperStyle Default { get; } = ForRegion(Color.FromRgb(56, 68, 82), 0.01, 0.55);

    public static WallpaperStyle ForRegion(Color average, double darkest, double lightest)
    {
        var dark = Color.FromRgb(26, 31, 39);
        var light = Color.FromRgb(247, 249, 252);
        double darkContrast = (darkest + 0.05) / (Luminance(dark) + 0.05);
        double lightContrast = (Luminance(light) + 0.05) / (lightest + 0.05);
        bool needsBacking = Math.Max(darkContrast, lightContrast) < 4.5;
        bool useDarkText = !needsBacking && darkContrast >= lightContrast;

        bool brightBackground = Luminance(average) >= 0.28;
        var shade = Blend(brightBackground ? dark : Color.FromRgb(165, 188, 211), average, 0.28);
        return new WallpaperStyle(useDarkText ? dark : light, useDarkText ? light : dark,
            needsBacking ? WithAlpha(Blend(dark, average, 0.17), 237) : Colors.Transparent,
            WithAlpha(shade, brightBackground ? (byte)78 : (byte)54));
    }

    public static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color Blend(Color first, Color second, double amount) => Color.FromRgb(
        (byte)(first.R + (second.R - first.R) * amount),
        (byte)(first.G + (second.G - first.G) * amount),
        (byte)(first.B + (second.B - first.B) * amount));

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}

internal sealed class WallpaperAppearance
{
    private SampleRequest? _lastRequest;
    private WallpaperStyle _style;

    public WallpaperStyle Read(Rect region)
    {
        try
        {
            var scene = ReadScene(region);
            var request = new SampleRequest(scene, region,
                scene.Path is { } path ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue);
            if (_lastRequest != request)
            {
                _style = Sample(scene, region);
                _lastRequest = request;
            }
            return _style;
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or
                                      NotSupportedException or ArgumentException or FormatException)
        {
            return WallpaperStyle.Default;
        }
    }

    internal static WallpaperStyle Sample(WallpaperScene scene, Rect region)
    {
        if (scene.Path is null || scene.Screen.IsEmpty)
        {
            double lightness = WallpaperStyle.Luminance(scene.DesktopColor);
            return WallpaperStyle.ForRegion(scene.DesktopColor, lightness, lightness);
        }

        using var stream = File.Open(scene.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand).Frames[0];
        int imageWidth = frame.PixelWidth;
        int imageHeight = frame.PixelHeight;
        stream.Position = 0;
        var thumbnail = new BitmapImage();
        thumbnail.BeginInit();
        thumbnail.StreamSource = stream;
        thumbnail.CacheOption = BitmapCacheOption.OnLoad;
        if (imageWidth >= imageHeight)
        {
            thumbnail.DecodePixelWidth = Math.Min(160, imageWidth);
        }
        else
        {
            thumbnail.DecodePixelHeight = Math.Min(160, imageHeight);
        }
        thumbnail.EndInit();

        var pixels = new FormatConvertedBitmap(thumbnail, PixelFormats.Bgra32, null, 0);
        int stride = pixels.PixelWidth * 4;
        var data = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(data, stride, 0);
        var display = ImageBounds(scene, imageWidth, imageHeight);

        const int columns = 16;
        const int rows = 6;
        var luminance = new double[columns * rows];
        int red = 0, green = 0, blue = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double x = region.Left + region.Width * (column + 0.5) / columns;
                double y = region.Top + region.Height * (row + 0.5) / rows;
                double u = (x - display.Left) / display.Width;
                double v = (y - display.Top) / display.Height;
                if (scene.Position == WallpaperPosition.Tile)
                {
                    u -= Math.Floor(u);
                    v -= Math.Floor(v);
                }

                var color = scene.DesktopColor;
                if (u >= 0 && u < 1 && v >= 0 && v < 1)
                {
                    int index = (int)(v * pixels.PixelHeight) * stride + (int)(u * pixels.PixelWidth) * 4;
                    color = Color.FromRgb(data[index + 2], data[index + 1], data[index]);
                }
                red += color.R;
                green += color.G;
                blue += color.B;
                luminance[row * columns + column] = WallpaperStyle.Luminance(color);
            }
        }

        Array.Sort(luminance);
        int count = luminance.Length;
        var average = Color.FromRgb((byte)(red / count), (byte)(green / count), (byte)(blue / count));
        return WallpaperStyle.ForRegion(average, luminance[count / 10], luminance[count - 1 - count / 10]);
    }

    private static Rect ImageBounds(WallpaperScene scene, int width, int height)
    {
        var screen = scene.Screen;
        if (scene.Position == WallpaperPosition.Stretch)
        {
            return screen;
        }
        if (scene.Position == WallpaperPosition.Tile)
        {
            return new Rect(screen.Left, screen.Top, width, height);
        }

        double scaleX = screen.Width / width;
        double scaleY = screen.Height / height;
        double scale = scene.Position switch
        {
            WallpaperPosition.Center => 1,
            WallpaperPosition.Fit => Math.Min(scaleX, scaleY),
            _ => Math.Max(scaleX, scaleY),
        };
        return new Rect(screen.Left + (screen.Width - width * scale) / 2,
            screen.Top + (screen.Height - height * scale) / 2, width * scale, height * scale);
    }

    private static WallpaperScene ReadScene(Rect region)
    {
        var native = (IDesktopWallpaper)new DesktopWallpaper();
        try
        {
            native.GetPosition(out var position);
            native.GetBackgroundColor(out uint colorRef);
            native.GetMonitorDevicePathCount(out uint count);
            Rect screen = Rect.Empty;
            Rect screens = Rect.Empty;
            string? monitor = null;
            var center = new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
            for (uint index = 0; index < count; index++)
            {
                native.GetMonitorDevicePathAt(index, out string id);
                if (native.GetMonitorRECT(id, out var bounds) != 0 || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
                {
                    continue;
                }
                var current = new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                screens.Union(current);
                if (current.Contains(center))
                {
                    monitor = id;
                    screen = current;
                }
            }

            native.GetWallpaper(monitor, out string? path);
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                path = null;
            }
            var color = Color.FromRgb((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));
            return new WallpaperScene(path, position is WallpaperPosition.Span or WallpaperPosition.Tile ? screens : screen, position, color);
        }
        finally
        {
            Marshal.ReleaseComObject(native);
        }
    }

    private readonly record struct SampleRequest(WallpaperScene Scene, Rect Region, DateTime Modified);

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaper;

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, [MarshalAs(UnmanagedType.LPWStr)] out string? path);
        void GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitor);
        void GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor, out MonitorRect bounds);
        void SetBackgroundColor(uint colorRef);
        void GetBackgroundColor(out uint colorRef);
        void SetPosition(WallpaperPosition position);
        void GetPosition(out WallpaperPosition position);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal readonly record struct WallpaperScene(string? Path, Rect Screen, WallpaperPosition Position, Color DesktopColor);

internal enum WallpaperPosition { Center, Tile, Stretch, Fit, Fill, Span }
