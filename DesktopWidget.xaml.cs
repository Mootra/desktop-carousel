using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopCarousel;

public partial class DesktopWidget : Window
{
    private const double PreferredWidth = 816;
    private const double SlotsPerSecond = 0.25;
    private readonly IReadOnlyList<string> _folders;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly DispatcherTimer _desktopTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _reloadTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _motionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<ShortcutTile> _tiles = [];
    private List<DesktopShortcut> _shortcuts = [];
    private Rect _workArea;
    private nint _handle;
    private bool _loading;
    private bool _closed;
    private bool _covered;
    private bool _paused;
    private bool _hideDesktopIcons = true;
    private bool _acquiringIconLease;
    private long _retryIconLeaseAfter;
    private DesktopIconLease? _iconLease;
    private double _position;
    private double _wheelDistance;
    private long _lastFrame;

    public DesktopWidget() : this(DesktopShortcuts.GetDesktopFolders()) { }

    internal DesktopWidget(IReadOnlyList<string> folders)
    {
        _folders = folders;
        InitializeComponent();
        PositionWidget();
        _desktopTimer.Tick += RefreshDesktopLayer;
        _reloadTimer.Tick += ReloadShortcuts;
        _motionTimer.Tick += RenderArc;
        Loaded += StartWatching;
        SizeChanged += (_, _) => LayoutArc();
        MouseEnter += (_, _) => UpdateRenderLoop();
        MouseLeave += (_, _) => UpdateRenderLoop();
        PreviewMouseWheel += ScrollArc;
    }

    internal IReadOnlyList<DesktopShortcut> Shortcuts => _shortcuts;

