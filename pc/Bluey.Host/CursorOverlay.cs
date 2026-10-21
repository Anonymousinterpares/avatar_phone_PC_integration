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
    private readonly WpfControls.Grid _berry;
    private PointD _current;
    private PointD _target;
    private bool _hasTarget;
    private bool _hiddenByUser;
    private bool _hiddenForCapture;
    private bool _dragging;
    private bool _thinking;
    private double _thinkTime;
    private System.Windows.Point _grab;
    private readonly Media.ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly Media.TranslateTransform _bob = new();
    private readonly Media.RotateTransform _tilt = new();
    private readonly Media.TranslateTransform _eyeLeft = new();
    private readonly Media.TranslateTransform _eyeRight = new();
    private readonly Media.ScaleTransform _blinkLeft = new() { ScaleX = 1, ScaleY = 1 };
    private readonly Media.ScaleTransform _blinkRight = new() { ScaleX = 1, ScaleY = 1 };
    private readonly Media.SolidColorBrush _bodyBrush = new(Media.Color.FromRgb(47, 92, 214));

    public event Action<bool>? UserHiddenChanged;
    public event Action? TalkRequested;

    public CursorOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Width = 240;
        Height = 150;
        var root = new WpfControls.Grid();
        _berry = new WpfControls.Grid
        {
            Width = 72,
            Height = 72,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            Background = Media.Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.SizeAll,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.72),
        };
        var pose = new Media.TransformGroup();
        pose.Children.Add(_scale);
        pose.Children.Add(_tilt);
        pose.Children.Add(_bob);
        _berry.RenderTransform = pose;
        _berry.Children.Add(new WpfShapes.Ellipse
        {
            Width = 54,
            Height = 54,
            Fill = _bodyBrush,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });
        _berry.Children.Add(Eye(-16, _eyeLeft, _blinkLeft));
        _berry.Children.Add(Eye(16, _eyeRight, _blinkRight));
        _berry.MouseLeftButtonDown += BerryDown;
        _berry.MouseMove += BerryMove;
        _berry.MouseLeftButtonUp += BerryUp;
        _berry.MouseRightButtonUp += BerryMenu;
        root.Children.Add(_berry);
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
            IsHitTestVisible = false,
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
        SourceInitialized += (_, _) => ApplyPointerStyle();
        Loaded += (_, _) =>
        {
            GoHome();
            _timer.Start();
        };
    }

    public void SetThinking(bool thinking) => Dispatcher.Invoke(() =>
    {
        _thinking = thinking;
        if (!thinking) RestPose();
    });

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

    public void SetUserHidden(bool hidden) => Dispatcher.Invoke(() =>
    {
        if (_hiddenByUser == hidden) return;
        _hiddenByUser = hidden;
        ApplyOpacity();
        UserHiddenChanged?.Invoke(hidden);
    });

    public void SetVisible(bool visible) => Dispatcher.Invoke(() =>
    {
        _hiddenForCapture = !visible;
        ApplyOpacity();
    });

    private void ApplyOpacity()
    {
        var hidden = _hiddenByUser || _hiddenForCapture;
        Opacity = hidden ? 0 : 1;
        IsHitTestVisible = !hidden;
        if (_dragging && hidden) EndDrag();
        ApplyPointerStyle();
    }

    private void BerryDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragging = true;
        _grab = e.GetPosition(this);
        _berry.CaptureMouse();
        e.Handled = true;
    }

    private void BerryMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        Left += pos.X - _grab.X;
        Top += pos.Y - _grab.Y;
        var dpi = Media.VisualTreeHelper.GetDpi(this);
        var x = (Left + Width / 2) * dpi.DpiScaleX;
        var y = (Top + Height - 20) * dpi.DpiScaleY;
        _current = _target = new PointD(x, y);
        _hasTarget = true;
        e.Handled = true;
    }

    private void BerryUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        _dragging = false;
        if (_berry.IsMouseCaptured) _berry.ReleaseMouseCapture();
    }

    private void BerryMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        EndDrag();
        e.Handled = true;
        var menu = new BerryDotMenu();
        menu.HideChosen += () => SetUserHidden(true);
        menu.TalkChosen += () => TalkRequested?.Invoke();
        var origin = _berry.PointToScreen(new System.Windows.Point(_berry.ActualWidth - 8, 8));
        var dpi = Media.VisualTreeHelper.GetDpi(this);
        menu.Left = origin.X / dpi.DpiScaleX;
        menu.Top = origin.Y / dpi.DpiScaleY;
        menu.Show();
    }

    private void GoHome()
    {
        var screen = Screen.PrimaryScreen?.Bounds ?? Screen.AllScreens[0].Bounds;
        _target = new PointD(screen.Left + screen.Width / 2.0, screen.Bottom - 80);
        _hasTarget = true;
        if (_current.X == 0 && _current.Y == 0) _current = _target;
    }

    private void Step()
    {
        Think();
        if (!_hasTarget) return;
        _current = new PointD(_current.X + (_target.X - _current.X) * 0.28, _current.Y + (_target.Y - _current.Y) * 0.28);
        var dpi = Media.VisualTreeHelper.GetDpi(this);
        Left = _current.X / dpi.DpiScaleX - Width / 2;
        Top = _current.Y / dpi.DpiScaleY - Height + 20;
    }

    private WpfShapes.Ellipse Eye(double x, Media.TranslateTransform shift, Media.ScaleTransform blink)
    {
        var eyes = new Media.TransformGroup();
        eyes.Children.Add(blink);
        eyes.Children.Add(shift);
        return new WpfShapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Media.Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(x, 8, 0, 0),
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = eyes,
        };
    }

    private void Think()
    {
        if (!_thinking || _dragging)
        {
            if (!_thinking) RestPose();
            return;
        }
        _thinkTime += 0.016;
        var stage = (int)(_thinkTime / 0.7) % 6;
        var wave = Math.Sin((_thinkTime / 0.7 % 1.0) * Math.PI);
        double scaleX = 1, scaleY = 1, bob = 0, tilt = 0, eye = 0, blink = 1;
        switch (stage)
        {
            case 0:
                scaleX = scaleY = 1 + 0.1 * wave;
                break;
            case 1:
                eye = -6 * wave;
                tilt = -8 * wave;
                break;
            case 2:
                eye = 6 * wave;
                tilt = 8 * wave;
                break;
            case 3:
                bob = -16 * wave;
                scaleY = 1 + 0.16 * wave;
                scaleX = 1 - 0.08 * wave;
                break;
            case 4:
                bob = 4 * wave;
                scaleY = 1 - 0.16 * wave;
                scaleX = 1 + 0.12 * wave;
                tilt = 10 * Math.Sin(wave * Math.PI);
                break;
            default:
                blink = wave > 0.72 ? 0.15 : 1;
                scaleX = scaleY = 1 + 0.05 * wave;
                var lift = (byte)(40 * wave);
                _bodyBrush.Color = Media.Color.FromRgb((byte)(47 + lift), (byte)(92 + lift / 2), 230);
                break;
        }
        if (stage != 5) _bodyBrush.Color = Media.Color.FromRgb(47, 92, 214);
        _scale.ScaleX = scaleX;
        _scale.ScaleY = scaleY;
        _bob.Y = bob;
        _tilt.Angle = tilt;
        _eyeLeft.X = _eyeRight.X = eye;
        _blinkLeft.ScaleY = _blinkRight.ScaleY = blink;
    }

    private void RestPose()
    {
        _thinkTime = 0;
        _scale.ScaleX = _scale.ScaleY = 1;
        _bob.Y = 0;
        _tilt.Angle = 0;
        _eyeLeft.X = _eyeRight.X = 0;
        _blinkLeft.ScaleY = _blinkRight.ScaleY = 1;
        _bodyBrush.Color = Media.Color.FromRgb(47, 92, 214);
    }

    private void ApplyPointerStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, -20);
        const int transparent = 0x20;
        style |= 0x80000 | 0x08000000 | 0x80;
        if (_hiddenByUser || _hiddenForCapture) style |= transparent;
        else style &= ~transparent;
        SetWindowLong(hwnd, -20, style);
        SetWindowDisplayAffinity(hwnd, 0x11);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
