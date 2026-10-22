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
    private readonly WpfControls.Image _sprite;
    private readonly BerryFilm? _film;
    private BerryFilm.Clip? _clip;
    private int _frame;
    private int _frameTime;
    private string _act = "";
    private bool _traveling;
    private bool _posePlaying;
    private bool _talking;
    private bool _talkForAudio;
    private DateTime _talkUntil;
    private bool _asleep;
    private bool _sleepTrip;
    private bool _teleport;
    private int _telePhase;
    private int _teleKind;
    private double _teleT;
    private PointD _teleDest;
    private double _drift;
    private double _size = 1;
    private double _speed = 1;
    private string _sleepCorner = "bottom-right";
    private readonly WpfShapes.Ellipse _dot;
    private string _pose = "wave";
    private DateTime _nextBlink;
    private DateTime _nextFidget;
    private int _fidget;
    private static readonly string[] Fidgets = ["idle_look", "idle_leaf", "yawn", "wink"];
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
        _dot = new WpfShapes.Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = new Media.SolidColorBrush(Media.Color.FromRgb(47, 92, 214)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _berry.Children.Add(_dot);
        root.Children.Add(_berry);
        _sprite = new WpfControls.Image
        {
            Stretch = Media.Stretch.Fill,
            IsHitTestVisible = false,
        };
        Media.RenderOptions.SetBitmapScalingMode(_sprite, Media.BitmapScalingMode.NearestNeighbor);
        Media.RenderOptions.SetEdgeMode(_sprite, Media.EdgeMode.Aliased);
        _film = BerryFilm.TryLoad();
        if (_film != null)
        {
            _berry.Children.Clear();
            _berry.Children.Add(_sprite);
            _berry.Children.Add(_dot);
            _berry.Width = 192;
            _berry.Height = 216;
            _sprite.Width = 192;
            _sprite.Height = 216;
            Width = 230;
            Height = 260;
            _act = "idle";
            _nextBlink = DateTime.UtcNow.AddSeconds(16);
            _nextFidget = DateTime.UtcNow.AddSeconds(36);
            Play("idle_breathe");
        }
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
            if (_asleep) Wake();
            if (point == null) GoHome();
            else GoTo(Clamp(point.Value), _teleport && !_sleepTrip);
        });
    }

    public void Configure(string size, int speedPercent, bool teleport, string sleepCorner)
    {
        Dispatcher.Invoke(() =>
        {
            _size = size switch { "small" => 1.0 / 3, "medium" => 2.0 / 3, _ => 1 };
            _speed = speedPercent switch { 150 => 1.5, 200 => 2, _ => 1 };
            _teleport = teleport;
            _sleepCorner = sleepCorner;
            if (!_asleep) ApplySize();
            _target = Clamp(_target);
            _current = Clamp(_current);
        });
    }

    public void TalkForText() => Dispatcher.Invoke(() =>
    {
        _talking = true;
        _talkForAudio = false;
        _talkUntil = DateTime.UtcNow.AddSeconds(3);
    });

    public void TalkForAudio() => Dispatcher.Invoke(() =>
    {
        _talking = true;
        _talkForAudio = true;
        _talkUntil = DateTime.UtcNow.AddSeconds(30);
    });

    public void EndTalk() => Dispatcher.Invoke(() =>
    {
        _talking = false;
        _talkForAudio = false;
    });

    public void Sleep() => Dispatcher.Invoke(() =>
    {
        if (_asleep || _sleepTrip) return;
        _sleepTrip = true;
        GoTo(Clamp(SleepSpot()), allowTeleport: false);
    });

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
        if (_asleep)
        {
            Wake();
            e.Handled = true;
            return;
        }
        _dragging = true;
        if (_film != null) Play("drag");
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
        var parked = Clamp(new PointD(x, y));
        _current = _target = parked;
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
        var was = _dragging;
        _dragging = false;
        if (_berry.IsMouseCaptured) _berry.ReleaseMouseCapture();
    }

    public void PlayPose(string name) => Dispatcher.Invoke(() =>
    {
        if (_film == null || !_film.Clips.ContainsKey(name)) return;
        _pose = name;
        _posePlaying = true;
        _act = "pose";
        Play(name);
    });

    private string ActNow()
    {
        if (_dragging) return "drag";
        if (_traveling) return "walk";
        if (_posePlaying) return "pose";
        if (_thinking) return "think";
        if (_talking && (_talkForAudio || DateTime.UtcNow < _talkUntil)) return "talk";
        if (_talking) _talking = false;
        return "idle";
    }

    private string ClipFor(string act) => act switch
    {
        "drag" => "drag",
        "walk" => "walk",
        "pose" => _pose,
        "think" => "think",
        "talk" => "talk",
        _ => "idle_breathe",
    };

    private void AdvanceFilm(int ms)
    {
        var act = ActNow();
        if (act != _act)
        {
            _act = act;
            Play(ClipFor(act));
        }
        if (_clip == null || _clip.Frames.Length == 0) return;
        _frameTime += ms;
        if (_frameTime < _clip.Frames[_frame].Ms) return;
        _frameTime = 0;
        _frame++;
        if (_frame < _clip.Frames.Length)
        {
            ShowFrame();
            return;
        }
        _frame = 0;
        if (_act is "pose" or "blink" or "fidget")
        {
            if (_act == "pose") _posePlaying = false;
            _act = "";
            return;
        }
        if (_act == "idle")
        {
            var now = DateTime.UtcNow;
            if (now >= _nextBlink)
            {
                _nextBlink = now.AddSeconds(14 + (_fidget % 7));
                _act = "blink";
                Play("idle_blink");
                return;
            }
            if (now >= _nextFidget)
            {
                _nextFidget = now.AddSeconds(32 + (_fidget % 18));
                _act = "fidget";
                Play(Fidgets[_fidget++ % Fidgets.Length]);
                return;
            }
        }
        ShowFrame();
    }

    private void Play(string name)
    {
        if (_film == null || !_film.Clips.TryGetValue(name, out var clip)) return;
        _clip = clip;
        _frame = 0;
        _frameTime = 0;
        ShowFrame();
    }

    private void ShowFrame()
    {
        if (_clip == null || _clip.Frames.Length == 0) return;
        _sprite.Source = _clip.Frames[_frame].Image;
    }

    private void BerryMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        EndDrag();
        e.Handled = true;
        var menu = new BerryDotMenu();
        menu.HideChosen += () => SetUserHidden(true);
        menu.TalkChosen += () => TalkRequested?.Invoke();
        menu.SleepChosen += () => Sleep();
        var origin = _berry.PointToScreen(new System.Windows.Point(_berry.ActualWidth - 8, 8));
        var dpi = Media.VisualTreeHelper.GetDpi(this);
        menu.Left = origin.X / dpi.DpiScaleX;
        menu.Top = origin.Y / dpi.DpiScaleY;
        menu.Show();
    }

    private void GoHome() => GoTo(Clamp(HomeSpot()), allowTeleport: false);

    private void GoTo(PointD point, bool allowTeleport)
    {
        var dest = Clamp(point);
        var dx = dest.X - _current.X;
        var dy = dest.Y - _current.Y;
        var far = dx * dx + dy * dy > 36 * 36;
        if (allowTeleport && far && _current.X != 0)
        {
            _teleDest = dest;
            _telePhase = 1;
            _teleT = 0;
            _teleKind = Random.Shared.Next(10);
            _traveling = false;
            return;
        }
        _target = dest;
        _hasTarget = true;
        if (_current.X == 0 && _current.Y == 0) _current = _target;
    }

    private void ApplySize()
    {
        var w = 192 * _size;
        var h = 216 * _size;
        _berry.Width = w;
        _berry.Height = h;
        _sprite.Width = w;
        _sprite.Height = h;
        Width = w + 38;
        Height = h + 44;
    }

    private PointD Clamp(PointD p)
    {
        var dpi = Media.VisualTreeHelper.GetDpi(this);
        var probe = new System.Drawing.Point((int)p.X, (int)p.Y);
        var screen = Screen.FromPoint(probe).WorkingArea;
        var halfW = Width / 2 * dpi.DpiScaleX;
        var aboveFoot = (Height - 20) * dpi.DpiScaleY;
        var foot = 20 * dpi.DpiScaleY;
        var minX = screen.Left + halfW;
        var maxX = screen.Right - halfW;
        var minY = screen.Top + aboveFoot;
        var maxY = screen.Bottom - foot;
        var x = maxX <= minX ? screen.Left + screen.Width / 2.0 : Math.Clamp(p.X, minX, maxX);
        var y = maxY <= minY ? screen.Top + screen.Height / 2.0 : Math.Clamp(p.Y, minY, maxY);
        return new PointD(x, y);
    }

    private PointD HomeSpot()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
        return new PointD(screen.Left + screen.Width / 2.0, screen.Bottom - 40);
    }

    private PointD SleepSpot()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
        var x = _sleepCorner.EndsWith("left", StringComparison.Ordinal) ? screen.Left + 30 : screen.Right - 30;
        var y = _sleepCorner.StartsWith("top", StringComparison.Ordinal) ? screen.Top + 40 : screen.Bottom - 30;
        return new PointD(x, y);
    }

    private void ArriveSleep()
    {
        _sleepTrip = false;
        _asleep = true;
        _sprite.Visibility = Visibility.Collapsed;
        _dot.Visibility = Visibility.Visible;
        _berry.Width = 28;
        _berry.Height = 28;
        Width = 48;
        Height = 56;
        _current = Clamp(_current);
        _target = _current;
    }

    private void Wake()
    {
        if (!_asleep && !_sleepTrip) return;
        _asleep = false;
        _sleepTrip = false;
        _telePhase = 0;
        _sprite.Visibility = Visibility.Visible;
        _dot.Visibility = Visibility.Collapsed;
        _sprite.Opacity = 1;
        ApplySize();
        _current = Clamp(_current);
        _target = _current;
        PlayPose("wake");
    }

    private void ApplyTeleport(double gone, bool outbound)
    {
        _sprite.Opacity = 1;
        _scale.ScaleX = _scale.ScaleY = 1;
        _tilt.Angle = 0;
        _bob.X = 0;
        _bob.Y = 0;
        var u = Math.Clamp(gone, 0, 1);
        switch (_teleKind)
        {
            case 0:
                _scale.ScaleX = _scale.ScaleY = Math.Max(0.05, 1 - u);
                break;
            case 1:
                _sprite.Opacity = 1 - u;
                break;
            case 2:
                _bob.Y = 48 * u;
                _scale.ScaleY = Math.Max(0.05, 1 - 0.85 * u);
                break;
            case 3:
                _tilt.Angle = 360 * u;
                _scale.ScaleX = _scale.ScaleY = Math.Max(0.05, 1 - u);
                break;
            case 4:
                _scale.ScaleX = _scale.ScaleY = 1 + u * 1.4;
                _sprite.Opacity = 1 - u;
                break;
            case 5:
                _scale.ScaleX = 1 + 0.8 * u;
                _scale.ScaleY = Math.Max(0.05, 1 - 0.9 * u);
                break;
            case 6:
                _sprite.Opacity = ((int)(u * 10)) % 2 == 0 ? 1 : 0;
                break;
            case 7:
                _bob.Y = outbound ? 36 * u : -70 * u;
                _sprite.Opacity = 1 - u;
                break;
            case 8:
                _tilt.Angle = 900 * u;
                _bob.X = Math.Sin(u * 18) * 28;
                _scale.ScaleX = 1 + Math.Sin(u * 14) * 0.5;
                _scale.ScaleY = Math.Max(0.05, 1 - u);
                break;
            default:
                _bob.X = Math.Sin(u * 40) * 22 * u;
                _bob.Y = Math.Cos(u * 28) * 12 * u;
                _scale.ScaleX = 1 + Math.Sin(u * 30) * 0.7;
                _sprite.Opacity = u > 0.75 ? 0 : 1;
                break;
        }
    }

    private void Face(bool left)
    {
        if (_sprite.RenderTransform is not Media.ScaleTransform flip)
        {
            flip = new Media.ScaleTransform(1, 1);
            _sprite.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            _sprite.RenderTransform = flip;
        }
        var sign = left ? -1 : 1;
        if (Math.Abs(flip.ScaleX - sign) > 0.01) flip.ScaleX = sign;
    }

    private void Step()
    {
        if (_telePhase > 0)
        {
            _teleT += 0.016;
            var span = _telePhase == 1 ? 0.38 : 0.46;
            var gone = _telePhase == 1 ? _teleT / span : 1 - _teleT / span;
            ApplyTeleport(gone, _telePhase == 1);
            if (_teleT >= span)
            {
                if (_telePhase == 1)
                {
                    _current = _target = _teleDest;
                    _telePhase = 2;
                    _teleT = 0;
                    _teleKind = (_teleKind + 3) % 10;
                }
                else
                {
                    _telePhase = 0;
                    _sprite.Opacity = 1;
                    RestPose();
                }
            }
        }
        else if (_hasTarget && !_asleep)
        {
            var dx = _target.X - _current.X;
            var dy = _target.Y - _current.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            _traveling = dist > 22;
            if (dist > 1.5)
            {
                var step = Math.Min(dist, (_traveling ? 4.6 : 2.0) * _speed);
                _current = new PointD(_current.X + dx / dist * step, _current.Y + dy / dist * step);
                _current = Clamp(_current);
                if (_film != null && Math.Abs(dx) > 10) Face(dx < 0);
            }
            else
            {
                _current = _target;
                _traveling = false;
                if (_sleepTrip) ArriveSleep();
            }
        }
        if (_hasTarget || _asleep)
        {
            var dpi = Media.VisualTreeHelper.GetDpi(this);
            Left = _current.X / dpi.DpiScaleX - Width / 2;
            Top = _current.Y / dpi.DpiScaleY - Height + 20;
            if (_asleep)
            {
                _drift += 0.016;
                if (_drift > 4) _drift = 0;
                if (_drift < 0.7) Left += Math.Sin(_drift / 0.7 * Math.PI) * 3;
            }
        }
        if (_film == null) Think();
        else if (!_asleep) AdvanceFilm(16);
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
