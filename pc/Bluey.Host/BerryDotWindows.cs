using System.Windows;
using System.Windows.Input;
using Media = System.Windows.Media;
using WpfControls = System.Windows.Controls;

namespace Bluey.Host;

public sealed class BerryDotMenu : Window
{
    private bool _chose;

    public event Action? HideChosen;
    public event Action? TalkChosen;
    public event Action? SleepChosen;
    public event Action? NoTouchChosen;
    public event Action? LearnChosen;
    public event Action? CommandsChosen;

    public BerryDotMenu(bool noTouch, bool learn, bool commands)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(Item("Hide", () => { _chose = true; HideChosen?.Invoke(); Close(); }));
        stack.Children.Add(Item("Talk", () => { _chose = true; TalkChosen?.Invoke(); Close(); }));
        stack.Children.Add(Item("Go to sleep", () => { _chose = true; SleepChosen?.Invoke(); Close(); }));
        stack.Children.Add(Item(noTouch ? "No touch: on" : "No touch: off", () => { _chose = true; NoTouchChosen?.Invoke(); Close(); }));
        stack.Children.Add(Item(learn ? "Learn: on" : "Learn: off", () => { _chose = true; LearnChosen?.Invoke(); Close(); }));
        stack.Children.Add(Item(commands ? "Commands: on" : "Commands: off", () => { _chose = true; CommandsChosen?.Invoke(); Close(); }));
        Content = new WpfControls.Border
        {
            Background = new Media.SolidColorBrush(Media.Color.FromRgb(28, 32, 52)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = stack,
        };
        Deactivated += (_, _) => { if (!_chose) Close(); };
    }

    private static WpfControls.Button Item(string text, Action click)
    {
        var button = new WpfControls.Button
        {
            Content = text,
            Width = 168,
            Height = 36,
            Margin = new Thickness(2),
            Background = Media.Brushes.Transparent,
            Foreground = Media.Brushes.White,
            BorderThickness = new Thickness(0),
            FontFamily = new Media.FontFamily("Segoe UI"),
            FontSize = 14,
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(12, 0, 12, 0),
        };
        button.Click += (_, _) => click();
        return button;
    }
}

public sealed class BerryTalkWindow : Window
{
    private readonly WpfControls.TextBox _box;

    public event Action<string>? Submit;

    public BerryTalkWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Width = 320;
        MinHeight = 120;
        SizeToContent = SizeToContent.Height;
        var title = new WpfControls.TextBlock
        {
            Text = "Talk to Bluey",
            Foreground = Media.Brushes.White,
            FontFamily = new Media.FontFamily("Segoe UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 8),
        };
        title.MouseLeftButtonDown += TitleDown;
        _box = new WpfControls.TextBox
        {
            MinHeight = 36,
            Padding = new Thickness(8),
            FontFamily = new Media.FontFamily("Segoe UI"),
            FontSize = 14,
            Background = new Media.SolidColorBrush(Media.Color.FromRgb(18, 20, 34)),
            Foreground = Media.Brushes.White,
            BorderThickness = new Thickness(0),
            CaretBrush = Media.Brushes.White,
        };
        _box.KeyDown += BoxKey;
        var hint = new WpfControls.TextBlock
        {
            Text = "Enter sends. Double-click closes.",
            Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(176, 184, 214)),
            FontFamily = new Media.FontFamily("Segoe UI"),
            FontSize = 11,
            Margin = new Thickness(4, 8, 0, 0),
        };
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(_box);
        stack.Children.Add(hint);
        Content = new WpfControls.Border
        {
            Background = new Media.SolidColorBrush(Media.Color.FromRgb(28, 32, 52)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            Child = stack,
        };
    }

    public void FocusInput()
    {
        _box.Focus();
        _box.SelectAll();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private void TitleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Close();
            return;
        }
        try { DragMove(); } catch { }
    }

    private void BoxKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = _box.Text.Trim();
        if (text.Length == 0) return;
        _box.Clear();
        Submit?.Invoke(text);
    }
}
