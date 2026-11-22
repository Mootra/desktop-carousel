using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopCarousel;

/// <summary>托盘菜单的配色、圆角和自绘菜单项。浅色为主，跟随系统深色模式。</summary>
internal static class MenuTheme
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmcpRound = 2;

    public static bool Dark { get; } = ReadDarkPreference();

    public static Color Background => Dark ? Color.FromArgb(32, 33, 36) : Color.FromArgb(255, 255, 255);
    public static Color Border => Dark ? Color.FromArgb(64, 65, 72) : Color.FromArgb(233, 233, 236);
    public static Color TextColor => Dark ? Color.FromArgb(236, 236, 241) : Color.FromArgb(31, 32, 34);
    public static Color MutedText => Dark ? Color.FromArgb(150, 150, 160) : Color.FromArgb(138, 139, 145);
    public static Color DisabledText => Dark ? Color.FromArgb(104, 105, 113) : Color.FromArgb(178, 179, 185);
    public static Color Hover => Dark ? Color.FromArgb(49, 50, 57) : Color.FromArgb(244, 244, 246);
    public static Color Pressed => Dark ? Color.FromArgb(59, 60, 68) : Color.FromArgb(235, 235, 238);
    public static Color SeparatorColor => Dark ? Color.FromArgb(58, 59, 66) : Color.FromArgb(236, 236, 239);
    public static Color Accent => Color.FromArgb(16, 163, 127);

    public static Font CreateFont()
    {
        var font = new Font("Microsoft YaHei UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        return font.FontFamily.Name == "Microsoft YaHei UI"
            ? font
            : new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
    }

    public static void StyleDropDown(ToolStripDropDown dropDown)
    {
        dropDown.Renderer = new MenuRenderer();
        dropDown.BackColor = Background;
        dropDown.ForeColor = TextColor;
        if (dropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
        }
        dropDown.DropShadowEnabled = true;
        dropDown.Padding = new Padding(6);
        dropDown.AutoSize = true;
        dropDown.Opened += (_, _) => ApplyRoundedCorners(dropDown.Handle);
    }

    public static ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new MenuRenderer(),
            BackColor = Background,
            ForeColor = TextColor,
            Font = CreateFont(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            ShowItemToolTips = true,
            DropShadowEnabled = true,
            Padding = new Padding(6),
            AutoSize = true,
        };
        menu.Opened += (_, _) => ApplyRoundedCorners(menu.Handle);
        return menu;
    }


    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter <= 1)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void ApplyRoundedCorners(nint handle)
    {
        if (handle == 0)
        {
            return;
        }
        int preference = DwmcpRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    private static bool ReadDarkPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}

internal sealed class MenuRenderer : ToolStripRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(MenuTheme.Background);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var bounds = new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1f, e.ToolStrip.Height - 1f);
        float radius = Math.Max(6f, e.ToolStrip.Font.Height * 0.55f);
        using var path = MenuTheme.RoundedRect(bounds, radius);
        using var pen = new Pen(MenuTheme.Border, 1f);
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
        e.Graphics.SmoothingMode = smoothing;
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var graphics = e.Graphics;
        float fontHeight = e.ToolStrip?.Font?.Height ?? 15f;
        int inset = (int)Math.Round(fontHeight * 0.85f);
        float y = e.Item.Height / 2f;
        using var pen = new Pen(MenuTheme.SeparatorColor, 1f);
        graphics.DrawLine(pen, inset, y, (e.ToolStrip?.Width ?? 0) - inset, y);
    }
}

/// <summary>自绘菜单项：圆角悬停、左侧勾选、右侧快捷键与子菜单箭头。</summary>
internal sealed class ThemedMenuItem : ToolStripMenuItem
{
    public ThemedMenuItem(string text)
        : base(text)
    {
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Hint
    {
        get => ShortcutKeyDisplayString;
        set => ShortcutKeyDisplayString = value;
    }

    private ToolStripDropDown? _submenu;

    public void PrepareSubmenu()
    {
        var dropDown = DropDown;
        MenuTheme.StyleDropDown(dropDown);
        _submenu = dropDown;
    }


    private Font CurrentFont => Font ?? SystemFonts.MessageBoxFont ?? Control.DefaultFont;

    private float FontHeight => CurrentFont.Height;

    private int Gutter => (int)Math.Round(FontHeight * 1.85f);

    public override Size GetPreferredSize(Size constrainingSize)
    {
        var size = base.GetPreferredSize(constrainingSize);
        int height = Math.Max(size.Height, (int)Math.Round(FontHeight * 1.95f));
        int width = size.Width;
        return new Size(width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var bounds = new Rectangle(Point.Empty, Size);
        bool highlighted = Selected || Pressed;
        if (highlighted)
        {
            var hover = new RectangleF(bounds.X + 2, bounds.Y + 1, bounds.Width - 4, bounds.Height - 2);
            using var path = MenuTheme.RoundedRect(hover, Math.Max(5f, FontHeight * 0.42f));
            using var brush = new SolidBrush(Pressed ? MenuTheme.Pressed : MenuTheme.Hover);
            var smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
            graphics.SmoothingMode = smoothing;
        }

        if (Checked)
        {
            DrawCheck(graphics);
        }


        var textColor = !Enabled ? MenuTheme.DisabledText : MenuTheme.TextColor;
        var textBounds = new Rectangle(Gutter, 0, Math.Max(0, Width - Gutter), Height);
        TextRenderer.DrawText(graphics, Text ?? string.Empty, CurrentFont, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (_submenu is not null)
        {
            DrawArrow(graphics, Enabled);
        }
        else if (!string.IsNullOrEmpty(Hint))
        {
            int right = (int)Math.Round(FontHeight * 0.55f);
            int hintWidth = TextRenderer.MeasureText(Hint, CurrentFont, Size.Empty, TextFormatFlags.NoPrefix).Width;
            var hintBounds = new Rectangle(Math.Max(Gutter, Width - hintWidth - right), 0, hintWidth, Height);
            TextRenderer.DrawText(graphics, Hint, CurrentFont, hintBounds, MenuTheme.MutedText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawCheck(Graphics graphics)
    {
        float size = FontHeight * 0.62f;
        float x = Gutter * 0.34f;
        float centerY = Height / 2f;
        using var pen = new Pen(MenuTheme.Accent, Math.Max(1.5f, FontHeight * 0.105f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawLines(pen,
        [
            new PointF(x, centerY + size * 0.10f),
            new PointF(x + size * 0.34f, centerY + size * 0.44f),
            new PointF(x + size * 0.80f, centerY - size * 0.34f),
        ]);
        graphics.SmoothingMode = smoothing;
    }

    private void DrawArrow(Graphics graphics, bool enabled)
    {
        float x = Width - FontHeight * 1.0f;
        float centerY = Height / 2f;
        using var pen = new Pen(enabled ? MenuTheme.MutedText : MenuTheme.DisabledText, Math.Max(1.4f, FontHeight * 0.1f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawLines(pen,
        [
            new PointF(x, centerY - FontHeight * 0.22f),
            new PointF(x + FontHeight * 0.26f, centerY),
            new PointF(x, centerY + FontHeight * 0.22f),
        ]);
        graphics.SmoothingMode = smoothing;
    }
}
