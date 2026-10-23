using System.Text.Json;
using Bluey.Auth;
using Bluey.Brains;
using Bluey.Desk;
using Bluey.Protocol;
using Bluey.Speech;

namespace Bluey.Session;

public sealed class ToolRun
{
    public required string Text { get; init; }
    public string? ImageBase64 { get; init; }
    public byte[]? Jpeg { get; init; }
    public string? AppName { get; init; }
    public bool GoToSleep { get; init; }
    public bool Ok { get; init; } = true;
}

public static class TaskOutcome
{
    public static readonly string[] Wins =
    [
        "win_hop", "win_bow", "win_spin", "win_stretch", "win_dab",
        "win_wiggle", "win_puff", "win_kick", "win_nod", "win_star",
    ];

    public static string Pose(bool failed, int spin) => failed ? "sad" : Wins[Math.Abs(spin) % Wins.Length];

    public const string NoTouchBlock = "No-touch mode is on, so I can't do that.";

    public static string Fallback(bool failed, bool acted, bool looked, string? problem)
    {
        if (!failed) return acted ? "Done." : looked ? "Here's what is on the screen." : "Done.";
        var line = FirstLine(problem);
        return string.IsNullOrWhiteSpace(line) ? "That didn't work." : line;
    }

    public static bool WantsComputerWork(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant();
        string[] phrases =
        [
            "click", "double-click", "double click", "right-click", "right click",
            "type ", "press ", "scroll", "drag", "launch", "delete",
            "move the mouse", "move mouse", "switch to", "switch the",
            "open ", "close ", "shut ",
        ];
        foreach (var phrase in phrases)
            if (t.Contains(phrase, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool MentionsNoTouch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("no-touch", StringComparison.Ordinal) || t.Contains("no touch", StringComparison.Ordinal);
    }

    private static string FirstLine(string? problem)
    {
        if (string.IsNullOrWhiteSpace(problem)) return "";
        var line = problem.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] : line;
    }
}

public sealed class CharacterSession
{
    private readonly BrainClient _brains;
    private readonly IDesk _desk;
    private readonly IReplySpeaker? _speaker;
    private readonly List<HistoryTurn> _history = new();
    private ScreenCapture? _shot;
    private readonly object _gate = new();
    private int _win;
    private readonly Queue<OutputLine> _outputs = new();
    private readonly object _outputGate = new();
    private int _outputGen;
    private bool _draining;
    private TaskCompletionSource? _playbackWait;

    private sealed class OutputLine
    {
        public string? Display;
        public string? Spoken;
    }

    public CharacterSession(BrainClient brains, IDesk desk, IReplySpeaker? speaker = null)
    {
        _brains = brains;
        _desk = desk;
        _speaker = speaker;
    }

    public bool Awake { get; private set; }
    public DateTimeOffset StoppedUntil { get; private set; }
    public event Action<string?, bool>? CaptionChanged;
    public event Action? OutputsIdle;
    public event Action<string>? SpeechGaveUp;
    internal static TimeSpan GapWhenSilent { get; set; } = SpeechPace.QuietHold;
    public event Action<PointD?>? PointChanged;
    public event Action<bool>? ActivityChanged;
    public event Action<string>? PoseRequested;
    private int _busy;

    private void Busy(bool on)
    {
        _busy += on ? 1 : -1;
        if (_busy < 0) _busy = 0;
        ActivityChanged?.Invoke(_busy > 0);
    }

    public void ShowCaption(string? text) => Present(text, speak: true);

    public string DescribeMode()
    {
        var settings = _brains.Settings;
        var ready = settings.ModeIsReady() ? "ready" : "needs login";
        return settings.Mode switch
        {
            BrainMode.XaiLive => "Grok live (xAI API key) — " + ready,
            BrainMode.OpenAiLive => "OpenAI live (API key) — " + ready,
            BrainMode.ChatGptPlan => "ChatGPT plan — " + ready,
            BrainMode.Anthropic => "Claude API key — " + ready,
            BrainMode.Cerebras => "Cerebras " + settings.CerebrasModel + " — " + ready,
            BrainMode.OpenRouter => "OpenRouter " + OpenRouterModels.RouteLabel(settings.OpenRouterModel, settings.OpenRouterProvider) + " — " + ready,
            _ => settings.Mode.ToString(),
        };
    }

