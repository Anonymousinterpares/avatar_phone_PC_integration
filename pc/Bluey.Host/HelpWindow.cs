using System.Drawing;
using System.Windows.Forms;

namespace Bluey.Host;

/// <summary>The on-screen help card. The person at the computer uses this. The helper cannot click Allow before control is granted.</summary>
public sealed class HelpWindow : Form
{
    private readonly Label _body;
    private readonly Label _code;
    private readonly Label _hotkey;
    private readonly Button _mouse;
    private readonly Button _fresh;
    private readonly Button _stop;

    public event Action? StopRequested;
    public event Action? MouseBackRequested;
    public event Action? NewCodeRequested;

    public HelpWindow()
    {
        Text = "Bluey help";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        TopMost = true;
        ClientSize = new Size(440, 250);
        _body = new Label { Left = 20, Top = 16, Width = 400, Height = 72 };
        _code = new Label
        {
            Left = 20,
            Top = 92,
            Width = 400,
            Height = 48,
            Font = new Font(FontFamily.GenericSansSerif, 28, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _hotkey = new Label { Left = 20, Top = 148, Width = 400, Height = 24, Text = "Press Ctrl+Alt+H to take the mouse back." };
        _fresh = new Button { Text = "New code", Left = 20, Top = 190, Width = 110 };
        _mouse = new Button { Text = "Take the mouse back", Left = 140, Top = 190, Width = 150 };
        _stop = new Button { Text = "Stop help", Left = 300, Top = 190, Width = 110 };
        _fresh.Click += (_, _) => NewCodeRequested?.Invoke();
        _mouse.Click += (_, _) => MouseBackRequested?.Invoke();
        _stop.Click += (_, _) => StopRequested?.Invoke();
        Controls.Add(_body);
        Controls.Add(_code);
        Controls.Add(_hotkey);
        Controls.Add(_fresh);
        Controls.Add(_mouse);
        Controls.Add(_stop);
    }

    public void ShowCode(string code)
    {
        _body.Text = "Tell this code to the helper. It works once, and only for 10 minutes.";
        _code.Text = code;
        _code.Visible = true;
        _hotkey.Visible = false;
        _mouse.Visible = false;
        _fresh.Visible = true;
        _stop.Text = "Cancel";
        Show();
        Activate();
    }

    public void ShowWatching()
    {
        _body.Text = "A helper is watching this screen. They can talk through the speakers. They cannot use the mouse until you allow it.";
        _code.Visible = false;
        _hotkey.Visible = false;
        _mouse.Visible = false;
        _fresh.Visible = true;
        _stop.Text = "Stop help";
        Show();
    }

    public void ShowControlling()
    {
        _body.Text = "The helper can use the mouse and keyboard for 10 minutes. You can take it back at any time.";
        _code.Visible = false;
        _hotkey.Visible = true;
        _mouse.Visible = true;
        _fresh.Visible = false;
        _stop.Text = "Stop help";
        Show();
        Activate();
    }

    public void ShowDisconnected(bool canReconnect = false)
    {
        _body.Text = canReconnect
            ? "The helper disconnected. They can reconnect for 2 minutes without a new code."
            : "The helper left.";
        _code.Visible = false;
        _hotkey.Visible = false;
        _mouse.Visible = false;
        _fresh.Visible = true;
        _stop.Text = "Close";
        Show();
    }

    public static bool Ask()
    {
        using var form = new Form
        {
            Text = "Bluey help",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            TopMost = true,
            ClientSize = new Size(440, 160),
        };
        var label = new Label
        {
            Left = 16,
            Top = 16,
            Width = 408,
            Height = 72,
            Text = "A helper wants to use the mouse and keyboard on this computer.\n\nAllow this only if you asked them for help.",
        };
        var allow = new Button { Text = "Allow", DialogResult = DialogResult.OK, Left = 240, Top = 104, Width = 80 };
        var no = new Button { Text = "No", DialogResult = DialogResult.Cancel, Left = 332, Top = 104, Width = 80 };
        form.Controls.Add(label);
        form.Controls.Add(allow);
        form.Controls.Add(no);
        form.AcceptButton = allow;
        form.CancelButton = no;
        return form.ShowDialog() == DialogResult.OK;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        Hide();
        StopRequested?.Invoke();
    }
}
