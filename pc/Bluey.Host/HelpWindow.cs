using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Bluey.Host;

/// <summary>
/// A small bar on the helped computer. It can be dragged and hidden
/// so it does not cover the work on screen.
/// </summary>
public sealed class HelpWindow : Form
{
    private enum Mode { Code, Watching, Controlling, Ask, Disconnected }

    private static readonly Color Ink = Color.FromArgb(28, 32, 52);
    private static readonly Color Muted = Color.FromArgb(176, 184, 214);
    private static readonly Color Paper = Color.FromArgb(244, 246, 255);
    private static readonly Color Accent = Color.FromArgb(108, 134, 245);
    private static readonly Color Danger = Color.FromArgb(196, 84, 96);
    private static readonly Color Ok = Color.FromArgb(54, 150, 112);

    private readonly Label _status;
    private readonly Label _detail;
    private readonly Label _code;
    private readonly Button _primary;
    private readonly Button _secondary;
    private readonly Button _extra;
    private readonly Button _fold;
    private Mode _mode = Mode.Code;
    private bool _collapsed;
    private bool _placed;
    private bool _dragging;
    private Point _dragScreen;
    private Action<bool>? _answer;
    private string _codeText = "";

    public event Action? StopRequested;
    public event Action? MouseBackRequested;
    public event Action? NewCodeRequested;

