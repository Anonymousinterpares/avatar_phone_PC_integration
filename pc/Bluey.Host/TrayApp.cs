using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Bluey.Auth;
using Bluey.Brains;
using Bluey.Protocol;
using Bluey.Session;
using Bluey.Speech;

namespace Bluey.Host;

public sealed class TrayApp : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly BrainClient _brains;
    private readonly CharacterSession _session;
    private readonly PhoneLink _phones;
    private readonly CursorOverlay _overlay;
    private readonly UsageHintsWindow _hints;
    private readonly SpeechConfigStore _speech;
    private readonly SupertonicSpeaker _speaker;
    private readonly NotifyIcon _icon;
    private readonly Icon _iconImage;
    private readonly ToolStripMenuItem _modeItem;

    private readonly Action _allowHelp;
    private readonly Action _helpSomeone;
    private ToolStripMenuItem? _hiddenItem;
    private BerryTalkWindow? _talkWindow;

    public TrayApp(AppSettings settings, SettingsStore store, BrainClient brains, CharacterSession session, PhoneLink phones, CursorOverlay overlay, UsageHintsWindow hints, SpeechConfigStore speech, SupertonicSpeaker speaker, Action allowHelp, Action helpSomeone)
    {
        _settings = settings;
        _store = store;
        _brains = brains;
        _session = session;
        _phones = phones;
        _overlay = overlay;
        _hints = hints;
        _speech = speech;
        _speaker = speaker;
        _allowHelp = allowHelp;
        _helpSomeone = helpSomeone;
        _modeItem = new ToolStripMenuItem("Mode") { Enabled = false };
        _iconImage = BlueberryIcon();
        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Visible = true,
            Text = Trim("Bluey  " + session.DescribeMode()),
        };
        _speaker.Failed += message =>
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
                _icon.ShowBalloonTip(5000, "Bluey speech", message, ToolTipIcon.Warning)));
        };
        _icon.ContextMenuStrip = BuildMenu();
        _icon.DoubleClick += (_, _) => ShowSettings(ask: false);
        _overlay.UserHiddenChanged += hidden =>
        {
            if (_hiddenItem != null && _hiddenItem.Checked != hidden)
                _hiddenItem.Checked = hidden;
        };
        _overlay.TalkRequested += ShowBerryTalk;
    }

    public void Refresh()
    {
        _icon.Text = Trim("Bluey  " + _session.DescribeMode());
        _modeItem.Text = _session.DescribeMode();
    }

    public void Dispose()
    {
        _icon.Dispose();
        _iconImage.Dispose();
    }

    private static Icon BlueberryIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var canvas = Graphics.FromImage(bitmap))
        {
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.Clear(Color.Transparent);
            using var body = new LinearGradientBrush(new Rectangle(0, 2, 32, 30), Color.FromArgb(169, 188, 255), Color.FromArgb(43, 47, 143), 70f);
            canvas.FillEllipse(body, 1, 6, 30, 25);
            using var navy = new SolidBrush(Color.FromArgb(28, 31, 102));
            canvas.FillPolygon(navy, Star(16f, 5.2f, 4.4f));
            canvas.FillEllipse(Brushes.White, 6, 13, 8, 9);
            canvas.FillEllipse(Brushes.White, 18, 13, 8, 9);
            canvas.FillEllipse(navy, 8f, 16f, 4.2f, 4.2f);
            canvas.FillEllipse(navy, 20f, 16f, 4.2f, 4.2f);
            canvas.FillEllipse(Brushes.White, 8.4f, 16.2f, 1.7f, 1.7f);
            canvas.FillEllipse(Brushes.White, 20.4f, 16.2f, 1.7f, 1.7f);
            canvas.FillEllipse(navy, 13.5f, 23.5f, 5f, 3.2f);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var raw = Icon.FromHandle(handle);
            return (Icon)raw.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static PointF[] Star(float cx, float cy, float outer)
    {
        var inner = outer * 0.42f;
        var points = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            points[i] = new PointF(cx + (float)(Math.Cos(angle) * radius), cy + (float)(Math.Sin(angle) * radius));
        }
        return points;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_modeItem);
        menu.Items.Add("Sign in and keys…", null, (_, _) => ShowSettings(ask: false));
        menu.Items.Add("Continue with ChatGPT", null, async (_, _) => await SignInAsync(returning: _settings.ChatGpt?.ClientId.Length > 0));
        menu.Items.Add("Sign out of ChatGPT", null, async (_, _) => await SignOutAsync());
        menu.Items.Add(new ToolStripSeparator());
        var control = new ToolStripMenuItem("Let him use the computer") { Checked = _settings.ComputerControl, CheckOnClick = true };
        control.CheckedChanged += (_, _) =>
        {
            _settings.ComputerControl = control.Checked;
            _store.Save(_settings);
            _ = _phones.BroadcastAsync(Packet.Of("session", _session.SessionJson()));
        };
        menu.Items.Add(control);
        var hints = new ToolStripMenuItem("Show usage hints") { Checked = _settings.UsageHints, CheckOnClick = true };
        hints.CheckedChanged += (_, _) =>
        {
            _settings.UsageHints = hints.Checked;
            _store.Save(_settings);
            _hints.SetEnabled(hints.Checked);
        };
        menu.Items.Add(hints);
        var speak = new ToolStripMenuItem("Speak replies") { Checked = _speech.Current.TextToSpeech, CheckOnClick = true };
        speak.CheckedChanged += (_, _) =>
        {
            _speech.Current.TextToSpeech = speak.Checked;
            _speech.Save();
            if (speak.Checked) _speaker.Warmup();
            else _speaker.Stop();
        };
        menu.Items.Add(speak);
        var male = new ToolStripMenuItem("Male") { Checked = _speech.Current.Sex == "male" };
        var female = new ToolStripMenuItem("Female") { Checked = _speech.Current.Sex == "female" };
        male.Click += (_, _) => ChooseSex("male", male, female);
        female.Click += (_, _) => ChooseSex("female", male, female);
        var sex = new ToolStripMenuItem("Voice sex");
        sex.DropDownItems.Add(male);
        sex.DropDownItems.Add(female);
        menu.Items.Add(sex);
        var ttsHealth = new ToolStripMenuItem("TTS: " + _speaker.StatusText) { Enabled = false };
        var ttsTest = new ToolStripMenuItem("Test TTS voice (speak sample)", null, (_, _) =>
        {
            _speaker.SpeakTest("Testing Supertonic 3 text to speech. Everything is working.");
        });
        menu.Items.Add(ttsHealth);
        menu.Items.Add(ttsTest);
        _speaker.StatusChanged += status =>
        {
            var app = System.Windows.Application.Current;
            app?.Dispatcher.BeginInvoke(new Action(() =>
            {
                ttsHealth.Text = "TTS: " + status;
            }));
        };
        menu.Opening += (_, _) =>
        {
            speak.Checked = _speech.Current.TextToSpeech;
            ttsHealth.Text = "TTS: " + _speaker.StatusText;
            ttsTest.Enabled = _speaker.IsReady;
        };
        menu.Items.Add("Avatar…", null, (_, _) => ShowAvatar());
        menu.Items.Add("Send him home", null, (_, _) => _overlay.PointAt(null));
        var hidden = new ToolStripMenuItem("Hide the blueberry") { CheckOnClick = true };
        hidden.CheckedChanged += (_, _) => _overlay.SetUserHidden(hidden.Checked);
        _hiddenItem = hidden;
        menu.Items.Add(hidden);
        menu.Items.Add("Phone on the left", null, (_, _) => Seat("left"));
        menu.Items.Add("Phone in the center", null, (_, _) => Seat("center"));
        menu.Items.Add("Phone on the right", null, (_, _) => Seat("right"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Type an ask…", null, (_, _) => ShowSettings(ask: true));
        menu.Items.Add("Hand the ask to Claude Code", null, async (_, _) => await HandoffAsync("claude"));
        menu.Items.Add("Hand the ask to Cursor", null, async (_, _) => await HandoffAsync("cursor"));
        menu.Items.Add("Allow help…", null, (_, _) => _allowHelp());
        menu.Items.Add("Help another computer…", null, (_, _) => _helpSomeone());
        menu.Items.Add("Set desktop password…", null, (_, _) =>
        {
            var current = _store.Load().DesktopPassword;
            using var form = new Form
            {
                Text = "Desktop Password",
                Width = 380,
                Height = 160,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
            };
            var hint = new Label { Text = "Password for remote desktop (leave empty to disable):", Left = 12, Top = 12, AutoSize = true };
            var box = new TextBox { Left = 12, Top = 34, Width = 340, Text = current, UseSystemPasswordChar = true };
            var showCheck = new CheckBox { Text = "Show characters", Left = 12, Top = 64, AutoSize = true };
            showCheck.CheckedChanged += (_, _) => box.UseSystemPasswordChar = !showCheck.Checked;
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 272, Top = 86 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 192, Top = 86 };
            form.Controls.Add(hint);
            form.Controls.Add(box);
            form.Controls.Add(showCheck);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            if (form.ShowDialog() != DialogResult.OK) return;
            var s = _store.Load();
            s.DesktopPassword = box.Text.Trim();
            _store.Save(s);
            MessageBox.Show(string.IsNullOrEmpty(s.DesktopPassword) ? "Desktop password removed." : "Desktop password updated.", "Bluey", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        menu.Items.Add("Show connection address", null, (_, _) =>
            MessageBox.Show("On the phone, Bluey finds this PC by itself.\nIf it does not, type this address:\n\n" + _phones.AddressText,
                "Bluey", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    private void ChooseSex(string sex, ToolStripMenuItem male, ToolStripMenuItem female)
    {
        _speech.Current.Sex = SpeechConfig.NormalizeSex(sex);
        _speech.Save();
        male.Checked = _speech.Current.Sex == "male";
        female.Checked = _speech.Current.Sex == "female";
    }

    private void Seat(string seat)
    {
        _settings.PhoneSeat = seat;
        _store.Save(_settings);
    }

    private void ShowAvatar()
    {
        using var form = new AvatarForm(_settings);
        if (form.ShowDialog() != DialogResult.OK) return;
        _store.Save(_settings);
        _overlay.Configure(_settings.BerrySize, _settings.BerrySpeed, _settings.BerryTeleport, _settings.BerrySleepCorner);
    }

    private void ShowBerryTalk()
    {
        if (_talkWindow == null)
        {
            _talkWindow = new BerryTalkWindow();
            _talkWindow.Submit += text =>
                _ = _session.AskAsync(text, _phones.BroadcastAsync, CancellationToken.None);
            _talkWindow.Closed += (_, _) => _talkWindow = null;
        }
        if (!_talkWindow.IsVisible)
        {
            _talkWindow.Left = _overlay.Left + 150;
            _talkWindow.Top = _overlay.Top;
        }
        _talkWindow.Show();
        _talkWindow.Activate();
        _talkWindow.FocusInput();
    }

    private void ShowSettings(bool ask)
    {
        using var form = new SettingsForm(_settings, ask);
        if (form.ShowDialog() != DialogResult.OK) return;
        _store.Save(_settings);
        Refresh();
        _ = _phones.BroadcastAsync(Packet.Of("session", _session.SessionJson()));
        if (ask && !string.IsNullOrWhiteSpace(form.AskText))
            _ = _session.AskAsync(form.AskText, _phones.BroadcastAsync, CancellationToken.None);
    }

    private async Task HandoffAsync(string target)
    {
        var prompt = Prompt("What should " + target + " do?");
        if (string.IsNullOrWhiteSpace(prompt)) return;
        var text = await AgentHandoff.RunAsync(target, prompt, CancellationToken.None);
        _session.ShowCaption(text);
        MessageBox.Show(text, "Bluey", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task SignInAsync(bool returning)
    {
        var port = FreePort();
        var start = ChatGptOAuth.Begin(_settings, port, returning);
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Process.Start(new ProcessStartInfo(start.Url) { UseShellExecute = true });
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var buffer = new byte[16 * 1024];
        var read = await stream.ReadAsync(buffer);
        var request = Encoding.ASCII.GetString(buffer, 0, read);
        var page = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\n\r\n<!doctype html><body style=\"font-family:sans-serif\">Bluey is signed in. You can close this tab.</body>";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(page));
        listener.Stop();
        var path = request.Split(' ').ElementAtOrDefault(1) ?? "";
        var query = path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : "";
        var fields = ParseQuery(query);
        if (!fields.TryGetValue("state", out var state) || state != start.Pkce.State)
            throw new InvalidOperationException("ChatGPT sign-in state did not match.");
        if (fields.TryGetValue("error", out var error))
            throw new InvalidOperationException("ChatGPT sign-in was cancelled (" + error + ").");
        if (!fields.TryGetValue("code", out var code))
            throw new InvalidOperationException("ChatGPT sign-in did not return a code.");
        fields.TryGetValue("client_id", out var issued);
        using var http = new HttpClient();
        var jwks = await ChatGptOAuth.FetchJwksAsync(http, CancellationToken.None);
        var account = await ChatGptOAuth.ExchangeAsync(http, start, code, issued, jwks, CancellationToken.None);
        _settings.ChatGpt = account;
        _settings.Mode = BrainMode.ChatGptPlan;
        _store.Save(_settings);
        Refresh();
        var plan = account.CanUsePlan ? "Plan usage is on." : "Signed in, but plan usage was not granted. Sign in again and allow it.";
        MessageBox.Show(plan + "\n" + account.Email, "Bluey", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task SignOutAsync()
    {
        var account = _settings.ChatGpt;
        _settings.ChatGpt = account == null ? null : new ChatGptAccount
        {
            Email = account.Email,
            Subject = account.Subject,
            ClientId = account.ClientId,
            Issuer = account.Issuer,
        };
        _store.Save(_settings);
        Refresh();
        if (account == null || string.IsNullOrEmpty(account.RefreshToken)) return;
        try
        {
            using var http = new HttpClient();
            using var discovery = await http.GetAsync("https://auth.openai.com/.well-known/openid-configuration");
            using var doc = System.Text.Json.JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
            var revoke = doc.RootElement.GetProperty("revocation_endpoint").GetString();
            if (revoke == null) return;
            await http.PostAsync(revoke, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = account.RefreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = account.ClientId,
            }));
        }
        catch (Exception)
        {
            MessageBox.Show("Local sign-out is done. Remote revocation did not confirm. You can disconnect Bluey PC in ChatGPT settings.", "Bluey");
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var fields = new Dictionary<string, string>();
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.IndexOf('=');
            if (split <= 0) continue;
            fields[Uri.UnescapeDataString(part[..split])] = Uri.UnescapeDataString(part[(split + 1)..]);
        }
        return fields;
    }

    private static string? Prompt(string title)
    {
        using var form = new Form { Text = title, Width = 440, Height = 150, StartPosition = FormStartPosition.CenterScreen };
        var box = new TextBox { Left = 12, Top = 12, Width = 400 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 312, Top = 48 };
        form.Controls.Add(box);
        form.Controls.Add(ok);
        form.AcceptButton = ok;
        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }

    private static string Trim(string text) => text.Length > 63 ? text[..63] : text;
}

public sealed class SettingsForm : Form
{
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly TextBox _xai = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly TextBox _openai = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly TextBox _anthropic = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly TextBox _cerebras = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly TextBox _openRouter = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, DropDownWidth = 340 };
    private readonly ComboBox _route = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox _search = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private bool _searchSync;
    private bool _searchTouched;
    private readonly TextBox _ask = new() { Width = 280 };

    public string AskText => _ask.Text;

    public SettingsForm(AppSettings settings, bool focusAsk)
    {
        Text = "Bluey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new System.Drawing.Size(460, focusAsk ? 640 : 580);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var providers = new object[] { "Grok live (xAI API key)", "OpenAI live (API key)", "ChatGPT plan", "Claude API key", "Cerebras (qwen-3.8-27b)", "OpenRouter" };
        _mode.Items.AddRange(providers);
        _search.Items.AddRange(providers);
        _mode.SelectedIndex = settings.Mode switch
        {
            BrainMode.OpenAiLive => 1,
            BrainMode.ChatGptPlan => 2,
            BrainMode.Anthropic => 3,
            BrainMode.Cerebras => 4,
            BrainMode.OpenRouter => 5,
            _ => 0,
        };
        _xai.Text = settings.XaiApiKey;
        _openai.Text = settings.OpenAiApiKey;
        _anthropic.Text = settings.AnthropicApiKey;
        _cerebras.Text = settings.CerebrasApiKey;
        _openRouter.Text = settings.OpenRouterApiKey;
        var selectedModel = OpenRouterModels.Normalize(settings.OpenRouterModel);
        for (var i = 0; i < OpenRouterModels.Choices.Length; i++)
        {
            _model.Items.Add(OpenRouterModels.Choices[i].Label);
            if (OpenRouterModels.Choices[i].Id == selectedModel) _model.SelectedIndex = i;
        }
        if (_model.SelectedIndex < 0) _model.SelectedIndex = 0;
        var selectedRoute = OpenRouterProviders.Normalize(settings.OpenRouterProvider);
        for (var i = 0; i < OpenRouterProviders.Choices.Length; i++)
        {
            _route.Items.Add(OpenRouterProviders.Choices[i].Label);
            if (OpenRouterProviders.Choices[i].Slug == selectedRoute) _route.SelectedIndex = i;
        }
        if (_route.SelectedIndex < 0) _route.SelectedIndex = 0;
        _searchTouched = WebSearchPreference.IsPinned();
        _search.SelectedIndex = WebSearchPreference.Index(_searchTouched ? WebSearchPreference.Resolve(settings.Mode) : settings.Mode);
        _model.SelectedIndexChanged += (_, _) => UpdateProviderLock();
        _mode.SelectedIndexChanged += (_, _) =>
        {
            UpdateProviderLock();
            if (_searchTouched) return;
            _searchSync = true;
            _search.SelectedIndex = _mode.SelectedIndex;
            _searchSync = false;
        };
        _search.SelectedIndexChanged += (_, _) =>
        {
            if (!_searchSync) _searchTouched = true;
        };
        UpdateProviderLock();
        Add(layout, "Mode", _mode);
        Add(layout, "Web search", _search);
        Add(layout, "xAI API key", _xai);
        Add(layout, "OpenAI API key", _openai);
        Add(layout, "Anthropic API key", _anthropic);
        Add(layout, "Cerebras key", _cerebras);
        Add(layout, "OpenRouter key", _openRouter);
        Add(layout, "OpenRouter model", _model);
        Add(layout, "Qwen provider", _route);
        if (focusAsk) Add(layout, "Ask", _ask);
        var chat = new Label
        {
            AutoSize = true,
            Text = settings.ChatGpt?.Email is { Length: > 0 } email
                ? "ChatGPT: " + email
                : "ChatGPT: not signed in. Use Continue with ChatGPT in the tray.",
        };
        layout.Controls.Add(chat, 1, layout.RowCount);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var save = new Button { Text = focusAsk ? "Ask" : "Save", DialogResult = DialogResult.OK };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(layout);
        Controls.Add(buttons);
        save.Click += (_, _) =>
        {
            settings.Mode = _mode.SelectedIndex switch
            {
                1 => BrainMode.OpenAiLive,
                2 => BrainMode.ChatGptPlan,
                3 => BrainMode.Anthropic,
                4 => BrainMode.Cerebras,
                5 => BrainMode.OpenRouter,
                _ => BrainMode.XaiLive,
            };
            settings.OpenRouterApiKey = _openRouter.Text.Trim();
            settings.OpenRouterModel = OpenRouterModels.Choices[Math.Max(0, _model.SelectedIndex)].Id;
            settings.OpenRouterProvider = OpenRouterProviders.Choices[Math.Max(0, _route.SelectedIndex)].Slug;
            settings.XaiApiKey = _xai.Text.Trim();
            settings.OpenAiApiKey = _openai.Text.Trim();
            settings.AnthropicApiKey = _anthropic.Text.Trim();
            settings.CerebrasApiKey = _cerebras.Text.Trim();
            var search = WebSearchPreference.FromIndex(_search.SelectedIndex);
            WebSearchPreference.Save(_searchTouched, search);
        };
    }

    private void UpdateProviderLock()
    {
        var index = _model.SelectedIndex;
        _route.Enabled = index >= 0 && index < OpenRouterModels.Choices.Length && OpenRouterModels.Choices[index].PinProvider;
    }

    private static void Add(TableLayoutPanel layout, string label, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
