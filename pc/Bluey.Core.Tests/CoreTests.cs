using Xunit;
using Bluey.Auth;
using Bluey.Brains;
using Bluey.Desk;
using Bluey.Protocol;
using Bluey.Session;
using Bluey.Speech;

namespace Bluey.Core.Tests;

public class CoreTests
{
    [Fact]
    public void Packet_round_trips_a_face_and_a_tool_call()
    {
        var bytes = ProtocolJson.Encode(new Packet
        {
            Face = FaceState.Make(0.5, -0.25, Mood.Pointing, 0.2),
            Command = "tool",
            Tool = "point_at",
            CallId = "abc",
            Text = "{\"target_id\":\"W3\"}",
        });
        var buffer = bytes.Concat(ProtocolJson.Encode(Packet.Of("awake"))).ToArray();
        var length = buffer.Length;
        var packets = ProtocolJson.Decode(ref buffer, ref length);
        Assert.Equal(2, packets.Count);
        Assert.Equal("pointing", packets[0].Face!.Mood);
        Assert.Equal("abc", packets[0].CallId);
        Assert.Equal("awake", packets[1].Command);
        Assert.Equal(0, length);
    }

    [Fact]
    public void Grid_maps_the_middle_of_the_screen_to_500()
    {
        var size = new SizeD(1920, 1080);
        var pixel = Grid.ToPixel(500, 1000, size);
        Assert.Equal(960, pixel.X);
        Assert.Equal(1080, pixel.Y);
        var grid = Grid.ToGrid(new PointD(960, 540), size);
        Assert.Equal(500, grid.X);
        Assert.Equal(500, grid.Y);
    }

    [Fact]
    public void Grid_uses_the_monitor_origin()
    {
        var size = new SizeD(1920, 1080);
        var origin = new PointD(1920, 0);
        var grid = Grid.ToGrid(new PointD(2880, 540), size, origin);
        Assert.Equal(500, grid.X);
        Assert.Equal(500, grid.Y);
        var pixel = Grid.ToPixel(500, 500, size, origin);
        Assert.Equal(2880, pixel.X);
        Assert.Equal(540, pixel.Y);
    }

    [Fact]
    public void Describe_keeps_a_name_on_a_second_monitor_and_notices_the_mouse_left()
    {
        var shot = new ScreenCapture(
            Array.Empty<byte>(),
            new[] { new ScreenTarget("L1", "line", "Metal", new RectD(2000, 100, 80, 20)) },
            "Spotify",
            new SizeD(1920, 1080),
            new PointD(1920, 0));
        var text = Grid.Describe(shot, new PointD(100, 100));
        Assert.Contains("Metal", text);
        Assert.Contains("@62,101", text);
        Assert.Contains("outside this picture", text);
    }