    internal bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            UpdateRenderLoop();
        }
    }

    internal bool HideDesktopIcons
    {
        get => _hideDesktopIcons;
        set
        {
            _hideDesktopIcons = value;
            _retryIconLeaseAfter = 0;
            RefreshDesktopLayer(this, EventArgs.Empty);
        }
    }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        _handle = new WindowInteropHelper(this).Handle;
        DesktopShell.ConfigureWidget(_handle);
        DesktopShell.PlaceOnDesktop(_handle, DesktopShell.FindSurface().Host);
    }

    private void PositionWidget()
    {
        _workArea = SystemParameters.WorkArea;
        Width = Math.Min(PreferredWidth, _workArea.Width - 20);
        Left = _workArea.Left + (_workArea.Width - Width) / 2;
        Top = Math.Max(_workArea.Top, _workArea.Bottom - Height - 20);
    }

    private async void RefreshDesktopLayer(object? sender, EventArgs args)
    {
        if (_closed || _handle == 0)
        {
            return;
        }
        if (_workArea != SystemParameters.WorkArea)
        {
            PositionWidget();
        }

        var surface = DesktopShell.FindSurface();
        DesktopShell.PlaceOnDesktop(_handle, surface.Host);
        _covered = DesktopShell.ForegroundCoversWidget(_handle, surface.Host);
        UpdateRenderLoop();

        bool wantHidden = _hideDesktopIcons && _shortcuts.Count > 0;
        if (_iconLease is { } current && (!wantHidden || current.Surface != surface || !current.RestorerRunning))
        {
            current.Dispose();
            _iconLease = null;
        }

        if (!wantHidden || !DesktopShell.IconsVisible(surface.Icons))
        {
            return;
        }
        if (_iconLease is { } active)
        {
            active.Hide();
            return;
        }
        if (_acquiringIconLease || Environment.TickCount64 < _retryIconLeaseAfter)
        {
            return;
        }

        _acquiringIconLease = true;
        try
        {
            var lease = await DesktopIconLease.StartAsync(surface, _lifetime.Token);
            if (lease is not null && !_closed && _hideDesktopIcons && _shortcuts.Count > 0 &&
                DesktopShell.IconsVisible(surface.Icons) && lease.Hide())
            {
                _iconLease = lease;
            }
            else
            {
                lease?.Dispose();
                _retryIconLeaseAfter = Environment.TickCount64 + 5_000;
            }
        }
        finally
        {
            _acquiringIconLease = false;
        }
    }

    private void StartWatching(object sender, RoutedEventArgs args)
    {
        Loaded -= StartWatching;
        foreach (var folder in _folders)
        {
            var watcher = new FileSystemWatcher
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
            };
            foreach (var pattern in DesktopShortcuts.Patterns)
            {
                watcher.Filters.Add(pattern);
            }
            watcher.Created += ScheduleReload;
            watcher.Deleted += ScheduleReload;
            watcher.Changed += ScheduleReload;
            watcher.Renamed += ScheduleReload;
            watcher.Error += ScheduleReload;
            try
            {
                watcher.Path = folder;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                watcher.Dispose();
            }
        }

        _desktopTimer.Start();
        ReloadShortcuts(sender, args);
    }

    private void ScheduleReload(object sender, EventArgs args) => Dispatcher.InvokeAsync(() =>
    {
        if (!_closed)
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        }
    }, DispatcherPriority.Background);

    private async void ReloadShortcuts(object? sender, EventArgs args)
    {
        _reloadTimer.Stop();
        if (_loading)
        {
            _reloadTimer.Start();
            return;
        }

        _loading = true;
        try
        {
            var updated = await DesktopShortcuts.ReadAsync(_folders);
            if (!_closed)
            {
                SetShortcuts(updated);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException)
        {
            StatusText.Text = "暂时无法读取桌面";
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetShortcuts(List<DesktopShortcut> updated)
    {
        double centered = Math.Floor(_position + 0.5);
        var current = _shortcuts.Count == 0 ? null : _shortcuts[(int)centered % _shortcuts.Count].Path;
        var newIndex = updated.FindIndex(shortcut => StringComparer.OrdinalIgnoreCase.Equals(shortcut.Path, current));
        _position = newIndex < 0 ? 0 : newIndex + (_position - centered);
        _wheelDistance = 0;
        _shortcuts = updated;
        _tiles.Clear();
        IconArc.Children.Clear();
        var style = (Style)FindResource("ShortcutTile");
        foreach (var shortcut in _shortcuts)
        {
            var scale = new ScaleTransform(1, 1, 56, 34);
            var offset = new TranslateTransform();
            var transform = new TransformGroup();
            transform.Children.Add(scale);
            transform.Children.Add(offset);
            var button = new Button { DataContext = shortcut, Style = style, RenderTransform = transform };
            button.Click += (_, _) => OpenShortcut(shortcut);
            _tiles.Add(new ShortcutTile(button, scale, offset));
            IconArc.Children.Add(button);
        }

        StatusText.Text = "桌面暂无快捷方式";
        Status.Visibility = _shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LayoutArc();
        RefreshDesktopLayer(this, EventArgs.Empty);
    }

    private bool AutoMotion => !_paused && !IsMouseOver && _tiles.Count > 1 && SystemParameters.ClientAreaAnimation;

    private void UpdateRenderLoop()
    {
        bool render = !_closed && !_covered && _tiles.Count > 1 && (AutoMotion || _wheelDistance != 0);
        if (render == _motionTimer.IsEnabled)
        {
            return;
        }
        if (render)
        {
            _lastFrame = Stopwatch.GetTimestamp();
            _motionTimer.Start();
        }
        else
        {
            _motionTimer.Stop();
        }
    }

    private void RenderArc(object? sender, EventArgs args)
    {
        long timestamp = Stopwatch.GetTimestamp();
        double seconds = Math.Min(Stopwatch.GetElapsedTime(_lastFrame, timestamp).TotalSeconds, 0.05);
        double wheelStep = Math.Abs(_wheelDistance) < 0.005
            ? _wheelDistance
            : _wheelDistance * (1 - Math.Exp(-15 * seconds));
        _wheelDistance -= wheelStep;
        double step = wheelStep + (AutoMotion ? SlotsPerSecond * seconds : 0);
        Advance(step);
        _lastFrame = timestamp;
        UpdateRenderLoop();
    }

    private void ScrollArc(object sender, MouseWheelEventArgs args)
    {
        if (_tiles.Count < 2)
        {
            return;
        }
        double distance = -args.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        if (SystemParameters.ClientAreaAnimation)
        {
            _wheelDistance += distance;
            UpdateRenderLoop();
        }
        else
        {
            Advance(distance);
        }
        args.Handled = true;
    }

    private void Advance(double distance)
    {
        _position = (_position + distance) % _tiles.Count;
        if (_position < 0)
        {
            _position += _tiles.Count;
        }
        LayoutArc();
    }

    private void LayoutArc()
    {
        if (_tiles.Count == 0 || ActualWidth <= 0)
        {
            return;
        }

        double halfWidth = Math.Min(341, Math.Max(36, ActualWidth / 2 - 60));
        double sideSlots = Math.Min(3.7, halfWidth / 86);
        double edge = Math.Min(_tiles.Count / 2.0, sideSlots);
        double radius = halfWidth / Math.Sin(sideSlots * 0.30);
        int centered = (int)Math.Floor(_position + 0.5) % _tiles.Count;
        for (int index = 0; index < _tiles.Count; index++)
        {
            var tile = _tiles[index];
            double offset = Math.IEEERemainder(index - _position, _tiles.Count);
            double fade = Math.Clamp((edge - Math.Abs(offset)) / 0.65, 0, 1);
            if (_tiles.Count == 1)
            {
                fade = 1;
            }
            tile.Button.Visibility = fade <= 0 ? Visibility.Hidden : Visibility.Visible;
            if (fade <= 0)
            {
                continue;
            }

            double angle = offset * 0.30;
            double depth = Math.Cos(angle);
            tile.Offset.X = ActualWidth / 2 - 56 + radius * Math.Sin(angle);
            tile.Offset.Y = 13 + 73 * (1 - depth);
            tile.Scale.ScaleX = tile.Scale.ScaleY = 0.61 + 0.39 * depth;
            tile.Button.Opacity = fade * fade * (3 - 2 * fade) * (0.67 + 0.33 * depth);
            tile.Button.IsHitTestVisible = fade > 0.28;
            tile.Button.Tag = index == centered ? "Center" : null;
        }
    }

    internal void OpenShortcut(DesktopShortcut shortcut) => OpenShellItem(shortcut.Path, $"无法打开“{shortcut.Name}”");

    internal void OpenDesktopFolder() => OpenShellItem("shell:Desktop", "无法打开桌面文件夹");

    private void OpenShellItem(string target, string errorMessage)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(errorMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnClosed(EventArgs args)
    {
        _closed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _desktopTimer.Stop();
        _reloadTimer.Stop();
        UpdateRenderLoop();
        _iconLease?.Dispose();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        base.OnClosed(args);
    }

    private sealed record ShortcutTile(Button Button, ScaleTransform Scale, TranslateTransform Offset);
}