    public string SessionJson()
    {
        var settings = _brains.Settings;
        var provider = settings.Mode switch
        {
            BrainMode.XaiLive => "xai",
            BrainMode.OpenAiLive => "openai",
            BrainMode.ChatGptPlan => "chatgpt",
            BrainMode.Cerebras => "cerebras",
            BrainMode.OpenRouter => "openrouter",
            _ => "anthropic",
        };
        return JsonSerializer.Serialize(new
        {
            mode = settings.Mode.ToString(),
            realtime = settings.RunsRealtimeOnPhone && settings.ModeIsReady(),
            provider,
            route = settings.Mode == BrainMode.OpenRouter ? OpenRouterModels.RouteLabel(settings.OpenRouterModel, settings.OpenRouterProvider) : "",
            computerControl = settings.ComputerControl,
            noTouch = settings.NoTouch,
        });
    }

    public void Stop()
    {
        StoppedUntil = DateTimeOffset.UtcNow.AddSeconds(6);
        DropLooks();
        CancelOutputs();
        _speaker?.Stop();
        PointChanged?.Invoke(null);
        CaptionChanged?.Invoke("Stopped", false);
    }

    public async Task<Packet?> HandleAsync(Packet packet, Func<Packet, Task> push, CancellationToken cancellationToken)
    {
        switch (packet.Command)
        {
            case "session":
                return Packet.Of("session", SessionJson(), packet.CallId);
            case "awake":
                Awake = true;
                return Packet.Of("session", SessionJson(), packet.CallId);
            case "asleep":
                Awake = false;
                DropLooks();
                PointChanged?.Invoke(null);
                CancelOutputs();
                _speaker?.Stop();
                CaptionChanged?.Invoke(null, false);
                return Packet.Of("session", SessionJson(), packet.CallId);
            case "realtimeToken":
                try
                {
                    var secret = await _brains.MintAsync(_brains.Settings, cancellationToken);
                    return new Packet { Command = "realtimeToken", CallId = packet.CallId, Text = secret.ToJson() };
                }
                catch (Exception ex)
                {
                    CaptionChanged?.Invoke(ex.Message, false);
                    return new Packet { Command = "realtimeToken", CallId = packet.CallId, Text = JsonSerializer.Serialize(new { error = ex.Message }) };
                }
            case "tool":
                Busy(true);
                ToolRun run;
                try
                {
                    run = await RunToolAsync(packet.Tool ?? "", packet.Text ?? "{}", cancellationToken);
                }
                finally
                {
                    Busy(false);
                }
                if (run.GoToSleep)
                {
                    Awake = false;
                    await push(Packet.Of("sleep"));
                }
                return new Packet { Command = "toolResult", CallId = packet.CallId, Text = run.Text, Image = run.ImageBase64 };
            case "ask":
                await AskAsync(packet.Text ?? "", push, cancellationToken);
                return Packet.Of("askDone", null, packet.CallId);
            case "caption":
                CaptionChanged?.Invoke(packet.Text, false);
                return null;
            case "captionDone":
                CaptionChanged?.Invoke(string.IsNullOrWhiteSpace(packet.Text) ? null : packet.Text, false);
                return null;
            case "stop":
                Stop();
                return Packet.Of("stop", "Stopped", packet.CallId);
            case "handoff":
                var target = "claude";
                var prompt = packet.Text ?? "";
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(packet.Text) ? "{}" : packet.Text);
                    if (doc.RootElement.TryGetProperty("target", out var targetValue)) target = targetValue.GetString() ?? target;
                    if (doc.RootElement.TryGetProperty("prompt", out var promptValue)) prompt = promptValue.GetString() ?? "";
                }
                catch (JsonException)
                {
                    // The whole text is the prompt.
                }
                var handoff = await AgentHandoff.RunAsync(target, prompt, cancellationToken);
                Present(handoff, speak: true, fallbackToDisplay: true);
                return new Packet { Command = "handoff", CallId = packet.CallId, Text = handoff };
            default:
                return null;
        }
    }

    public async Task AskAsync(string userText, Func<Packet, Task> push, CancellationToken cancellationToken)
    {
        userText = userText.Trim();
        if (userText.Length == 0)
        {
            await ShowAsync(push, "I didn't catch that.", done: true);
            return;
        }
        var settings = _brains.Settings;
        if (!settings.ModeIsReady())
        {
            await ShowAsync(push, "Pick a login in the tray first. " + DescribeMode(), done: true);
            return;
        }
        var prior = new List<CompletedTool>();
        byte[]? jpeg = null;
        string? screen = null;
        string? seenApp = null;
        string finalText = "";
        var slept = false;
        var acted = false;
        var failed = false;
        var looked = false;
        string? problem = null;
        List<HistoryTurn> history;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < _history.Count; i++)
                _history[i] = LookMemory.Expire(_history[i], now);
            history = _history.ToList();
        }
        Busy(true);
        try
        {
            for (var round = 0; round < 6; round++)
            {
                var reply = await _brains.CompleteAsync(settings, new AskInput
                {
                    UserText = userText,
                    History = history,
                    PriorTools = prior,
                    Jpeg = jpeg,
                    ScreenText = screen,
                    ComputerControl = settings.ComputerControl,
                    NoTouch = settings.NoTouch,
                    SpeakAloud = _speaker?.Enabled == true,
                }, cancellationToken);
                if (!string.IsNullOrWhiteSpace(reply.Text))
                {
                    finalText = SpokenSpan.Strip(reply.Text);
                    await ShowAsync(push, reply.Text, done: reply.ToolCalls.Count == 0);
                }
                if (reply.ToolCalls.Count == 0) break;
                var sleep = false;
                foreach (var call in reply.ToolCalls)
                {
                    var run = await RunToolAsync(call.Name, call.ArgumentsJson, cancellationToken);
                    if (ActionGuard.ActionNames.Contains(call.Name))
                    {
                        acted = true;
                        if (!run.Ok)
                        {
                            failed = true;
                            problem ??= run.Text;
                        }
                    }
                    if (run.Jpeg != null) looked = true;
                    prior.Add(new CompletedTool(call.CallId, call.Name, call.ArgumentsJson, run.Text));
                    if (run.Jpeg != null)
                    {
                        jpeg = run.Jpeg;
                        screen = run.Text;
                        if (!string.IsNullOrWhiteSpace(run.AppName)) seenApp = run.AppName;
                    }
                    sleep |= run.GoToSleep;
                }
                if (sleep)
                {
                    Awake = false;
                    slept = true;
                    await push(Packet.Of("sleep"));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            failed = true;
            problem ??= ex.Message;
            finalText = ex.Message;
            await ShowAsync(push, ex.Message, done: true);
        }
        finally
        {
            Busy(false);
        }
        var stopped = DateTimeOffset.UtcNow < StoppedUntil;
        var blocked = settings.NoTouch && (TaskOutcome.WantsComputerWork(userText) || (acted && failed));
        if (blocked) failed = true;
        if (!slept && !stopped && blocked && !TaskOutcome.MentionsNoTouch(finalText))
        {
            var why = TaskOutcome.NoTouchBlock;
            finalText = string.IsNullOrWhiteSpace(finalText) ? why : finalText.TrimEnd() + " " + why;
            await ShowAsync(push, why, done: true);
        }
        else if (string.IsNullOrWhiteSpace(finalText) && !slept)
        {
            if (!acted && !looked) failed = true;
            finalText = stopped ? "Stopped." : TaskOutcome.Fallback(failed, acted, looked, problem);
            await ShowAsync(push, finalText, done: true);
        }
        if (!slept && !stopped)
            PoseRequested?.Invoke(TaskOutcome.Pose(failed, _win++));
        var drop = slept || DateTimeOffset.UtcNow < StoppedUntil;
        lock (_gate)
        {
            if (jpeg != null)
            {
                for (var i = 0; i < _history.Count; i++)
                    _history[i] = LookMemory.Release(_history[i], note: false);
                _history.Add(new HistoryTurn("user", userText, jpeg, screen, DateTimeOffset.UtcNow, seenApp));
            }
            else
            {
                _history.Add(new HistoryTurn("user", userText));
            }
            _history.Add(new HistoryTurn("assistant", finalText));
            if (drop)
            {
                for (var i = 0; i < _history.Count; i++)
                    _history[i] = LookMemory.Release(_history[i], note: true);
            }
            if (_history.Count > 16) _history.RemoveRange(0, _history.Count - 16);
        }
    }

    private void DropLooks()
    {
        lock (_gate)
        {
            for (var i = 0; i < _history.Count; i++)
                _history[i] = LookMemory.Release(_history[i], note: true);
        }
    }

    private async Task ShowAsync(Func<Packet, Task> push, string text, bool done)
    {
        var display = Present(text, speak: done, fallbackToDisplay: done);
        await push(new Packet { Command = done ? "captionDone" : "caption", Text = display });
    }

    internal string Present(string? text, bool speak, bool fallbackToDisplay = false)
    {
        if (text == null)
        {
            EnqueueOutput(null, null);
            return "";
        }
        foreach (var act in SpokenSpan.TakeActs(text)) PoseRequested?.Invoke(act);
        var display = SpokenSpan.Strip(text);
        string? spoken = null;
        if (speak)
        {
            spoken = SpokenSpan.Extract(text);
            if (string.IsNullOrWhiteSpace(spoken) && fallbackToDisplay && _speaker?.Enabled == true && !string.IsNullOrWhiteSpace(display))
                spoken = display;
            if (string.IsNullOrWhiteSpace(spoken)) spoken = null;
        }
        EnqueueOutput(string.IsNullOrWhiteSpace(display) ? null : display, spoken);
        return display;
    }

    private void CancelOutputs()
    {
        _outputGen++;
        _playbackWait?.TrySetResult();
        lock (_outputGate)
        {
            _outputs.Clear();
            _draining = false;
        }
    }

    private void EnqueueOutput(string? display, string? spoken)
    {
        var gen = _outputGen;
        lock (_outputGate)
        {
            _outputs.Enqueue(new OutputLine { Display = display, Spoken = spoken });
            if (_draining) return;
            _draining = true;
        }
        _ = DrainOutputsAsync(gen);
    }

    private async Task DrainOutputsAsync(int gen)
    {
        try
        {
            while (gen == _outputGen)
            {
                OutputLine item;
                lock (_outputGate)
                {
                    if (gen != _outputGen || _outputs.Count == 0) break;
                    item = _outputs.Dequeue();
                }
                var voiced = _speaker?.Enabled == true && !string.IsNullOrWhiteSpace(item.Spoken);
                CaptionChanged?.Invoke(item.Display, voiced);
                SpeechLog.Write(voiced
                    ? "show then speak \"" + TrimForLog(item.Spoken) + "\""
                    : "show \"" + TrimForLog(item.Display) + "\" (no speech, hold " + (int)GapWhenSilent.TotalSeconds + "s)");
                if (voiced) await WaitForPlaybackAsync(gen, item.Spoken!);
                else await Task.Delay(GapWhenSilent);
                if (gen != _outputGen) break;
                bool more;
                lock (_outputGate) more = _outputs.Count > 0;
                if (!more) OutputsIdle?.Invoke();
            }
        }
        finally
        {
            var again = false;
            lock (_outputGate)
            {
                if (gen != _outputGen || _outputs.Count == 0)
                {
                    if (gen == _outputGen) _draining = false;
                }
                else again = true;
            }
            if (again) _ = DrainOutputsAsync(gen);
        }
    }

    private Task WaitForPlaybackAsync(int gen, string spoken)
    {
        if (_speaker == null) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _playbackWait = done;
        void Finish()
        {
            if (gen == _outputGen) done.TrySetResult();
        }
        void Fail(string message)
        {
            SpeechLog.Write("playback failed: " + message);
            Finish();
        }
        _speaker.PlaybackFinished += Finish;
        _speaker.Failed += Fail;
        _speaker.Speak(spoken);
        var timeout = Task.Delay(TimeSpan.FromSeconds(90));
        return Task.WhenAny(done.Task, timeout).ContinueWith(finished =>
        {
            _speaker.PlaybackFinished -= Finish;
            _speaker.Failed -= Fail;
            if (_playbackWait == done) _playbackWait = null;
            if (finished.Result == timeout && gen == _outputGen)
            {
                var message = "No playback end for \"" + TrimForLog(spoken) + "\" after 90s.";
                SpeechLog.Write(message);
                SpeechGaveUp?.Invoke(message);
            }
        });
    }

    private static string TrimForLog(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var flat = text.Replace("\r", " ").Replace("\n", " ");
        return flat.Length <= 80 ? flat : flat[..80];
    }

    public async Task<ToolRun> RunToolAsync(string name, string arguments, CancellationToken cancellationToken)
    {
        var settings = _brains.Settings;
        var stopped = DateTimeOffset.UtcNow < StoppedUntil;
        var refusal = ActionGuard.Refusal(name, settings.ComputerControl, stopped, arguments, settings.NoTouch);
        if (refusal != null) return new ToolRun { Text = refusal, Ok = false };

        switch (name)
        {
            case "look_at_screen":
                return await LookAsync(null, cancellationToken);
            case "point_at":
            {
                var id = ActionGuard.ReadString(arguments, "target_id");
                if (_shot == null) return new ToolRun { Text = "Call look_at_screen first." };
                var target = Grid.ById(_shot, id);
                if (target == null) return new ToolRun { Text = "No target " + id + ". Use an id from the last look_at_screen." };
                PointChanged?.Invoke(new PointD(target.Rect.MidX, target.Rect.MaxY + 3));
                return new ToolRun { Text = "Pointing at \"" + target.Text + "\"." };
            }
            case "point_at_spot":
            {
                var x = ActionGuard.ReadNumber(arguments, "x");
                var y = ActionGuard.ReadNumber(arguments, "y");
                if (x == null || y == null) return new ToolRun { Text = "Give x and y." };
                var size = _shot?.Size ?? _desk.ScreenSize;
                PointChanged?.Invoke(Grid.ToPixel(x.Value, y.Value, size, _shot?.Origin ?? default));
                return new ToolRun { Text = "Pointing there." };
            }
            case "stop_pointing":
                PointChanged?.Invoke(null);
                return new ToolRun { Text = "Your cursor will head home." };
            case "go_to_sleep":
                PoseRequested?.Invoke("sleep");
                PointChanged?.Invoke(null);
                return new ToolRun { Text = "Going to sleep. Say a very short goodbye.", GoToSleep = true };
            case "web_research":
            {
                var question = ActionGuard.ReadString(arguments, "question");
                if (string.IsNullOrWhiteSpace(question)) return new ToolRun { Text = "What should I research?" };
                var answer = await _brains.ResearchAsync(settings, question, cancellationToken);
                Present(answer, speak: true, fallbackToDisplay: true);
                return new ToolRun { Text = answer };
            }
            case "click":
                return await ClickAsync(arguments, cancellationToken);
            case "type_text":
                return await TypeAsync(arguments, cancellationToken);
            case "press_keys":
                return await PressAsync(arguments, cancellationToken);
            case "scroll":
                return await ScrollAsync(arguments, cancellationToken);
            case "drag":
                return await DragAsync(arguments, cancellationToken);
            case "open_app":
            {
                var app = ActionGuard.ReadString(arguments, "name");
                var opened = await _desk.OpenAppAsync(app, cancellationToken);
                var run = await LookAsync(opened, cancellationToken);
                return opened.StartsWith("Opened ", StringComparison.Ordinal) ? run : new ToolRun { Text = run.Text, ImageBase64 = run.ImageBase64, Jpeg = run.Jpeg, AppName = run.AppName, Ok = false };
            }
            case "open_url":
            {
                var opened = _desk.OpenUrl(ActionGuard.ReadString(arguments, "url"));
                var run = await LookAsync(opened, cancellationToken);
                return opened.StartsWith("Opened ", StringComparison.Ordinal) ? run : new ToolRun { Text = run.Text, ImageBase64 = run.ImageBase64, Jpeg = run.Jpeg, AppName = run.AppName, Ok = false };
            }
            default:
                return new ToolRun { Text = "Unknown tool " + name + "." };
        }
    }

    private async Task<ToolRun> LookAsync(string? prefix, CancellationToken cancellationToken)
    {
        var shot = await _desk.CaptureAsync(cancellationToken);
        _shot = shot;
        var text = Grid.Describe(shot, _desk.Mouse);
        if (prefix != null) text = prefix + "\nHere's the screen now (ids have changed):\n" + text;
        var image = shot.Jpeg.Length == 0 ? null : Convert.ToBase64String(shot.Jpeg);
        return new ToolRun { Text = text, ImageBase64 = image, Jpeg = shot.Jpeg, AppName = shot.AppName };
    }

    private bool TrySpot(string arguments, string idKey, string xKey, string yKey, out PointD point, out string label)
    {
        point = default;
        label = "that spot";
        var id = ActionGuard.ReadString(arguments, idKey);
        if (!string.IsNullOrEmpty(id) && _shot != null)
        {
            var target = Grid.ById(_shot, id);
            if (target != null)
            {
                point = new PointD(target.Rect.MidX, target.Rect.MidY);
                label = target.Text;
                return true;
            }
        }
        var x = ActionGuard.ReadNumber(arguments, xKey);
        var y = ActionGuard.ReadNumber(arguments, yKey);
        if (x == null || y == null) return false;
        point = Grid.ToPixel(x.Value, y.Value, _shot?.Size ?? _desk.ScreenSize, _shot?.Origin ?? default);
        return true;
    }

    private async Task<ToolRun> ClickAsync(string arguments, CancellationToken cancellationToken)
    {
        if (!TrySpot(arguments, "target_id", "x", "y", out var point, out var label))
            return new ToolRun { Text = "Tell me what to click: a target id, or x and y.", Ok = false };
        PointChanged?.Invoke(point);
        var right = ActionGuard.ReadBool(arguments, "right");
        var count = ActionGuard.ReadBool(arguments, "double") ? 2 : 1;
        await _desk.ClickAsync(point, right, count, cancellationToken);
        var verb = right ? "Right-clicked" : count == 2 ? "Double-clicked" : "Clicked";
        var name = label == "that spot" ? "there" : "\"" + label + "\"";
        return await LookAsync(verb + " " + name + ".", cancellationToken);
    }

    private async Task<ToolRun> TypeAsync(string arguments, CancellationToken cancellationToken)
    {
        var text = ActionGuard.ReadString(arguments, "text");
        if (text.Length == 0) return new ToolRun { Text = "Nothing to type.", Ok = false };
        if (_desk.FocusedFieldIsPassword())
            return new ToolRun { Text = "That's a password field. Ask the user to type it themselves.", Ok = false };
        await _desk.TypeAsync(text, chunk =>
        {
            CaptionChanged?.Invoke(chunk, false);
            return Task.CompletedTask;
        }, cancellationToken);
        if (ActionGuard.ReadBool(arguments, "press_return"))
        {
            await _desk.PressAsync("enter", cancellationToken);
            return await LookAsync("Typed it and pressed Enter.", cancellationToken);
        }
        return new ToolRun { Text = "Typed it." };
    }

    private async Task<ToolRun> PressAsync(string arguments, CancellationToken cancellationToken)
    {
        var keys = ActionGuard.ReadString(arguments, "keys");
        await _desk.PressAsync(keys, cancellationToken);
        CaptionChanged?.Invoke(keys, false);
        return await LookAsync("Pressed " + keys + ".", cancellationToken);
    }

    private async Task<ToolRun> ScrollAsync(string arguments, CancellationToken cancellationToken)
    {
        var direction = ActionGuard.ReadString(arguments, "direction").ToLowerInvariant();
        if (string.IsNullOrEmpty(direction)) direction = "down";
        var amount = Math.Clamp(ActionGuard.ReadNumber(arguments, "amount") ?? 3, 1, 10);
        var pixels = (int)(amount * 120);
        if (!TrySpot(arguments, "target_id", "x", "y", out var point, out _))
        {
            var size = _shot?.Size ?? _desk.ScreenSize;
            point = new PointD(size.Width / 2, size.Height / 2);
        }
        var (dx, dy) = direction switch
        {
            "up" => (0, -pixels),
            "left" => (-pixels, 0),
            "right" => (pixels, 0),
            _ => (0, pixels),
        };
        PointChanged?.Invoke(point);
        await _desk.ScrollAsync(point, dx, dy, cancellationToken);
        return await LookAsync("Scrolled " + direction + ".", cancellationToken);
    }

    private async Task<ToolRun> DragAsync(string arguments, CancellationToken cancellationToken)
    {
        if (!TrySpot(arguments, "from_id", "from_x", "from_y", out var from, out _) ||
            !TrySpot(arguments, "to_id", "to_x", "to_y", out var to, out _))
            return new ToolRun { Text = "Tell me where to drag from and to.", Ok = false };
        PointChanged?.Invoke(from);
        await _desk.DragAsync(from, to, cancellationToken);
        PointChanged?.Invoke(to);
        return await LookAsync("Dragged it.", cancellationToken);
    }
}
