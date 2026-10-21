using System.Drawing;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using Bluey.Protocol;

namespace Bluey.Host;

/// <summary>Watch and, after they allow it, use another computer that is running Bluey.</summary>
public sealed class RemoteHelpWindow : Form
{
    private readonly TextBox _address = new() { Left = 88, Top = 12, Width = 280 };
    private readonly TextBox _code = new() { Left = 430, Top = 12, Width = 90 };
    private readonly Button _connect = new() { Text = "Connect", Left = 530, Top = 10, Width = 90 };
    private readonly Button _disconnect = new() { Text = "Disconnect", Left = 628, Top = 10, Width = 100, Enabled = false };
    private readonly Label _status = new() { Left = 12, Top = 44, Width = 820, Height = 20, Text = "Enter their address and the code from their screen." };
    private readonly Button _talk = new() { Text = "Hold to talk", Left = 12, Top = 70, Width = 120 };
    private readonly Button _control = new() { Text = "Request control", Left = 140, Top = 70, Width = 130, Enabled = false };
    private readonly CheckBox _hear = new() { Text = "Hear their sound", Left = 284, Top = 74, Checked = true, AutoSize = true };
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly HelpLink _link = new();
    private readonly HelperMic _mic = new();
    private readonly PhysicalSpeaker _speakers = new();
    private bool _watching;
    private bool _steering;
    private bool _asking;
    private bool _talking;
    private bool _userLeft;
    private bool _connecting;
    private string? _resume;
    private string _host = "";
    private int _port = PhoneLink.PreferredPort;
    private Point _down;
    private bool _dragging;
    private DateTime _lastMove = DateTime.MinValue;