    [Fact]
    public void Stored_look_stays_for_90_seconds_then_becomes_a_note()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var fresh = new HistoryTurn("user", "play metal", new byte[] { 1 }, "Frontmost app: Spotify", now.AddSeconds(-90), "Spotify");
        Assert.True(LookMemory.SendPixels(fresh, false, now));
        var stale = fresh with { SeenAt = now.AddSeconds(-91) };
        Assert.False(LookMemory.SendPixels(stale, false, now));
        var expired = LookMemory.Expire(stale, now);
        Assert.Null(expired.Jpeg);
        Assert.Null(expired.ScreenText);
        Assert.Contains("Last look: Spotify.", expired.Text);
        Assert.Contains("play metal", expired.Text);
    }

    [Fact]
    public void A_new_shot_is_the_only_picture_sent()
    {
        var now = DateTimeOffset.UtcNow;
        var older = new HistoryTurn("user", "open it", new byte[] { 1 }, "old", now.AddSeconds(-5), "Chrome");
        var newer = new HistoryTurn("user", "play metal", new byte[] { 2 }, "new", now, "Spotify");
        var history = new List<HistoryTurn> { older, new HistoryTurn("assistant", "Opened."), newer };
        Assert.Same(newer, LookMemory.LatestFresh(history, false, now));
        Assert.Null(LookMemory.LatestFresh(history, true, now));
        var released = LookMemory.Release(older, note: false);
        Assert.Null(released.Jpeg);
        Assert.Equal("open it", released.Text);
    }

    [Fact]
    public void Guard_refuses_lock_and_allows_a_normal_shortcut()
    {
        Assert.Contains("lock", ActionGuard.Refusal("press_keys", true, false, "{\"keys\":\"Win+L\"}")!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ActionGuard.Refusal("press_keys", true, false, "{\"keys\":\"ctrl+t\"}"));
        Assert.Contains("turned off", ActionGuard.Refusal("click", false, false, "{}")!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop", ActionGuard.Refusal("click", true, true, "{}")!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pkce_challenge_is_the_s256_digest()
    {
        var pkce = Pkce.Create();
        var expected = Pkce.Base64Url(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(pkce.Verifier)));
        Assert.Equal(expected, pkce.Challenge);
        var start = ChatGptOAuth.Begin(new AppSettings { HostId = "urn:uuid:test" }, 1455, returning: false);
        Assert.Contains("client_id=dynamic_agent_client", start.Url);
        Assert.Contains("agent_name_hint=Bluey%20PC", start.Url);
        Assert.Contains("chatgpt.tokens.use.direct", Uri.UnescapeDataString(start.Url));
        Assert.Contains("http://127.0.0.1:1455/auth/callback", Uri.UnescapeDataString(start.Url));
        Assert.DoesNotContain("id_token_hint", start.Url);
    }

    [Fact]
    public void Client_secret_accepts_the_openai_and_xai_shapes()
    {
        Assert.Equal("ek_123", TokenExtract.ClientSecret("{\"value\":\"ek_123\",\"expires_at\":1}"));
        Assert.Equal("secret", TokenExtract.ClientSecret("{\"client_secret\":{\"value\":\"secret\"}}"));
        Assert.Null(TokenExtract.ClientSecret("not json"));
    }

    [Fact]
    public void Sse_parser_collects_text_and_a_tool_call()
    {
        var body = """
            data: {"type":"response.output_text.delta","delta":"Look "}
            data: {"type":"response.output_text.delta","delta":"here"}
            data: {"type":"response.output_item.done","item":{"type":"function_call","call_id":"call_1","name":"look_at_screen","arguments":"{}"}}
            data: [DONE]
            """;
        var reply = SseParser.Parse(body);
        Assert.Equal("Look here", reply.Text);
        Assert.Equal("look_at_screen", reply.ToolCalls[0].Name);
    }

    [Fact]
    public void Plan_limit_becomes_a_spoken_line()
    {
        var body = """
            data: {"type":"response.failed","response":{"error":{"code":"subscription_sharing_usage_limit_exceeded"}}}
            """;
        var reply = SseParser.Parse(body);
        Assert.Contains("plan limit", reply.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Anthropic_blocks_become_text_and_tools()
    {
        var reply = BrainClient.ParseAnthropic("""
            {"content":[
              {"type":"text","text":"That button."},
              {"type":"tool_use","id":"toolu_1","name":"point_at","input":{"target_id":"C2"}}
            ]}
            """);
        Assert.Equal("That button.", reply.Text);
        Assert.Equal("point_at", reply.ToolCalls[0].Name);
        Assert.Contains("C2", reply.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public void Mode_is_ready_only_with_the_matching_credential()
    {
        var settings = new AppSettings { Mode = BrainMode.XaiLive };
        Assert.False(settings.ModeIsReady());
        settings.XaiApiKey = "xai-test";
        Assert.True(settings.ModeIsReady());
        Assert.True(settings.RunsRealtimeOnPhone);
        settings.Mode = BrainMode.ChatGptPlan;
        Assert.False(settings.RunsRealtimeOnPhone);
        Assert.False(settings.ModeIsReady());
        settings.ChatGpt = new ChatGptAccount { RefreshToken = "r", Scopes = new List<string> { "chatgpt.tokens.use.direct" } };
        Assert.True(settings.ModeIsReady());
        settings.Mode = BrainMode.Cerebras;
        Assert.False(settings.ModeIsReady());
        Assert.False(settings.RunsRealtimeOnPhone);
        settings.CerebrasApiKey = "csk-test";
        settings.CerebrasModel = "qwen-3.8-27b";
        Assert.True(settings.ModeIsReady());
        settings.Mode = BrainMode.OpenRouter;
        Assert.False(settings.ModeIsReady());
        settings.OpenRouterApiKey = "sk-or-test";
        Assert.True(settings.ModeIsReady());
        Assert.False(settings.RunsRealtimeOnPhone);
    }

    [Fact]
    public void OpenRouter_pins_the_chosen_provider_and_rejects_unknown_ones()
    {
        Assert.Equal("modelrun", OpenRouterProviders.Normalize("nope"));
        Assert.Equal("near-ai", OpenRouterProviders.Normalize("near-ai"));
        var pin = OpenRouterProviders.Pin("dekallm");
        Assert.Equal(new[] { "dekallm" }, (string[])pin["only"]);
        Assert.Equal(false, pin["allow_fallbacks"]);
        Assert.Equal("ModelRun", OpenRouterProviders.Label("modelrun"));
    }

    [Fact]
    public void OpenRouter_nitro_models_skip_the_host_pin()
    {
        Assert.Equal("qwen/qwen3.8-27b", OpenRouterModels.Normalize(null));
        Assert.Equal("qwen/qwen3.8-27b", OpenRouterModels.Normalize("not-a-model"));
        Assert.Equal("qwen/qwen3.8-flash-next:nitro", OpenRouterModels.Normalize("qwen/qwen3.8-flash-next:nitro"));

        var pinned = OpenRouterModels.RequestShape("qwen/qwen3.8-27b", "dekallm");
        Assert.Equal("qwen/qwen3.8-27b", pinned.Model);
        Assert.NotNull(pinned.Provider);
        Assert.Equal(new[] { "dekallm" }, (string[])pinned.Provider!["only"]);
        Assert.Equal(false, pinned.Provider["allow_fallbacks"]);
        Assert.Equal("none", pinned.ReasoningEffort);
        Assert.False(pinned.DisableReasoning);

        var flash = OpenRouterModels.RequestShape("qwen/qwen3.8-flash:nitro", "modelrun");
        Assert.Equal("qwen/qwen3.8-flash:nitro", flash.Model);
        Assert.Null(flash.Provider);
        Assert.Null(flash.ReasoningEffort);
        Assert.True(flash.DisableReasoning);

        var next = OpenRouterModels.RequestShape("qwen/qwen3.8-flash-next:nitro", "near-ai");
        Assert.Null(next.Provider);
        Assert.True(next.DisableReasoning);

        var omni = OpenRouterModels.RequestShape("qwen/qwen3.8-omni-flash:nitro", "near-ai");
        Assert.Null(omni.Provider);
        Assert.True(omni.DisableReasoning);

        var gemini35 = OpenRouterModels.RequestShape("google/gemini-3.5-flash-lite:nitro", "modelrun");
        Assert.Null(gemini35.Provider);
        Assert.Equal("minimal", gemini35.ReasoningEffort);
        Assert.False(gemini35.DisableReasoning);

        var gemini31 = OpenRouterModels.RequestShape("google/gemini-3.1-flash-lite:nitro", "dekallm");
        Assert.Null(gemini31.Provider);
        Assert.Equal("none", gemini31.ReasoningEffort);
        Assert.False(gemini31.DisableReasoning);

        Assert.Equal("Qwen 3.8 27B · DekaLLM", OpenRouterModels.RouteLabel("qwen/qwen3.8-27b", "dekallm"));
        Assert.Equal("Qwen 3.8 Flash", OpenRouterModels.RouteLabel("qwen/qwen3.8-flash:nitro", "modelrun"));
        Assert.Equal("Gemini 3.1 Flash Lite", OpenRouterModels.RouteLabel("google/gemini-3.1-flash-lite:nitro", "modelrun"));
    }

    [Fact]
    public void DotEnv_reads_a_key_and_ignores_comments()
    {
        var values = DotEnv.Parse("""
            # comment
            export CEREBRAS_API_KEY='csk-test'
            EMPTY=
            """);
        Assert.Equal("csk-test", values["CEREBRAS_API_KEY"]);
        Assert.Equal("", values["EMPTY"]);
    }

    [Fact]
    public void Cerebras_chat_completion_becomes_text_and_tools()
    {
        var reply = BrainClient.ParseChatCompletion("""
            {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
              {"id":"efa96a7b1","type":"function","function":{"name":"point_at","arguments":"{\"target_id\":\"C2\"}"}}
            ]}}]}
            """);
        Assert.Equal("", reply.Text);
        Assert.Equal("point_at", reply.ToolCalls[0].Name);
        Assert.Equal("efa96a7b1", reply.ToolCalls[0].CallId);
        Assert.Contains("C2", reply.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public void Settings_round_trip_through_dpapi()
    {
        var path = Path.Combine(Path.GetTempPath(), "bluey-test-" + Guid.NewGuid().ToString("N"), "settings.bin");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Mode = BrainMode.OpenAiLive,
            OpenAiApiKey = "sk-test",
            PhoneSeat = "left",
            OpenRouterModel = "google/gemini-3.5-flash-lite:nitro",
            OpenRouterProvider = "NEAR-AI",
        };
        store.Save(settings);
        var loaded = store.Load();
        Assert.Equal(BrainMode.OpenAiLive, loaded.Mode);
        Assert.Equal("sk-test", loaded.OpenAiApiKey);
        Assert.Equal("left", loaded.PhoneSeat);
        Assert.Equal("google/gemini-3.5-flash-lite:nitro", loaded.OpenRouterModel);
        Assert.Equal("near-ai", loaded.OpenRouterProvider);
        store.Save(new AppSettings { OpenRouterModel = "nope", OpenRouterProvider = "nope" });
        loaded = store.Load();
        Assert.Equal("qwen/qwen3.8-27b", loaded.OpenRouterModel);
        Assert.Equal("modelrun", loaded.OpenRouterProvider);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Speech_config_round_trips_and_normalizes_sex()
    {
        var root = Path.Combine(Path.GetTempPath(), "bluey-speech-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "pc", "bin");
        Directory.CreateDirectory(nested);
        var path = Path.Combine(root, SpeechConfig.FileName);
        new SpeechConfig { TextToSpeech = true, Sex = "FEMALE", Lang = "pl" }.Save(path);
        var found = SpeechConfig.FindExisting(new[] { nested });
        Assert.Equal(path, found);
        var loaded = SpeechConfig.Load(path!);
        Assert.True(loaded.TextToSpeech);
        Assert.Equal("female", loaded.Sex);
        Assert.Equal("pl", loaded.Lang);
        File.WriteAllText(path, """{"textToSpeech":true,"sex":"nope","lang":"zz"}""");
        loaded = SpeechConfig.Load(path);
        Assert.Equal("male", loaded.Sex);
        Assert.Equal("na", loaded.Lang);
        Assert.Equal("Hello there.", SpeechText.Prepare("  Hello   there. \n"));
        Assert.True(SpeechText.Prepare(new string('a', 800)).Length <= SpeechText.MaxChars);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Spoken_marks_are_added_only_when_speech_is_on()
    {
        Assert.DoesNotContain("[[spoken]]", Personality.Instructions(false));
        Assert.DoesNotContain("[[spoken]]", Personality.Instructions(true));
        var on = Personality.Instructions(true, speakAloud: true);
        Assert.Contains("[[spoken]]", on);
        Assert.Contains("[[/spoken]]", on);
        Assert.Equal("Heard.", SpokenSpan.Extract("Shown. [[spoken]]Heard.[[/spoken]] More."));
        Assert.Null(SpokenSpan.Extract("Shown only."));
        Assert.Null(SpokenSpan.Extract("[[spoken]]no close"));
        Assert.Equal("Shown. Heard. More.", SpokenSpan.Strip("Shown. [[spoken]]Heard.[[/spoken]] More."));
        Assert.Equal("One. Two.", SpokenSpan.Extract("[[spoken]]One.[[/spoken]] and [[SPOKEN]]Two.[[/spoken]]"));
    }

    [Fact]
    public void Only_the_marked_span_is_spoken()
    {
        var speaker = new RecordingSpeaker { Enabled = true };
        string? shown = null;
        var session = new CharacterSession(
            new BrainClient(new HttpClient(), new SettingsStore(Path.Combine(Path.GetTempPath(), "bluey-unused-" + Guid.NewGuid().ToString("N")))),
            new FakeDesk(),
            speaker);
        session.CaptionChanged += text => shown = text;
        session.ShowCaption("The list is long. [[spoken]]It's the metal one.[[/spoken]]");
        Assert.Equal("The list is long. It's the metal one.", shown);
        Assert.Equal(new[] { "It's the metal one." }, speaker.Spoken);
        session.ShowCaption("Nothing to hear.");
        Assert.Equal(new[] { "It's the metal one." }, speaker.Spoken);
        Assert.Equal(1, speaker.Stops);
        session.ShowCaption("  ");
        Assert.Equal(2, speaker.Stops);
        session.Stop();
        Assert.Equal(3, speaker.Stops);
    }

    [Fact]
    public async Task Ask_runs_a_tool_then_answers()
    {
        var desk = new FakeDesk();
        var session = new CharacterSession(new BrainClient(new HttpClient(), new SettingsStore(Path.Combine(Path.GetTempPath(), "bluey-unused-" + Guid.NewGuid().ToString("N")))), desk);
        // The real client would call the network. This test drives RunTool directly.
        var look = await session.RunToolAsync("look_at_screen", "{}", CancellationToken.None);
        Assert.Contains("Frontmost app: Notepad", look.Text);
        Assert.Contains("W1", look.Text);
        var point = await session.RunToolAsync("point_at", "{\"target_id\":\"W1\"}", CancellationToken.None);
        Assert.Contains("Hello", point.Text);
        var refused = await session.RunToolAsync("click", "{\"target_id\":\"W1\"}", CancellationToken.None);
        Assert.Contains("turned off", refused.Text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RecordingSpeaker : IReplySpeaker
{
    public bool Enabled { get; set; }
    public List<string> Spoken { get; } = new();
    public int Stops { get; private set; }
    public void Speak(string text) => Spoken.Add(text);
    public void Stop() => Stops++;
}

public sealed class FakeDesk : IDesk
{
    public bool ComputerControl => false;
    public SizeD ScreenSize => new(1000, 500);
    public PointD Mouse => new(100, 40);
    public List<PointD> Clicks { get; } = new();

    public Task<ScreenCapture> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(new ScreenCapture(
        new byte[] { 1, 2, 3 },
        new[]
        {
            new ScreenTarget("L1", "line", "Hello world", new RectD(80, 20, 200, 30)),
            new ScreenTarget("W1", "word", "Hello", new RectD(80, 20, 80, 30)),
        },
        "Notepad",
        ScreenSize));

    public Task ClickAsync(PointD point, bool right, int count, CancellationToken cancellationToken)
    {
        Clicks.Add(point);
        return Task.CompletedTask;
    }

    public Task TypeAsync(string text, Func<string, Task> onChunk, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PressAsync(string keys, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ScrollAsync(PointD point, int dx, int dy, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DragAsync(PointD from, PointD to, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<string> OpenAppAsync(string name, CancellationToken cancellationToken) => Task.FromResult("Opened " + name);
    public string OpenUrl(string url) => "Opened.";
    public bool FocusedFieldIsPassword() => false;
    public void Warp(PointD point) { }
}
