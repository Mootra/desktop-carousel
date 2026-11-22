using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopCarousel;

/// <summary>全局热键：唤出/收起轮播。注册失败时按顺序尝试备用组合。</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4A11;
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    private const uint Win = 0x0008;
    private const uint NoRepeat = 0x4000;
    private const uint KeyD = 0x44;

    private static readonly (uint Modifiers, string Text)[] Candidates =
    [
        (Control | Alt, "Ctrl+Alt+D"),
        (Win | Alt, "Win+Alt+D"),
        (Control | Alt | Shift, "Ctrl+Alt+Shift+D"),
    ];

    private readonly nint _window;
    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private bool _disposed;

    public string Text { get; }

    private GlobalHotkey(nint window, HwndSource source, Action invoke, string text)
    {
        _window = window;
        _source = source;
        _hook = (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                try
                {
                    invoke();
                }
                catch (Exception)
                {
                    // 热键回调不能把整个桌面控件带崩。
                }
                handled = true;
            }
            return 0;
        };
        Text = text;
        source.AddHook(_hook);
    }

    /// <summary>注册第一个可用的组合；全部被占用时返回 null。</summary>
    public static GlobalHotkey? TryRegister(nint window, Action invoke)
    {
        if (window == 0 || HwndSource.FromHwnd(window) is not { } source)
        {
            return null;
        }

        foreach (var (modifiers, text) in Candidates)
        {
            if (!RegisterHotKey(window, HotkeyId, modifiers | NoRepeat, KeyD))
            {
                continue;
            }
            try
            {
                return new GlobalHotkey(window, source, invoke, text);
            }
            catch
            {
                UnregisterHotKey(window, HotkeyId);
                throw;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        UnregisterHotKey(_window, HotkeyId);
        _source.RemoveHook(_hook);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