    public RemoteHelpWindow()
    {
        Text = "Help another computer";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 720);
        KeyPreview = true;
        var top = new Panel { Dock = DockStyle.Top, Height = 108 };
        top.Controls.Add(new Label { Text = "Address", Left = 12, Top = 16, AutoSize = true });
        top.Controls.Add(_address);
        top.Controls.Add(new Label { Text = "Code", Left = 380, Top = 16, AutoSize = true });
        top.Controls.Add(_code);
        top.Controls.Add(_connect);
        top.Controls.Add(_disconnect);
        top.Controls.Add(_status);
        top.Controls.Add(_talk);
        top.Controls.Add(_control);
        top.Controls.Add(_hear);
        Controls.Add(_picture);
        Controls.Add(top);
        _connect.Click += async (_, _) => await ConnectFromFormAsync();
        _disconnect.Click += (_, _) => LeaveSession();
        _talk.MouseDown += (_, _) => { _talk.Capture = true; StartTalk(); };
        _talk.MouseUp += (_, _) => { _talk.Capture = false; StopTalk(); };
        _control.Click += async (_, _) => await RequestControlAsync();
        _hear.CheckedChanged += async (_, _) => await HearChangedAsync();
        _picture.MouseDown += PictureDown;
        _picture.MouseMove += PictureMove;
        _picture.MouseUp += PictureUp;
        _picture.MouseWheel += PictureWheel;
        _mic.Pcm += OnMic;
        _link.Received += OnPacket;
        _link.Closed += OnLinkClosed;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        LeaveSession();
        _mic.Dispose();
        _speakers.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!CanSteer() || ActiveControl is TextBox) return;
        var name = KeyName(e.KeyCode);
        if (name == null) return;
        if (e.Control || e.Alt || name.Length > 1)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            var parts = new List<string>();
            if (e.Control) parts.Add("ctrl");
            if (e.Alt) parts.Add("alt");
            if (e.Shift) parts.Add("shift");
            parts.Add(name);
            SendInput("{\"action\":\"press\",\"keys\":" + JsonSerializer.Serialize(string.Join('+', parts)) + "}");
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!CanSteer() || ActiveControl is TextBox) return;
        if (char.IsControl(e.KeyChar)) return;
        e.Handled = true;
        SendInput("{\"action\":\"type\",\"text\":" + JsonSerializer.Serialize(e.KeyChar.ToString()) + "}");
    }

    private async Task ConnectFromFormAsync()
    {
        if (!TryParseAddress(_address.Text, out var host, out var port))
        {
            _status.Text = "Type the address as shown, including the numbers after the colon.";
            return;
        }
        _host = host;
        _port = port;
        _resume = null;
        _userLeft = false;
        await ConnectCoreAsync(resume: false);
    }

    private async Task ConnectCoreAsync(bool resume)
    {
        if (_connecting) return;
        _connecting = true;
        try
        {
            _connect.Enabled = false;
            _status.Text = resume ? "Reconnecting…" : "Connecting…";
            await _link.ConnectAsync(_host, _port, CancellationToken.None);
            var attempt = resume && !string.IsNullOrEmpty(_resume) ? "resume:" + _resume : _code.Text.Trim();
            var reply = await _link.RequestAsync("deskHelp", attempt, 10000);
            var result = reply.Text ?? "";
            if (result != "ok")
            {
                _link.Close();
                _resume = null;
                _status.Text = result switch
                {
                    "expired" => "That code has expired. Ask for a new one on their computer.",
                    "busy" => "Someone is already helping on that computer.",
                    _ => resume
                        ? "The help session ended. Ask for a new code."
                        : "That code did not work. Ask them to show it again.",
                };
                _connect.Enabled = true;
                ClearFrame();
                return;
            }
            await _link.RequestAsync("deskStart", "{\"fps\":10,\"quality\":95,\"scale\":1,\"grayscale\":false}", 10000);
            if (_hear.Checked)
                await _link.RequestAsync("deskAudioStart", "{\"rate\":24000,\"ch\":1,\"mutePc\":false}", 10000);
            _watching = true;
            _disconnect.Enabled = true;
            _control.Enabled = true;
            _status.Text = _steering
                ? "You can use the mouse while this window is in front."
                : "Watching. The other computer has the mouse.";
            _picture.Focus();
        }
        catch (Exception ex)
        {
            _link.Close();
            if (!resume) _resume = null;
            _status.Text = ex.Message;
            _connect.Enabled = !_watching;
            if (!resume) ClearFrame();
        }
        finally
        {
            _connecting = false;
        }
    }

    private void LeaveSession()
    {
        _userLeft = true;
        _resume = null;
        StopTalk();
        if (_link.Connected)
        {
            try
            {
                if (_watching) _link.Send("deskHelpLeave");
                _link.Send("deskStop");
            }
            catch { }
        }
        _watching = false;
        _steering = false;
        _asking = false;
        _link.Close();
        _disconnect.Enabled = false;
        _control.Enabled = false;
        _connect.Enabled = true;
        _status.Text = "Disconnected.";
        ClearFrame();
    }

    private void OnLinkClosed()
    {
        if (IsDisposed || _userLeft) return;
        BeginInvoke(new Action(() =>
        {
            _watching = false;
            StopTalk();
            ClearFrame();
            if (string.IsNullOrEmpty(_resume))
            {
                _status.Text = "The connection closed.";
                _connect.Enabled = true;
                _disconnect.Enabled = false;
                return;
            }
            _ = ReconnectAsync();
        }));
    }

    private async Task ReconnectAsync()
    {
        var until = DateTime.UtcNow.AddMinutes(2);
        while (!_userLeft && DateTime.UtcNow < until && !string.IsNullOrEmpty(_resume))
        {
            _status.Text = "Connection dropped. Reconnecting…";
            await Task.Delay(2000);
            if (_userLeft) return;
            await ConnectCoreAsync(resume: true);
            if (_watching) return;
        }
        if (!_userLeft)
            _status.Text = "The help session ended. Ask for a new code.";
    }

    private void OnPacket(Packet packet)
    {
        if (IsDisposed) return;
        var image = packet.Image;
        var text = packet.Text;
        var audio = packet.Audio;
        var command = packet.Command;
        BeginInvoke(new Action(() =>
        {
            if (command == "deskFrame" && _watching && !string.IsNullOrEmpty(image)) ShowFrame(image);
            else if (command == "deskAudio" && !string.IsNullOrEmpty(audio) && _hear.Checked && !_talking) PlayAudio(audio, text);
            else if (command == "deskHelpState") ApplyState(text ?? "");
        }));
    }

    private void ApplyState(string text)
    {
        if (text.StartsWith("resume:", StringComparison.OrdinalIgnoreCase))
        {
            _resume = text["resume:".Length..].Trim();
            return;
        }
        switch (text)
        {
            case "watch":
                _steering = false;
                _asking = false;
                _control.Enabled = true;
                _status.Text = "Watching. The other computer has the mouse.";
                break;
            case "control":
                _steering = true;
                _asking = false;
                _control.Enabled = false;
                _status.Text = "You can use the mouse while this window is in front.";
                _picture.Focus();
                break;
            case "denied":
                _asking = false;
                _control.Enabled = true;
                _status.Text = "They said no. You can keep watching.";
                break;
            case "ended":
                _userLeft = true;
                _resume = null;
                _watching = false;
                _steering = false;
                StopTalk();
                _link.Close();
                _connect.Enabled = true;
                _disconnect.Enabled = false;
                _control.Enabled = false;
                _status.Text = "They ended the help session.";
                ClearFrame();
                break;
        }
    }

    private async Task RequestControlAsync()
    {
        if (!_watching || _steering || _asking) return;
        _asking = true;
        _control.Enabled = false;
        _status.Text = "Waiting for them to press Allow.";
        try
        {
            var reply = await _link.RequestAsync("deskControl", null, 10000);
            if (reply.Text == "no")
            {
                _asking = false;
                _control.Enabled = true;
                _status.Text = "The computer did not take that request.";
            }
        }
        catch (Exception ex)
        {
            _asking = false;
            _control.Enabled = true;
            _status.Text = ex.Message;
        }
    }

    private async Task HearChangedAsync()
    {
        if (!_watching || !_link.Connected) return;
        try
        {
            if (_hear.Checked)
                await _link.RequestAsync("deskAudioStart", "{\"rate\":24000,\"ch\":1,\"mutePc\":false}", 10000);
            else
            {
                _speakers.Clear();
                _link.Send("deskAudioStop");
            }
        }
        catch { }
    }

    private void StartTalk()
    {
        if (!_watching || _talking) return;
        _talking = true;
        _speakers.Clear();
        _talk.Text = "Talking…";
        _mic.Start();
    }

    private void StopTalk()
    {
        if (!_talking) return;
        _talking = false;
        _mic.Stop();
        _talk.Text = "Hold to talk";
        if (_link.Connected)
        {
            try { _link.Send("deskTalkStop"); } catch { }
        }
    }

    private void OnMic(byte[] pcm)
    {
        if (!_talking || !_link.Connected || pcm.Length == 0) return;
        try { _link.Send("deskTalk", audio: Convert.ToBase64String(pcm)); }
        catch { }
    }

    private void ClearFrame()
    {
        var previous = _picture.Image;
        _picture.Image = null;
        previous?.Dispose();
    }

    private void ShowFrame(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            var copy = new Bitmap(image);
            var previous = _picture.Image;
            _picture.Image = copy;
            previous?.Dispose();
        }
        catch { }
    }

    private void PlayAudio(string base64, string? meta)
    {
        try
        {
            var pcm = Convert.FromBase64String(base64);
            var rate = 24000;
            var channels = 1;
            if (!string.IsNullOrEmpty(meta))
            {
                using var doc = JsonDocument.Parse(meta);
                if (doc.RootElement.TryGetProperty("rate", out var r) && r.TryGetInt32(out var rv)) rate = rv;
                if (doc.RootElement.TryGetProperty("ch", out var c) && c.TryGetInt32(out var cv)) channels = cv;
            }
            var samples = channels <= 1 ? Pcm16ToFloat(pcm) : Downmix(pcm, channels);
            _speakers.Enqueue(samples, rate);
        }
        catch { }
    }

    private bool CanSteer() => _watching && _steering && ContainsFocus && _link.Connected;

    private void PictureDown(object? sender, MouseEventArgs e)
    {
        _picture.Focus();
        if (!CanSteer() || !TryPicturePoint(e.Location, out var x, out var y)) return;
        _down = e.Location;
        _dragging = true;
        SendPoint("move", x, y);
    }

    private void PictureMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || !CanSteer() || (DateTime.UtcNow - _lastMove).TotalMilliseconds < 40) return;
        if (!TryPicturePoint(e.Location, out var x, out var y)) return;
        _lastMove = DateTime.UtcNow;
        SendPoint("move", x, y);
    }

    private void PictureUp(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (!CanSteer() || !TryPicturePoint(e.Location, out var x, out var y)) return;
        var moved = Math.Abs(e.X - _down.X) + Math.Abs(e.Y - _down.Y) > 6;
        if (moved) SendPoint("move", x, y);
        else SendPoint(e.Button == MouseButtons.Right ? "click" : "click", x, y, e.Button == MouseButtons.Right);
    }

    private void PictureWheel(object? sender, MouseEventArgs e)
    {
        if (!CanSteer() || !TryPicturePoint(e.Location, out var x, out var y)) return;
        var inv = CultureInfo.InvariantCulture;
        var dy = (-e.Delta / 120.0).ToString(inv);
        SendInput($"{{\"action\":\"scroll\",\"x\":{x.ToString(inv)},\"y\":{y.ToString(inv)},\"dx\":0,\"dy\":{dy}}}");
    }

    private void SendPoint(string action, float x, float y, bool right = false)
    {
        var inv = CultureInfo.InvariantCulture;
        var extra = right ? ",\"right\":true" : "";
        SendInput($"{{\"action\":\"{action}\",\"x\":{x.ToString(inv)},\"y\":{y.ToString(inv)}{extra}}}");
    }

    private void SendInput(string json)
    {
        if (!_link.Connected) return;
        try { _link.Send("deskInput", json); } catch { }
    }

    private bool TryPicturePoint(Point point, out float x, out float y)
    {
        x = 0;
        y = 0;
        var image = _picture.Image;
        if (image == null) return false;
        var boxW = (float)_picture.ClientSize.Width;
        var boxH = (float)_picture.ClientSize.Height;
        if (boxW < 1 || boxH < 1) return false;
        var scale = Math.Min(boxW / image.Width, boxH / image.Height);
        var drawW = image.Width * scale;
        var drawH = image.Height * scale;
        var offX = (boxW - drawW) / 2f;
        var offY = (boxH - drawH) / 2f;
        x = (point.X - offX) / drawW;
        y = (point.Y - offY) / drawH;
        return x >= 0 && y >= 0 && x <= 1 && y <= 1;
    }

    private static bool TryParseAddress(string text, out string host, out int port)
    {
        host = "";
        port = PhoneLink.PreferredPort;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        var colon = trimmed.LastIndexOf(':');
        if (colon > 0 && int.TryParse(trimmed[(colon + 1)..], out var parsed) && parsed is > 0 and <= 65535)
        {
            host = trimmed[..colon];
            port = parsed;
        }
        else host = trimmed;
        return host.Length > 0;
    }

    private static string? KeyName(Keys key)
    {
        if (key is >= Keys.A and <= Keys.Z) return key.ToString().ToLowerInvariant();
        if (key is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return ((char)('0' + (key - Keys.NumPad0))).ToString();
        if (key is >= Keys.F1 and <= Keys.F12) return "f" + ((int)key - (int)Keys.F1 + 1);
        return key switch
        {
            Keys.Enter => "enter",
            Keys.Tab => "tab",
            Keys.Escape => "escape",
            Keys.Space => "space",
            Keys.Back => "backspace",
            Keys.Delete => "del",
            Keys.Left => "left",
            Keys.Up => "up",
            Keys.Right => "right",
            Keys.Down => "down",
            Keys.Home => "home",
            Keys.End => "end",
            Keys.PageUp => "pageup",
            Keys.PageDown => "pagedown",
            _ => null,
        };
    }

    private static float[] Pcm16ToFloat(byte[] pcm)
    {
        var count = pcm.Length / 2;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        return samples;
    }

    private static float[] Downmix(byte[] pcm, int channels)
    {
        var frames = pcm.Length / (2 * channels);
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
                sum += BitConverter.ToInt16(pcm, (i * channels + c) * 2);
            samples[i] = sum / channels / 32768f;
        }
        return samples;
    }
}
