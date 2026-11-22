using System.Runtime.InteropServices;

namespace DesktopCarousel;

internal readonly record struct DesktopSurface(nint Host, nint Icons, uint ExplorerProcessId);

internal static class DesktopShell
{
    private const int Style = -16;
    private const int ExtendedStyle = -20;
    private const int Visible = 0x10000000;
    private const int Topmost = 0x00000008;
    private const uint PreviousWindow = 3;
    private const uint PositionOnlyZOrder = 0x0001 | 0x0002 | 0x0010;

    public static DesktopSurface FindSurface()
    {
        nint shell = GetShellWindow();
        if (shell == 0)
        {
            return default;
        }

        GetWindowThreadProcessId(shell, out uint processId);
        nint view = FindWindowEx(shell, 0, "SHELLDLL_DefView", null);
        if (view != 0)
        {
            return new DesktopSurface(shell, FindWindowEx(view, 0, "SysListView32", null), processId);
        }

        // Older Explorer layouts can keep the icon view in a separate WorkerW.
        for (nint worker = FindWindowEx(0, 0, "WorkerW", null);
            worker != 0;
            worker = FindWindowEx(0, worker, "WorkerW", null))
        {
            GetWindowThreadProcessId(worker, out uint workerProcessId);
            if (workerProcessId == processId && (view = FindWindowEx(worker, 0, "SHELLDLL_DefView", null)) != 0)
            {
                return new DesktopSurface(worker, FindWindowEx(view, 0, "SysListView32", null), processId);
            }
        }

        return new DesktopSurface(shell, 0, processId);
    }

    public static void ConfigureWidget(nint window)
    {
        const int toolWindow = 0x00000080;
        const int appWindow = 0x00040000;
        const int noActivate = 0x08000000;
        SetWindowLong(window, ExtendedStyle, (GetWindowLong(window, ExtendedStyle) | toolWindow | noActivate) & ~appWindow);
    }

    /// <summary>唤出时临时置顶并允许激活，收起时回到桌面层并恢复不抢焦点。</summary>
    public static void SetSummoned(nint window, bool summoned)
    {
        const int noActivate = 0x08000000;
        int style = GetWindowLong(window, ExtendedStyle);
        int updated = summoned ? style & ~noActivate : style | noActivate;
        if (updated != style)
        {
            SetWindowLong(window, ExtendedStyle, updated);
        }

        // HWND_TOPMOST / HWND_NOTOPMOST；之后由 PlaceOnDesktop 把窗口放回图标层之上。
        SetWindowPos(window, summoned ? -1 : -2, 0, 0, 0, 0, PositionOnlyZOrder);
    }

    public static void Activate(nint window)
    {
        nint foreground = GetForegroundWindow();
        uint foregroundThread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        uint currentThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    public static void PlaceOnDesktop(nint window, nint desktopHost)
    {
        if (desktopHost == 0)
        {
            SetWindowPos(window, 1, 0, 0, 0, 0, PositionOnlyZOrder); // HWND_BOTTOM
            return;
        }

        bool desktopTopmost = IsTopmost(desktopHost);
        if (IsTopmost(window) && !desktopTopmost)
        {
            SetWindowPos(window, -2, 0, 0, 0, 0, PositionOnlyZOrder); // HWND_NOTOPMOST
        }

        nint aboveDesktop = GetWindow(desktopHost, PreviousWindow);
        // Explorer and the composition engine can have hidden windows between visible layers.
        while (aboveDesktop != 0 && !IsWindowVisible(aboveDesktop))
        {
            aboveDesktop = GetWindow(aboveDesktop, PreviousWindow);
        }
        if (aboveDesktop == window)
        {
            return;
        }

        // Put the widget immediately above the icon host, in the same Z-order band.
        // When Explorer raises its desktop, the widget follows that layer.
        if (aboveDesktop == 0 || (IsTopmost(aboveDesktop) && !desktopTopmost))
        {
            aboveDesktop = desktopTopmost ? -1 : 0; // HWND_TOPMOST or HWND_TOP
        }
        SetWindowPos(window, aboveDesktop, 0, 0, 0, 0, PositionOnlyZOrder);
    }

    public static bool IconsVisible(nint icons) => icons != 0 && (GetWindowLong(icons, Style) & Visible) != 0;

    public static bool SetIconVisibility(DesktopSurface expected, bool show)
    {
        var current = FindSurface();
        return current.Icons != 0 && current.Icons == expected.Icons && current.ExplorerProcessId == expected.ExplorerProcessId
            && ShowWindowAsync(current.Icons, show ? 8 : 0); // SW_SHOWNA or SW_HIDE
    }

    public static bool ForegroundCoversWidget(nint widget, nint desktopHost)
    {
        nint foreground = GetForegroundWindow();
        return foreground != 0 && foreground != desktopHost && foreground != GetShellWindow() && foreground != widget
            && !IsIconic(foreground) && GetWindowRect(foreground, out var front) && GetWindowRect(widget, out var behind)
            && front.Left <= behind.Left && front.Top <= behind.Top && front.Right >= behind.Right && front.Bottom >= behind.Bottom;
    }

    private static bool IsTopmost(nint window) => (GetWindowLong(window, ExtendedStyle) & Topmost) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint previous, string className, string? title);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool join);
}
