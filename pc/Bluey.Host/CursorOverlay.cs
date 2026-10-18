using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Bluey.Desk;
using Media = System.Windows.Media;
using WpfControls = System.Windows.Controls;
using WpfShapes = System.Windows.Shapes;

namespace Bluey.Host;

public sealed class CursorOverlay : Window
{
    private readonly WpfControls.TextBlock _caption;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _captionTimer;
    private PointD _current;
    private PointD _target;
    private bool _hasTarget;

    public CursorOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Media.Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Width = 240;
        Height = 150;
        IsHitTestVisible = false;
        var root = new WpfControls.Grid { IsHitTestVisible = false };
        root.Children.Add(new WpfShapes.Ellipse
        {
            Width = 54,
            Height = 54,
            Fill = new Media.SolidColorBrush(Media.Color.FromRgb(47, 92, 214)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
        });
        root.Children.Add(new WpfShapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Media.Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(-16, 0, 0, 28),
        });
        root.Children.Add(new WpfShapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Media.Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 0, 28),
        });
        _caption = new WpfControls.TextBlock
        {
            Foreground = Media.Brushes.White,
            Background = new Media.SolidColorBrush(Media.Color.FromArgb(210, 20, 24, 38)),
            Padding = new Thickness(8, 4, 8, 4),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 220,
            FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Visibility = System.Windows.Visibility.Collapsed,
        };
        root.Children.Add(_caption);
        Content = root;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Step();
        _captionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _captionTimer.Tick += (_, _) =>
        {
            _captionTimer.Stop();
            _caption.Visibility = Visibility.Collapsed;
            _caption.Text = "";
        };
        SourceInitialized += (_, _) => MakeClickThrough();
        Loaded += (_, _) =>
        {
            GoHome();
            _timer.Start();
        };
    }

    public void SetCaption(string? text)
    {
        Dispatcher.Invoke(() =>
        {
            _captionTimer.Stop();
            if (string.IsNullOrWhiteSpace(text))
            {
                _caption.Visibility = Visibility.Collapsed;
                _caption.Text = "";
                return;
            }
            _caption.Text = text.Length > 280 ? text[..280] : text;
            _caption.Visibility = Visibility.Visible;
            _captionTimer.Start();
        });
    }

    public void PointAt(PointD? point)
    {
        Dispatcher.Invoke(() =>
        {
            if (point == null) GoHome();
            else
            {
                _target = point.Value;
                _hasTarget = true;
            }
        });
    }

    public void SetVisible(bool visible) => Dispatcher.Invoke(() => Opacity = visible ? 1 : 0);

    private void GoHome()
    {
        var screen = Screen.PrimaryScreen?.Bounds ?? Screen.AllScreens[0].Bounds;
        _target = new PointD(screen.Left + screen.Width / 2.0, screen.Bottom - 80);
        _hasTarget = true;
        if (_current.X == 0 && _current.Y == 0) _current = _target;
    }

    private void Step()
    {
        if (!_hasTarget) return;
        _current = new PointD(_current.X + (_target.X - _current.X) * 0.28, _current.Y + (_target.Y - _current.Y) * 0.28);
        var dpi = Media.VisualTreeHelper.GetDpi(this);
        Left = _current.X / dpi.DpiScaleX - Width / 2;
        Top = _current.Y / dpi.DpiScaleY - Height + 20;
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, style | 0x20 | 0x80000 | 0x08000000 | 0x80);
        SetWindowDisplayAffinity(hwnd, 0x11);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
