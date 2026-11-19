using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopCarousel;

internal sealed record DesktopShortcut(string Path, string Name, BitmapSource? Icon);

internal static class DesktopShortcuts
{
    public static readonly string[] Patterns = ["*.lnk", "*.url"];

    public static string[] GetDesktopFolders() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        }
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static Task<List<DesktopShortcut>> ReadAsync(IReadOnlyList<string> folders) =>
        Task.Run(() => Read(folders));

    private static List<DesktopShortcut> Read(IReadOnlyList<string> folders)
    {
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var paths = folders.Where(Directory.Exists)
            .SelectMany(folder => Patterns.SelectMany(pattern => Directory.EnumerateFiles(folder, pattern, options)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        // SHGetFileInfo requires COM to be initialized on the calling thread.
        var result = CoInitializeEx(nint.Zero, 0); // COINIT_MULTITHREADED
        Marshal.ThrowExceptionForHR(result);
        nint imageList = 0;
        try
        {
            var imageListId = new Guid("46eb5926-582e-4017-9fdf-e8998daa0950"); // IImageList
            if (SHGetImageList(2, in imageListId, out imageList) < 0) // SHIL_EXTRALARGE
            {
                imageList = 0;
            }
            return paths.Select(path => ReadShortcut(path, imageList))
                .OrderBy(shortcut => shortcut.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        finally
        {
            if (imageList != 0)
            {
                Marshal.Release(imageList);
            }
            CoUninitialize();
        }
    }

    private static DesktopShortcut ReadShortcut(string path, nint imageList)
    {
        const uint icon = 0x100;
        const uint displayName = 0x200;
        const uint systemIconIndex = 0x4000;
        var result = SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), icon | displayName | systemIconIndex);
        var name = result == 0 || string.IsNullOrWhiteSpace(info.DisplayName)
            ? Path.GetFileNameWithoutExtension(path)
            : info.DisplayName;

        nint largeIcon = result != 0 && imageList != 0 ? ImageListGetIcon(imageList, info.IconIndex, 1) : 0;
        try
        {
            return new DesktopShortcut(path, name, ReadIcon(largeIcon) ?? ReadIcon(info.Icon));
        }
        finally
        {
            if (largeIcon != 0)
            {
                DestroyIcon(largeIcon);
            }
            if (info.Icon != 0)
            {
                DestroyIcon(info.Icon);
            }
        }
    }

    private static BitmapSource? ReadIcon(nint icon)
    {
        if (icon == 0)
        {
            return null;
        }

        var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        if ((image.Format == PixelFormats.Pbgra32 || image.Format == PixelFormats.Bgra32) && IsTransparent(image))
        {
            return null;
        }
        image.Freeze();
        return image;
    }

    private static bool IsTransparent(BitmapSource image)
    {
        int stride = image.PixelWidth * 4;
        int size = stride * image.PixelHeight;
        var pixels = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            image.CopyPixels(pixels, stride, 0);
            for (int index = 3; index < size; index += 4)
            {
                if (pixels[index] > 0)
                {
                    return false;
                }
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nuint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int kind, in Guid interfaceId, out nint imageList);

    [DllImport("comctl32.dll", EntryPoint = "ImageList_GetIcon")]
    private static extern nint ImageListGetIcon(nint imageList, int index, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint apartment);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
