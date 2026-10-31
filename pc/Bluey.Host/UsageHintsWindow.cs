using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WpfPoint = System.Windows.Point;
using Media = System.Windows.Media;
using WpfControls = System.Windows.Controls;

namespace Bluey.Host;

/// <summary>
/// Slides in from the right edge of the monitor when the pointer is within 10px of that edge.
/// </summary>
public sealed class UsageHintsWindow : Window
{
    private readonly DispatcherTimer _timer;
    private double _slide = 1;
    private bool _enabled = true;
    private System.Drawing.Rectangle _anchor;

    public UsageHintsWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Media.Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Width = 380;
        Height = 640;
        Content = Build();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Slide();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, style | 0x00000080);
        };
        Loaded += (_, _) => _timer.Start();
    }

    public void SetEnabled(bool enabled)
    {
        Dispatcher.Invoke(() =>
        {
            _enabled = enabled;
            if (!enabled)
            {
                _slide = 1;
                Opacity = 0;
                IsHitTestVisible = false;
            }
        });
    }

    private void Slide()
    {
        GetCursorPos(out var cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var bounds = screen.Bounds;
        var near = cursor.X >= bounds.Right - 10 && cursor.X <= bounds.Right + 24
            && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;
        var over = PointerInside();
        if (near) _anchor = bounds;
        if (_anchor.Width == 0) _anchor = bounds;
        var show = _enabled && (near || (over && _slide < 0.98));
        var target = show ? 0d : 1d;
        _slide += (target - _slide) * 0.2;
        if (Math.Abs(_slide - target) < 0.004) _slide = target;

        var dpi = Media.VisualTreeHelper.GetDpi(this);
        Height = Math.Max(280, Math.Min(680, _anchor.Height / dpi.DpiScaleY - 48));
        var widthPx = ActualWidth * dpi.DpiScaleX;
        var heightPx = Height * dpi.DpiScaleY;
        var shownX = _anchor.Right - widthPx - 12 * dpi.DpiScaleX;
        var hiddenX = _anchor.Right;
        var x = shownX + (hiddenX - shownX) * _slide;
        var y = _anchor.Top + (_anchor.Height - heightPx) / 2;
        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
        Opacity = _enabled ? 1 - _slide : 0;
        IsHitTestVisible = _enabled && _slide < 0.85;
    }

    private bool PointerInside()
    {
        if (!_enabled || !IsVisible || ActualWidth <= 0) return false;
        GetCursorPos(out var cursor);
        var dpi = Media.VisualTreeHelper.GetDpi(this);
        var origin = PointToScreen(new WpfPoint(0, 0));
        var width = ActualWidth * dpi.DpiScaleX;
        var height = ActualHeight * dpi.DpiScaleY;
        return cursor.X >= origin.X && cursor.X <= origin.X + width
            && cursor.Y >= origin.Y && cursor.Y <= origin.Y + height;
    }

    private static WpfControls.Border Build()
    {
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(HeadingTitle("What Bluey can do"));
        stack.Children.Add(Note("Move the mouse to the right edge of the screen. Scroll this list."));
        foreach (var (heading, lines) in Guide)
        {
            stack.Children.Add(Heading(heading));
            foreach (var line in lines) stack.Children.Add(Item(line));
        }
        var scroll = new WpfControls.ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = WpfControls.ScrollBarVisibility.Disabled,
            Padding = new Thickness(4, 0, 8, 0),
        };
        return new WpfControls.Border
        {
            Background = new Media.SolidColorBrush(Media.Color.FromArgb(236, 16, 18, 32)),
            BorderBrush = new Media.SolidColorBrush(Media.Color.FromRgb(108, 134, 245)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 14, 10, 14),
            Child = scroll,
        };
    }

    private static WpfControls.TextBlock HeadingTitle(string text) => new()
    {
        Text = text,
        Foreground = Media.Brushes.White,
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static WpfControls.TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(185, 178, 204)),
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
    };

    private static WpfControls.TextBlock Heading(string text) => new()
    {
        Text = text,
        Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(169, 188, 255)),
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 10, 0, 4),
    };

    private static WpfControls.TextBlock Item(string text) => new()
    {
        Text = text,
        Foreground = Media.Brushes.White,
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static readonly (string Heading, string[] Lines)[] Guide =
    {
        ("Talk", new[]
        {
            "Double-tap his face to wake or sleep. Ctrl+Alt+Space does the same from the PC.",
            "Hold his face, or the Hold button, and speak. Let go to send.",
            "Tap Manual typing on the phone when you cannot speak, type, and tap Send.",
            "The answer stays for about 12 seconds, on the phone and next to him on the PC.",
        }),
        ("Point", new[]
        {
            "Ask “what’s this?” with the mouse on the thing you mean. He looks, then flies over to it.",
            "Ask “what does this button mean?” or “read this number.”",
            "Say “stop pointing” or “go home.” Sleeping him also sends him to the bottom of the screen.",
            "Ctrl+Alt+S stops him, stops a program he started, and sends him home.",
        }),
        ("Look things up", new[]
        {
            "“Look up the next public holiday.”",
            "“What’s the weather in Warsaw?” He uses the web when a fact might have changed.",
        }),
        ("Use the computer", new[]
        {
            "Turn on “Let him use the computer” in the tray first. Until then he can look and point, and he will not click.",
            "No touch, on the tray or the phone, lets him describe the screen only. He will not move the mouse, click, or type.",
            "“Open Notepad.” “Open Chrome.” “Open github.com.”",
            "“Click Save.” “Double-click that file.” “Right-click this.”",
            "“Type hello and press enter.” “Press ctrl+t.” “Scroll down.” “Drag this onto the desktop.”",
            "Before sending, deleting, buying, or closing unsaved work, he says what he is about to do and waits.",
            "He will not type a password, and he will not press Win+L or Ctrl+Alt+Delete.",
        }),
        ("Commands", new[]
        {
            "Let him run commands is off until you turn it on. It is separate from Let him use the computer.",
            "Pick Command folder… first. He asks before each new program, on the PC and on the phone.",
            "Run once is only this time. Always allow saves that exact program, those arguments, and that folder.",
            "He will not run a shell. No answer counts as no. One yes does not become a habit.",
        }),
        ("The tray", new[]
        {
            "Sign in and keys… picks Cerebras, OpenRouter, Grok, ChatGPT, or Claude. OpenRouter model picks Qwen 3.8 27B or a :nitro model. The host pin applies only to Qwen 3.8 27B.",
            "Phone on the left, center, or right is where his eyes rest.",
            "Send him home parks the blueberry at the bottom of the screen.",
            "Hide the blueberry takes him off the screen without quitting. Drag the dot to move him. Right-click it for Hide and Talk.",
            "Show usage hints is this panel.",
            "Speak replies reads aloud only the line he wraps for speech. Voice sex is Male or Female. Both are saved in bluey.config.json.",
            "Show connection address is what to type on the phone if it cannot find the PC.",
            "Quit closes Bluey.",
        }),
    };

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point point);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