    public HelpWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Ink;
        ForeColor = Paper;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;
        _status = MakeLabel(Paper, 10.5f, FontStyle.Bold);
        _detail = MakeLabel(Muted, 9f, FontStyle.Regular);
        _code = MakeLabel(Paper, 22f, FontStyle.Bold);
        _primary = MakeButton(Accent);
        _secondary = MakeButton(Danger);
        _extra = MakeButton(Color.FromArgb(54, 60, 88));
        _fold = MakeButton(Color.FromArgb(54, 60, 88));
        _primary.Click += (_, _) => Primary();
        _secondary.Click += (_, _) => Secondary();
        _extra.Click += (_, _) => NewCodeRequested?.Invoke();
        _fold.Click += (_, _) => SetCollapsed(!_collapsed);
        _status.Click += (_, _) => { if (_collapsed) SetCollapsed(false); };
        Controls.AddRange(new Control[] { _status, _detail, _code, _primary, _secondary, _extra, _fold });
        foreach (var label in new Control[] { _status, _detail, _code, this })
        {
            label.MouseDown += BeginDrag;
            label.MouseMove += ContinueDrag;
            label.MouseUp += (_, _) => _dragging = false;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000;
            parameters.ExStyle |= 0x00000080;
            return parameters;
        }
    }

    public void ShowCode(string code)
    {
        _answer = null;
        _codeText = code;
        _mode = Mode.Code;
        _collapsed = false;
        Arrange();
        Show();
    }

    public void ShowWatching()
    {
        if (_answer != null) return;
        _mode = Mode.Watching;
        Arrange();
        Show();
    }

    public void ShowControlling()
    {
        _answer = null;
        _mode = Mode.Controlling;
        _collapsed = false;
        Arrange();
        Show();
    }

    public void ShowDisconnected(bool canReconnect = false)
    {
        _answer = null;
        _mode = Mode.Disconnected;
        _detail.Tag = canReconnect;
        _collapsed = false;
        Arrange();
        Show();
    }

    public void ShowAsk(Action<bool> answer)
    {
        _answer = answer;
        _mode = Mode.Ask;
        _collapsed = false;
        Arrange();
        Show();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        FinishAsk(false);
        Hide();
        StopRequested?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = Round(bounds, 14);
        using var pen = new Pen(Color.FromArgb(90, 108, 134, 245), 1.6f);
        e.Graphics.DrawPath(pen, path);
    }

    private void Primary()
    {
        if (_mode == Mode.Ask) FinishAsk(true);
        else if (_mode == Mode.Controlling) MouseBackRequested?.Invoke();
        else StopRequested?.Invoke();
    }

    private void Secondary()
    {
        if (_mode == Mode.Ask) FinishAsk(false);
        else StopRequested?.Invoke();
    }

    private void FinishAsk(bool allow)
    {
        var answer = _answer;
        _answer = null;
        answer?.Invoke(allow);
    }

    private void SetCollapsed(bool collapsed)
    {
        _collapsed = collapsed;
        Arrange();
    }

    private void Arrange()
    {
        SuspendLayout();
        var reconnect = _detail.Tag as bool? == true;
        _status.Text = _collapsed ? Pill() : Status();
        _detail.Text = _collapsed ? "" : Detail(reconnect);
        _code.Text = _mode == Mode.Code && !_collapsed ? _codeText : "";
        _code.Visible = _code.Text.Length > 0;
        _detail.Visible = _detail.Text.Length > 0;
        _primary.Text = PrimaryText();
        _secondary.Text = SecondaryText();
        _extra.Text = "New code";
        _fold.Text = _collapsed ? "Show" : "Hide";
        _primary.Visible = !_collapsed && _primary.Text.Length > 0;
        _secondary.Visible = !_collapsed && _secondary.Text.Length > 0;
        _extra.Visible = !_collapsed && _mode is Mode.Code or Mode.Watching or Mode.Disconnected;
        var width = _collapsed ? 280 : 560;
        var height = _collapsed ? 40 : _mode == Mode.Code ? 128 : _detail.Visible ? 78 : 58;
        ClientSize = new Size(width, height);
        if (!_placed)
        {
            var screen = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(screen.Left + Math.Max(12, (screen.Width - Width) / 2), screen.Top + 16);
            _placed = true;
        }
        var right = ClientSize.Width - 12;
        Place(_fold, ref right);
        if (_secondary.Visible) Place(_secondary, ref right);
        if (_primary.Visible) Place(_primary, ref right);
        if (_extra.Visible) Place(_extra, ref right);
        var textWidth = Math.Max(90, right - 20);
        _status.SetBounds(16, _collapsed ? 9 : 8, _collapsed ? 190 : textWidth, 22);
        _code.SetBounds(16, 36, textWidth, 34);
        _detail.SetBounds(16, _mode == Mode.Code ? 74 : 32, textWidth, 34);
        ClampToScreen();
        ApplyShape();
        ResumeLayout();
        Invalidate();
    }

    private string Status() => _mode switch
    {
        Mode.Code => "Tell the helper this code",
        Mode.Watching => "A helper is watching",
        Mode.Controlling => "A helper has the mouse",
        Mode.Ask => "Allow the mouse and keyboard?",
        _ => "The helper is gone",
    };

    private string Pill() => _mode switch
    {
        Mode.Code => "Code " + _codeText,
        Mode.Watching => "Helper is watching",
        Mode.Controlling => "Helper has the mouse",
        Mode.Ask => "Allow the mouse?",
        _ => "Helper left",
    };

    private string Detail(bool reconnect) => _mode switch
    {
        Mode.Code => "It works once, for 10 minutes.",
        Mode.Watching => "They can talk. They cannot click yet.",
        Mode.Controlling => "Press Ctrl+Alt+H to take the mouse back.",
        Mode.Ask => "Allow this only if you asked them for help.",
        Mode.Disconnected when reconnect => "They can reconnect for 2 minutes.",
        _ => "The help session has ended.",
    };

    private string PrimaryText() => _mode switch
    {
        Mode.Controlling => "Take the mouse back",
        Mode.Ask => "Allow",
        Mode.Code or Mode.Disconnected => "",
        _ => "",
    };

    private string SecondaryText() => _mode switch
    {
        Mode.Ask => "No",
        Mode.Code => "Cancel",
        Mode.Disconnected => "Close",
        _ => "Stop help",
    };

    private void Place(Button button, ref int right)
    {
        var width = Math.Max(64, TextRenderer.MeasureText(button.Text, button.Font).Width + 22);
        right -= width + 8;
        button.SetBounds(right, _collapsed ? 4 : 10, width, 28);
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is Button) return;
        _dragging = true;
        _dragScreen = ((Control)sender!).PointToScreen(e.Location);
    }

    private void ContinueDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging || sender is not Control control) return;
        var now = control.PointToScreen(e.Location);
        Location = new Point(Location.X + now.X - _dragScreen.X, Location.Y + now.Y - _dragScreen.Y);
        _dragScreen = now;
        ClampToScreen();
    }

    private void ClampToScreen()
    {
        var area = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).WorkingArea;
        var x = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        var y = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(x, y);
    }

    private void ApplyShape()
    {
        using var path = Round(new Rectangle(0, 0, Width, Height), 16);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static Label MakeLabel(Color color, float size, FontStyle style) => new()
    {
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", size, style),
        AutoSize = false,
    };

    private static Button MakeButton(Color back)
    {
        var button = new Button
        {
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.2f);
        return button;
    }

    private static GraphicsPath Round(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
