using System.Windows;
using Forms = System.Windows.Forms;

namespace DesktopCarousel;

public partial class App : Application
{
    private Mutex? _instance;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _menu;
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
        widget.Show();
    }

    private Forms.ContextMenuStrip CreateMenu(DesktopWidget widget)
    {
        var menu = new Forms.ContextMenuStrip();
        var launch = new Forms.ToolStripMenuItem("快捷方式");
        var pause = new Forms.ToolStripMenuItem("暂停轮播");
        pause.Click += (_, _) => widget.Paused = !widget.Paused;
        var hide = new Forms.ToolStripMenuItem("隐藏原桌面图标");
        hide.Click += (_, _) => widget.HideDesktopIcons = !widget.HideDesktopIcons;
        menu.Items.AddRange([launch, pause, hide, new Forms.ToolStripSeparator()]);
        menu.Items.Add("打开桌面文件夹", null, (_, _) => widget.OpenDesktopFolder());
        menu.Items.Add("退出", null, (_, _) => widget.Close());

        menu.Opening += (_, _) =>
        {
            pause.Checked = widget.Paused;
            hide.Checked = widget.HideDesktopIcons;
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

    protected override void OnExit(ExitEventArgs args)
    {
        _tray?.Dispose();
        _menu?.Dispose();
        _icon?.Dispose();
        _instance?.Dispose();
        base.OnExit(args);
    }
}
