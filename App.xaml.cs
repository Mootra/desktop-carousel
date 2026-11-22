using System.IO;
using System.Security;
using System.Windows;
using Forms = System.Windows.Forms;

namespace DesktopCarousel;

public partial class App : Application
{
    private Mutex? _instance;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _menu;
    private Forms.ToolStripMenuItem? _summonItem;
    private Forms.ToolStripMenuItem? _startupItem;
    private GlobalHotkey? _hotkey;
    private System.Drawing.Icon? _icon;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        _instance = new Mutex(false, @"Local\DesktopCarousel", out bool firstInstance);
        if (!firstInstance)
        {
            Shutdown();
            return;
        }

        var widget = new DesktopWidget();
        MainWindow = widget;
        widget.Closed += (_, _) => Shutdown();
        _menu = CreateMenu(widget);
        _icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        _tray = new Forms.NotifyIcon
        {
            Text = "桌面轮播",
            Icon = _icon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        widget.MouseRightButtonUp += (_, _) => _menu.Show(Forms.Cursor.Position);
        _tray.DoubleClick += (_, _) => widget.ToggleSummon();
        widget.SummonChanged += (_, _) => UpdateSummonState(widget);
        widget.Show();

        // 热键要等窗口句柄创建后再注册。
        _hotkey = GlobalHotkey.TryRegister(widget.Handle, widget.ToggleSummon);
        UpdateHotkeyState(widget);
    }

    private Forms.ContextMenuStrip CreateMenu(DesktopWidget widget)
    {
        var menu = new Forms.ContextMenuStrip();
        _summonItem = new Forms.ToolStripMenuItem("唤出轮播");
        _summonItem.Click += (_, _) => widget.ToggleSummon();
        var pause = new Forms.ToolStripMenuItem("暂停轮播");
        pause.Click += (_, _) => widget.Paused = !widget.Paused;
        var hide = new Forms.ToolStripMenuItem("隐藏原桌面图标");
        hide.Click += (_, _) => widget.HideDesktopIcons = !widget.HideDesktopIcons;
        _startupItem = new Forms.ToolStripMenuItem("开机自启动") { ToolTipText = "登录 Windows 后自动启动桌面轮播" };
        _startupItem.Click += (_, _) => ToggleStartup();
        var launch = new Forms.ToolStripMenuItem("快捷方式");
        menu.Items.AddRange([_summonItem, pause, hide, _startupItem, new Forms.ToolStripSeparator()]);
        menu.Items.Add("打开桌面文件夹", null, (_, _) => widget.OpenDesktopFolder());
        menu.Items.Add("退出", null, (_, _) => widget.Close());

        menu.Opening += (_, _) =>
        {
            pause.Checked = widget.Paused;
            hide.Checked = widget.HideDesktopIcons;
            _summonItem.Checked = widget.Summoned;
            _startupItem.Checked = StartupRegistration.IsEnabled();
            foreach (var item in launch.DropDownItems.Cast<Forms.ToolStripItem>().ToArray())
            {
                item.Dispose();
            }
            launch.DropDownItems.Clear();
            foreach (var shortcut in widget.Shortcuts)
            {
                launch.DropDownItems.Add(shortcut.Name.Replace("&", "&&"), null, (_, _) => widget.OpenShortcut(shortcut));
            }
            launch.Enabled = launch.DropDownItems.Count > 0;
        };
        return menu;
    }

    private void ToggleStartup()
    {
        if (_startupItem is null)
        {
            return;
        }

        bool enable = !StartupRegistration.IsEnabled();
        try
        {
            StartupRegistration.SetEnabled(enable);
            _startupItem.Checked = enable;
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            _startupItem.Checked = StartupRegistration.IsEnabled();
            _tray?.ShowBalloonTip(4000, "桌面轮播",
                enable ? "无法写入开机自启动项" : "无法移除开机自启动项", Forms.ToolTipIcon.Warning);
        }
    }

    private void UpdateHotkeyState(DesktopWidget widget)
    {
        if (_summonItem is null)
        {
            return;
        }

        if (_hotkey is not null)
        {
            _summonItem.ShortcutKeyDisplayString = _hotkey.Text;
            _summonItem.ToolTipText = $"按 {_hotkey.Text} 在任何窗口之上唤出或收起轮播";
        }
        else
        {
            _summonItem.ToolTipText = "全局热键被其他程序占用，可从本菜单唤出轮播";
            _tray?.ShowBalloonTip(6000, "桌面轮播",
                "全局热键注册失败，Ctrl+Alt+D、Win+Alt+D 等组合已被占用。可从托盘菜单唤出轮播。",
                Forms.ToolTipIcon.Warning);
        }
        UpdateSummonState(widget);
    }

    private void UpdateSummonState(DesktopWidget widget)
    {
        if (_summonItem is not null)
        {
            _summonItem.Checked = widget.Summoned;
        }
    }

    protected override void OnExit(ExitEventArgs args)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        _menu?.Dispose();
        _icon?.Dispose();
        _instance?.Dispose();
        base.OnExit(args);
    }
}
