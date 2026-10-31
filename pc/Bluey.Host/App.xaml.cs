using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Bluey.Auth;
using Bluey.Brains;
using Bluey.Commands;
using Bluey.Desk;
using Bluey.Memory;
using Bluey.Protocol;
using Bluey.Session;
using Bluey.Speech;

namespace Bluey.Host;

public partial class App : System.Windows.Application
{
    private PhoneLink? _phones;
    private TrayApp? _tray;
    private CursorOverlay? _overlay;
    private CharacterSession? _session;
    private MemoryStore? _memory;
    private CommandStore? _commandStore;
    private CommandPrompt? _commandPrompt;
    private SupertonicSpeaker? _speaker;
    private DesktopStreamer? _streamer;
    private AudioStreamer? _audioStreamer;
    private readonly HelpSession _help = new();
    private readonly HelpSpeaker _helpSpeaker = new();
    private HelpWindow? _helpWindow;
    private bool _stoppingHelp;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "Bluey");
            args.Handled = true;
        };
        var store = new SettingsStore();
        var settings = store.Load();
        var speech = SpeechConfigStore.Open();
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var brains = new BrainClient(http, store);
        _overlay = new CursorOverlay();
        _overlay.Configure(settings.BerrySize, settings.BerrySpeed, settings.BerryTeleport, settings.BerrySleepCorner, settings.BerryBubble);
        _overlay.Show();
        var desk = new WindowsDesk(() => store.Load().ComputerControl, visible => _overlay.SetVisible(visible));
        _speaker = new SupertonicSpeaker(speech);
        _memory = new MemoryStore();
        _memory.Open();
        _commandStore = new CommandStore();
        _session = new CharacterSession(brains, desk, _speaker, _memory, _commandStore);
        _session.AllowLearning = () => !_help.IsHelper(PhoneLink.CurrentClientId);
        _session.AllowCommands = () => !_help.IsHelper(PhoneLink.CurrentClientId);
        brains.LiveMemoryNote = () =>
        {
            var current = store.Load();
            if (!current.Learn || !current.LearnTell || current.LearnQuiet) return null;
            if (_help.IsHelper(PhoneLink.CurrentClientId)) return null;
            var book = _memory.Load();
            if (_memory.TooNew || _memory.Unreadable) return null;
            return UserMemory.SessionBrief(book);
        };
        _overlay.BindSpeechClock(() => _speaker.HeardSeconds());
        _speaker.PaceReady += marks => _overlay.FollowSpeech(marks);
        _session.CaptionChanged += (text, voiced) =>
        {
            _overlay.SetCaption(text, voiced);
            if (string.IsNullOrWhiteSpace(text)) _overlay.EndTalk();
            else if (speech.Current.TextToSpeech)
            {
                _overlay.HoldCaption();
                _overlay.TalkForAudio();
            }
            else
            {
                _overlay.ReleaseCaption();
                _overlay.TalkForText();
            }
        };
        _session.OutputsIdle += () => _overlay.ReleaseCaption();
        _speaker.PlaybackFinished += () =>
        {
            _overlay.EndTalk();
            _overlay.ReleaseCaption();
        };
        _speaker.Failed += message =>
        {
            SpeechLog.Write("failed: " + message);
            _overlay.EndTalk();
            _overlay.ReleaseCaption();
        };
        _session.SpeechGaveUp += message =>
        {
            SpeechLog.Write(message);
            _overlay.EndTalk();
            _overlay.ReleaseCaption();
        };
        _session.PointChanged += point => _overlay.PointAt(point);
        _session.ActivityChanged += busy => _overlay.SetThinking(busy);
        _session.PoseRequested += name => _overlay.PlayPose(name);
        _streamer = new DesktopStreamer();
        _audioStreamer = new AudioStreamer();
        Bluey.Audio.AudioDeviceManager.EnsurePhysicalDefault();
        _phones = new PhoneLink(async (packet, push, token) =>
        {
            switch (packet.Command)
            {
                case "deskAuth":
                {
                    var currentSettings = store.Load();
                    var password = currentSettings.DesktopPassword;
                    var attempt = packet.Text ?? "";
                    if (string.IsNullOrEmpty(password) || attempt == password)
                    {
                        _help.MarkOwner(PhoneLink.CurrentClientId);
                        return Packet.Of("deskAuth", "ok", packet.CallId);
                    }
                    return Packet.Of("deskAuth", "fail", packet.CallId);
                }
                case "deskHelp":
                {
                    var helperId = PhoneLink.CurrentClientId;
                    var attempt = packet.Text ?? "";
                    var joined = attempt.StartsWith("resume:", StringComparison.OrdinalIgnoreCase)
                        ? _help.TryResume(helperId, attempt["resume:".Length..])
                        : _help.TryJoin(helperId, attempt);
                    if (joined == HelpJoin.Ok)
                    {
                        var controlling = _help.CanControl(helperId);
                        var resumeToken = _help.ResumeToken(helperId);
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (controlling) HelpWindowFor().ShowControlling();
                            else HelpWindowFor().ShowWatching();
                        }));
                        if (!string.IsNullOrEmpty(resumeToken) && _phones != null)
                            _ = _phones.SendToAsync(helperId, Packet.Of("deskHelpState", "resume:" + resumeToken));
                        if (controlling && _phones != null)
                            _ = _phones.SendToAsync(helperId, Packet.Of("deskHelpState", "control"));
                        return Packet.Of("deskHelp", "ok", packet.CallId);
                    }
                    var text = joined switch
                    {
                        HelpJoin.Expired => "expired",
                        HelpJoin.Busy => "busy",
                        _ => "fail",
                    };
                    return Packet.Of("deskHelp", text, packet.CallId);
                }
                case "deskControl":
                {
                    var asker = PhoneLink.CurrentClientId;
                    if (!_help.BeginAsk(asker))
                        return Packet.Of("deskControl", "no", packet.CallId);
                    _ = Dispatcher.BeginInvoke(new Action(() => FinishControlAsk(asker)));
                    return Packet.Of("deskControl", "asking", packet.CallId);
                }
                case "deskTalk":
                {
                    if (!_help.IsHelper(PhoneLink.CurrentClientId) || string.IsNullOrEmpty(packet.Audio)) return null;
                    try { _helpSpeaker.Play(Convert.FromBase64String(packet.Audio)); }
                    catch { }
                    return null;
                }
                case "deskTalkStop":
                    if (_help.IsHelper(PhoneLink.CurrentClientId)) _helpSpeaker.Reset();
                    return null;
                case "deskHelpLeave":
                    if (_help.Remove(PhoneLink.CurrentClientId))
                    {
                        _helpSpeaker.Reset();
                        _ = Dispatcher.BeginInvoke(new Action(() => _helpWindow?.ShowDisconnected()));
                    }
                    return Packet.Of("deskHelpLeft", null, packet.CallId);
                case "deskStart":
                {
                    var viewer = PhoneLink.CurrentClientId;
                    if (!_help.IsHelper(viewer) && !_help.CanControl(viewer))
                        return Packet.Of("deskStart", "denied", packet.CallId);
                    var fps = 10;
                    var quality = 60;
                    var scale = 0.5;
                    var grayscale = false;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(packet.Text ?? "{}");
                        var root = doc.RootElement;
                        if (root.TryGetProperty("fps", out var f) && f.TryGetInt32(out var fv)) fps = fv;
                        if (root.TryGetProperty("quality", out var q) && q.TryGetInt32(out var qv)) quality = qv;
                        if (root.TryGetProperty("scale", out var s) && s.TryGetDouble(out var sv)) scale = sv;
                        if (root.TryGetProperty("grayscale", out var g) && g.ValueKind == System.Text.Json.JsonValueKind.True) grayscale = true;
                    }
                    catch { }
                    _streamer!.Start(push, fps, quality, scale, grayscale);
                    return Packet.Of("deskStarted", null, packet.CallId);
                }
                case "deskSettings":
                {
                    var fps = 10;
                    var quality = 60;
                    var scale = 0.5;
                    var grayscale = false;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(packet.Text ?? "{}");
                        var root = doc.RootElement;
                        if (root.TryGetProperty("fps", out var f) && f.TryGetInt32(out var fv)) fps = fv;
                        if (root.TryGetProperty("quality", out var q) && q.TryGetInt32(out var qv)) quality = qv;
                        if (root.TryGetProperty("scale", out var s) && s.TryGetDouble(out var sv)) scale = sv;
                        if (root.TryGetProperty("grayscale", out var g) && g.ValueKind == System.Text.Json.JsonValueKind.True) grayscale = true;
                    }
                    catch { }
                    _streamer!.UpdateSettings(fps, quality, scale, grayscale);
                    return null;
                }
                case "deskStop":
                    if (_help.Remove(PhoneLink.CurrentClientId))
                    {
                        _helpSpeaker.Reset();
                        _ = Dispatcher.BeginInvoke(new Action(() => _helpWindow?.ShowDisconnected()));
                    }
                    _streamer!.UpdateCrop(0, 0, 1.0, 1.0);
                    if (!_help.HasAnyone()) _streamer.Stop();
                    return Packet.Of("deskStopped", null, packet.CallId);
                case "deskAudioStart":
                {
                    var rate = 24000;
                    var ch = 1;
                    var mutePc = false;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(packet.Text ?? "{}");
                        var root = doc.RootElement;
                        if (root.TryGetProperty("rate", out var r) && r.TryGetInt32(out var rv)) rate = rv;
                        if (root.TryGetProperty("ch", out var c) && c.TryGetInt32(out var cv)) ch = cv;
                        if (root.TryGetProperty("mutePc", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.True) mutePc = true;
                    }
                    catch { }
                    _audioStreamer!.Start(push, rate, ch, mutePc);
                    return Packet.Of("deskAudioStarted", null, packet.CallId);
                }
                case "deskAudioSettings":
                {
                    var rate = 24000;
                    var ch = 1;
                    var mutePc = false;
                    var enabled = true;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(packet.Text ?? "{}");
                        var root = doc.RootElement;
                        if (root.TryGetProperty("enabled", out var en) && en.ValueKind == System.Text.Json.JsonValueKind.False) enabled = false;
                        if (root.TryGetProperty("rate", out var r) && r.TryGetInt32(out var rv)) rate = rv;
                        if (root.TryGetProperty("ch", out var c) && c.TryGetInt32(out var cv)) ch = cv;
                        if (root.TryGetProperty("mutePc", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.True) mutePc = true;
                    }
                    catch { }
                    if (enabled)
                    {
                        if (!_audioStreamer!.Running)
                            _audioStreamer.Start(push, rate, ch, mutePc);
                        else
                            _audioStreamer.UpdateSettings(push, rate, ch, mutePc);
                    }
                    else
                    {
                        _audioStreamer!.Stop();
                    }
                    return null;
                }
                case "deskAudioStop":
                    _audioStreamer?.Stop();
                    return Packet.Of("deskAudioStopped", null, packet.CallId);
                case "deskCrop":
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(packet.Text ?? "{}");
                        var root = doc.RootElement;
                        var cx = root.GetProperty("cropX").GetDouble();
                        var cy = root.GetProperty("cropY").GetDouble();
                        var cw = root.GetProperty("cropW").GetDouble();
                        var ch = root.GetProperty("cropH").GetDouble();
                        var maxW = 0;
                        var maxH = 0;
                        if (root.TryGetProperty("maxW", out var mw) && mw.TryGetInt32(out var mwi)) maxW = mwi;
                        if (root.TryGetProperty("maxH", out var mh) && mh.TryGetInt32(out var mhi)) maxH = mhi;
                        _streamer?.UpdateCrop(cx, cy, cw, ch, maxW, maxH);
                    }
                    catch { }
                    return null;
                }
                case "deskInput":
                {
                    if (!_help.CanControl(PhoneLink.CurrentClientId)) return null;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(packet.Text ?? "{}");
                        var root = doc.RootElement;
                        var action = root.GetProperty("action").GetString() ?? "";
                        var screenBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Windows.Forms.Screen.AllScreens[0].Bounds;
                        switch (action)
                        {
                            case "click":
                            {
                                var nx = root.GetProperty("x").GetDouble();
                                var ny = root.GetProperty("y").GetDouble();
                                var px = screenBounds.Left + nx * screenBounds.Width;
                                var py = screenBounds.Top + ny * screenBounds.Height;
                                var right = root.TryGetProperty("right", out var rv) && rv.ValueKind == System.Text.Json.JsonValueKind.True;
                                var pt = new Bluey.Desk.PointD(px, py);
                                desk.Warp(pt);
                                await desk.ClickAsync(pt, right, 1, token, restore: false);
                                desk.Warp(pt);
                                break;
                            }
                            case "doubleClick":
                            {
                                var nx = root.GetProperty("x").GetDouble();
                                var ny = root.GetProperty("y").GetDouble();
                                var px = screenBounds.Left + nx * screenBounds.Width;
                                var py = screenBounds.Top + ny * screenBounds.Height;
                                var pt = new Bluey.Desk.PointD(px, py);
                                desk.Warp(pt);
                                await desk.ClickAsync(pt, false, 2, token, restore: false);
                                desk.Warp(pt);
                                break;
                            }
                            case "scroll":
                            {
                                var nx = root.GetProperty("x").GetDouble();
                                var ny = root.GetProperty("y").GetDouble();
                                var px = screenBounds.Left + nx * screenBounds.Width;
                                var py = screenBounds.Top + ny * screenBounds.Height;
                                var dx = root.TryGetProperty("dx", out var dxv) ? (int)(dxv.GetDouble() * 120) : 0;
                                var dy = root.TryGetProperty("dy", out var dyv) ? (int)(dyv.GetDouble() * 120) : 0;
                                var pt = new Bluey.Desk.PointD(px, py);
                                desk.Warp(pt);
                                await desk.ScrollAsync(pt, dx, dy, token, restore: false);
                                desk.Warp(pt);
                                break;
                            }
                            case "type":
                            {
                                var text = root.GetProperty("text").GetString() ?? "";
                                await desk.TypeAsync(text, _ => Task.CompletedTask, token);
                                break;
                            }
                            case "press":
                            {
                                var keys = root.GetProperty("keys").GetString() ?? "";
                                await desk.PressAsync(keys, token);
                                break;
                            }
                            case "drag":
                            {
                                var fx = root.GetProperty("fromX").GetDouble();
                                var fy = root.GetProperty("fromY").GetDouble();
                                var tx = root.GetProperty("toX").GetDouble();
                                var ty = root.GetProperty("toY").GetDouble();
                                var from = new Bluey.Desk.PointD(screenBounds.Left + fx * screenBounds.Width, screenBounds.Top + fy * screenBounds.Height);
                                var to = new Bluey.Desk.PointD(screenBounds.Left + tx * screenBounds.Width, screenBounds.Top + ty * screenBounds.Height);
                                await desk.DragAsync(from, to, token, restore: false);
                                desk.Warp(to);
                                break;
                            }
                            case "move":
                            {
                                var nx = root.GetProperty("x").GetDouble();
                                var ny = root.GetProperty("y").GetDouble();
                                desk.Warp(new Bluey.Desk.PointD(screenBounds.Left + nx * screenBounds.Width, screenBounds.Top + ny * screenBounds.Height));
                                break;
                            }
                        }
                    }
                    catch { }
                    return null;
                }
                case "ttsGetStatus":
                    return new Packet { Command = "ttsStatus", Text = GetTtsStatusJson(), CallId = packet.CallId };
                case "ttsTest":
                    _speaker!.SpeakTest(packet.Text);
                    return new Packet { Command = "ttsStatus", Text = GetTtsStatusJson(), CallId = packet.CallId };
                case "ttsToggle":
                    speech.Current.TextToSpeech = !speech.Current.TextToSpeech;
                    speech.Save();
                    if (speech.Current.TextToSpeech) _speaker!.Warmup();
                    else _speaker!.Stop();
                    BroadcastTtsStatus();
                    return new Packet { Command = "ttsStatus", Text = GetTtsStatusJson(), CallId = packet.CallId };
                case "noTouch":
                {
                    var enabled = packet.Text is "true" or "1";
                    settings.NoTouch = enabled;
                    store.Save(settings);
                    var json = _session!.SessionJson();
                    _ = Dispatcher.BeginInvoke(() => _tray?.ShowNoTouch(enabled));
                    _ = _phones!.BroadcastAsync(Packet.Of("session", json));
                    return Packet.Of("session", json, packet.CallId);
                }
                case "commands":
                {
                    var enabled = packet.Text is "true" or "1";
                    settings.Commands = enabled;
                    store.Save(settings);
                    var json = _session!.SessionJson();
                    _ = Dispatcher.BeginInvoke(() => _tray?.ShowCommands(enabled));
                    _ = _phones!.BroadcastAsync(Packet.Of("session", json));
                    return Packet.Of("session", json, packet.CallId);
                }
                case "memoryGet":
                case "memoryFlags":
                case "memoryUse":
                case "memoryForget":
                case "memoryBackup":
                case "memoryRestore":
                    return MemoryCommand(packet, settings, store);
                case "ttsSet":
                    if (packet.Text == "true" || packet.Text == "1") speech.Current.TextToSpeech = true;
                    else if (packet.Text == "false" || packet.Text == "0") speech.Current.TextToSpeech = false;
                    speech.Save();
                    if (speech.Current.TextToSpeech) _speaker!.Warmup();
                    else _speaker!.Stop();
                    BroadcastTtsStatus();
                    return new Packet { Command = "ttsStatus", Text = GetTtsStatusJson(), CallId = packet.CallId };
                default:
                    return await _session!.HandleAsync(packet, push, token);
            }
        });
        var prompt = new CommandPrompt(_phones);
        _commandPrompt = prompt;
        _session.ApproveCommand = prompt.AskAsync;
        _phones.TryHandleImmediately = packet =>
        {
            if (packet.Command != "commandAnswer") return false;
            if (!_help.IsHelper(PhoneLink.CurrentClientId)) prompt.Offer(packet.Text);
            return true;
        };
        _phones.OnClientsCleared = () =>
        {
            _streamer?.Stop();
            _audioStreamer?.Stop();
        };
        _phones.OnClientClosed = clientId =>
        {
            if (!_help.Park(clientId)) return;
            _helpSpeaker.Reset();
            _ = Dispatcher.BeginInvoke(new Action(() => _helpWindow?.ShowDisconnected(canReconnect: true)));
        };
        _phones.Start();
        var hints = new UsageHintsWindow();
        hints.Show();
        hints.SetEnabled(settings.UsageHints);
        _tray = new TrayApp(settings, store, brains, _session, _phones, _overlay, hints, speech, _speaker, _memory, _commandStore, ShowHelpCode, ShowRemoteHelp);
        var helpSweep = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        helpSweep.Tick += (_, _) => SweepHelpControl();
        helpSweep.Start();
        _speaker.StatusChanged += _ => BroadcastTtsStatus();
        if (speech.Current.TextToSpeech) _speaker.Warmup();
        var gaze = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        gaze.Tick += (_, _) => PumpFace(store);
        gaze.Start();
        _overlay.SourceInitialized += (_, _) => InstallHotkeys();
        if (new WindowInteropHelper(_overlay).Handle != IntPtr.Zero) InstallHotkeys();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _helpSpeaker.Dispose();
        _helpWindow?.Dispose();
        _audioStreamer?.Dispose();
        _streamer?.Dispose();
        _phones?.Dispose();
        _speaker?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private Packet MemoryCommand(Packet packet, AppSettings settings, SettingsStore store)
    {
        if (_memory == null || _session == null) return Packet.Of("memory", "{\"error\":\"no\"}", packet.CallId);
        if (_help.IsHelper(PhoneLink.CurrentClientId)) return Packet.Of("memory", "{\"error\":\"no\"}", packet.CallId);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(packet.Text) ? "{}" : packet.Text);
            var root = doc.RootElement;
            string? note = null;
            switch (packet.Command)
            {
                case "memoryFlags":
                    if (root.TryGetProperty("learn", out var learn)) settings.Learn = learn.ValueKind == System.Text.Json.JsonValueKind.True;
                    if (root.TryGetProperty("tell", out var tell)) settings.LearnTell = tell.ValueKind == System.Text.Json.JsonValueKind.True;
                    if (root.TryGetProperty("quiet", out var quiet)) settings.LearnQuiet = quiet.ValueKind == System.Text.Json.JsonValueKind.True;
                    store.Save(settings);
                    var session = _session.SessionJson();
                    _ = Dispatcher.BeginInvoke(() => _tray?.ShowLearn(settings.Learn, settings.LearnTell, settings.LearnQuiet));
                    _ = _phones!.BroadcastAsync(Packet.Of("session", session));
                    return Packet.Of("session", session, packet.CallId);
                case "memoryUse":
                {
                    var book = _memory.Load();
                    if (UserMemory.SetUse(book, root.GetProperty("id").GetString() ?? "", root.GetProperty("use").GetString() ?? ""))
                        _memory.Save(book);
                    break;
                }
                case "memoryForget":
                    if (root.TryGetProperty("all", out var all) && all.ValueKind == System.Text.Json.JsonValueKind.True)
                    {
                        _memory.ForgetEverything();
                        note = "Forgotten. A copy is in the backups.";
                    }
                    else
                    {
                        var book = _memory.Load();
                        if (UserMemory.ForgetId(book, root.GetProperty("id").GetString() ?? "")) _memory.Save(book);
                    }
                    break;
                case "memoryBackup":
                {
                    var path = _memory.Backup();
                    note = path == null ? "There is nothing to save yet." : "Saved a copy on this PC.";
                    break;
                }
                case "memoryRestore":
                    note = _memory.Restore(root.GetProperty("name").GetString() ?? "", root.TryGetProperty("add", out var add) && add.ValueKind == System.Text.Json.JsonValueKind.True);
                    break;
            }
            var snapshot = _memory.Snapshot(settings.Learn, settings.LearnTell, settings.LearnQuiet, note);
            _ = _phones!.BroadcastAsync(Packet.Of("memory", snapshot));
            return Packet.Of("memory", snapshot, packet.CallId);
        }
        catch (Exception ex)
        {
            return Packet.Of("memory", System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }), packet.CallId);
        }
    }

    private void PumpFace(SettingsStore store)
    {
        if (_phones == null || _session == null) return;
        var settings = store.Load();
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Windows.Forms.Screen.AllScreens[0].Bounds;
        var seat = settings.PhoneSeat switch
        {
            "left" => 0.18,
            "right" => 0.82,
            _ => 0.5,
        };
        var homeX = screen.Left + screen.Width * seat;
        var homeY = screen.Bottom - 40;
        GetCursorPos(out var cursor);
        var gazeX = Math.Clamp((cursor.X - homeX) / (screen.Width * 0.45), -1, 1);
        var gazeY = Math.Clamp((cursor.Y - homeY) / (screen.Height * 0.45), -1, 1);
        var mood = _session.Awake ? Mood.Listening : Mood.Resting;
        _ = _phones.BroadcastAsync(new Packet { Face = FaceState.Make(gazeX, gazeY, mood) });
    }

    private void InstallHotkeys()
    {
        var hwnd = new WindowInteropHelper(_overlay!).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(HotkeyHook);
        RegisterHotKey(hwnd, 1, 0x0002 | 0x0001, 0x53); // Ctrl+Alt+S
        RegisterHotKey(hwnd, 2, 0x0002 | 0x0001, 0x20); // Ctrl+Alt+Space
        RegisterHotKey(hwnd, 3, 0x0002 | 0x0001, 0x48); // Ctrl+Alt+H
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0312 || _session == null || _phones == null) return IntPtr.Zero;
        if (wParam == 1)
        {
            _session.Stop();
            _ = _phones.BroadcastAsync(Packet.Of("stop"));
            handled = true;
        }
        else if (wParam == 2)
        {
            _ = _phones.BroadcastAsync(Packet.Of(_session.Awake ? "sleep" : "wake"));
            handled = true;
        }
        else if (wParam == 3)
        {
            TakeMouseBack();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private HelpWindow HelpWindowFor()
    {
        if (_helpWindow != null) return _helpWindow;
        _helpWindow = new HelpWindow();
        _helpWindow.StopRequested += StopHelp;
        _helpWindow.MouseBackRequested += TakeMouseBack;
        _helpWindow.NewCodeRequested += ShowHelpCode;
        return _helpWindow;
    }

    private RemoteHelpWindow? _remoteHelp;

    private void ShowRemoteHelp()
    {
        if (_remoteHelp == null || _remoteHelp.IsDisposed)
            _remoteHelp = new RemoteHelpWindow();
        if (!_remoteHelp.Visible) _remoteHelp.Show();
        _remoteHelp.Activate();
    }

    private void ShowHelpCode()
    {
        var previous = _help.KickHelper();
        _helpSpeaker.Reset();
        if (previous != null && _phones != null)
            _ = _phones.SendToAsync(previous, Packet.Of("deskHelpState", "ended"));
        HelpWindowFor().ShowCode(_help.StartCode());
    }

    private void StopHelp()
    {
        if (_stoppingHelp) return;
        _stoppingHelp = true;
        try
        {
            var helper = _help.KickHelper();
            _help.CancelCode();
            _helpSpeaker.Reset();
            if (helper != null && _phones != null)
                _ = _phones.SendToAsync(helper, Packet.Of("deskHelpState", "ended"));
            _helpWindow?.Hide();
        }
        finally
        {
            _stoppingHelp = false;
        }
    }

    private void TakeMouseBack()
    {
        var helper = _help.RevokeHelperControl();
        if (helper == null || _phones == null) return;
        _ = _phones.SendToAsync(helper, Packet.Of("deskHelpState", "watch"));
        _helpWindow?.ShowWatching();
    }

    private void FinishControlAsk(string clientId)
    {
        if (!_help.IsHelper(clientId))
        {
            _help.ClearAsking(clientId);
            return;
        }
        HelpWindowFor().ShowAsk(allow =>
        {
            if (allow && _help.Grant(clientId))
            {
                _ = _phones!.SendToAsync(clientId, Packet.Of("deskHelpState", "control"));
                HelpWindowFor().ShowControlling();
            }
            else
            {
                _help.ClearAsking(clientId);
                _ = _phones!.SendToAsync(clientId, Packet.Of("deskHelpState", "denied"));
                if (_help.IsHelper(clientId)) HelpWindowFor().ShowWatching();
            }
        });
    }

    private void SweepHelpControl()
    {
        _help.ExpirePark();
        var helper = _help.ExpireControl();
        if (helper == null || _phones == null) return;
        _ = _phones.SendToAsync(helper, Packet.Of("deskHelpState", "watch"));
        _helpWindow?.ShowWatching();
    }

    private string GetTtsStatusJson()
    {
        if (_speaker == null) return "{}";
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            enabled = _speaker.Enabled,
            ready = _speaker.IsReady,
            running = _speaker.IsRunning,
            status = _speaker.StatusText,
            error = _speaker.LastError ?? "",
        });
    }

    private void BroadcastTtsStatus()
    {
        if (_phones == null || _speaker == null) return;
        _ = _phones.BroadcastAsync(new Packet
        {
            Command = "ttsStatus",
            Text = GetTtsStatusJson(),
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point point);
}
