using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bluey.Auth;

public enum BrainMode
{
    XaiLive,
    OpenAiLive,
    ChatGptPlan,
    Anthropic,
    Cerebras,
    OpenRouter,
}

public static class OpenRouterProviders
{
    public static readonly (string Slug, string Label)[] Choices =
    {
        ("modelrun", "ModelRun (faster)"),
        ("dekallm", "DekaLLM (cheaper)"),
        ("near-ai", "NEAR AI (cheaper)"),
    };

    public static string Normalize(string? value)
    {
        var slug = (value ?? "").Trim().ToLowerInvariant();
        foreach (var choice in Choices)
            if (choice.Slug == slug) return slug;
        return "modelrun";
    }

    public static string Label(string? value) => Normalize(value) switch
    {
        "dekallm" => "DekaLLM",
        "near-ai" => "NEAR AI",
        _ => "ModelRun",
    };

    public static Dictionary<string, object> Pin(string? value) => new()
    {
        ["only"] = new[] { Normalize(value) },
        ["allow_fallbacks"] = false,
    };
}

public readonly record struct OpenRouterRequest(
    string Model,
    Dictionary<string, object>? Provider,
    string? ReasoningEffort,
    bool DisableReasoning);

public readonly record struct OpenRouterModelChoice(
    string Id,
    string Label,
    string Short,
    bool PinProvider,
    string? ReasoningEffort,
    bool DisableReasoning);

public static class OpenRouterModels
{
    public const string DefaultId = "qwen/qwen3.8-27b";

    public static readonly OpenRouterModelChoice[] Choices =
    {
        new("qwen/qwen3.8-27b", "Qwen 3.8 27B (pick a host)", "Qwen 3.8 27B", true, "none", false),
        new("qwen/qwen3.8-flash-next:nitro", "Qwen 3.8 Flash Next :nitro", "Qwen 3.8 Flash Next", false, null, true),
        new("qwen/qwen3.8-flash:nitro", "Qwen 3.8 Flash :nitro", "Qwen 3.8 Flash", false, null, true),
        new("qwen/qwen3.8-omni-flash:nitro", "Qwen 3.8 Omni Flash :nitro", "Qwen 3.8 Omni Flash", false, null, true),
        new("google/gemini-3.5-flash-lite:nitro", "Gemini 3.5 Flash Lite :nitro", "Gemini 3.5 Flash Lite", false, "minimal", false),
        new("google/gemini-3.1-flash-lite:nitro", "Gemini 3.1 Flash Lite :nitro", "Gemini 3.1 Flash Lite", false, "none", false),
    };

    public static string Normalize(string? value)
    {
        var id = (value ?? "").Trim();
        foreach (var choice in Choices)
            if (string.Equals(choice.Id, id, StringComparison.Ordinal)) return choice.Id;
        return DefaultId;
    }

    public static OpenRouterModelChoice Find(string? value)
    {
        var id = Normalize(value);
        foreach (var choice in Choices)
            if (choice.Id == id) return choice;
        return Choices[0];
    }

    public static OpenRouterRequest RequestShape(string? model, string? provider)
    {
        var choice = Find(model);
        return new(
            choice.Id,
            choice.PinProvider ? OpenRouterProviders.Pin(provider) : null,
            choice.ReasoningEffort,
            choice.DisableReasoning);
    }

    public static string RouteLabel(string? model, string? provider)
    {
        var choice = Find(model);
        if (!choice.PinProvider) return choice.Short;
        return choice.Short + " · " + OpenRouterProviders.Label(provider);
    }
}

public sealed class ChatGptAccount
{
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "https://auth.openai.com";
    public string ClientId { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public List<string> Scopes { get; set; } = new();
    public DateTimeOffset AccessExpiresAt { get; set; }
    public DateTimeOffset SavedAt { get; set; }

    [JsonIgnore]
    public bool CanUsePlan => Scopes.Contains("chatgpt.tokens.use.direct") && !string.IsNullOrEmpty(RefreshToken);
}

public sealed class AppSettings
{
    public BrainMode Mode { get; set; } = BrainMode.XaiLive;
    public bool ComputerControl { get; set; }
    public bool NoTouch { get; set; }
    public bool Learn { get; set; }
    public bool LearnTell { get; set; }
    public bool LearnQuiet { get; set; }
    public bool Commands { get; set; }
    public bool CommandsMedium { get; set; }
    public bool CommandsHigh { get; set; }
    public bool UsageHints { get; set; } = true;
    public string PhoneSeat { get; set; } = "center";
    public string BerrySize { get; set; } = "large";
    public int BerrySpeed { get; set; } = 100;
    public bool BerryTeleport { get; set; }
    public string BerrySleepCorner { get; set; } = "bottom-right";
    public string BerryBubble { get; set; } = "bar";
    public string HostId { get; set; } = "urn:uuid:" + Guid.NewGuid().ToString();
    public string OpenAiApiKey { get; set; } = "";
    public string XaiApiKey { get; set; } = "";
    public string AnthropicApiKey { get; set; } = "";
    public string CerebrasApiKey { get; set; } = "";
    public string OpenRouterApiKey { get; set; } = "";
    public string OpenAiRealtimeModel { get; set; } = "gpt-realtime-2.1";
    public string OpenAiTextModel { get; set; } = "gpt-6.1-sol";
    public string XaiVoiceModel { get; set; } = "grok-voice-latest";
    public string XaiTextModel { get; set; } = "grok-4.7";
    public string AnthropicModel { get; set; } = "claude-sonnet-4-5";
    public string CerebrasModel { get; set; } = "qwen-3.8-27b";
    public string OpenRouterModel { get; set; } = "qwen/qwen3.8-27b";
    public string OpenRouterProvider { get; set; } = "modelrun";
    public string ChatGptModel { get; set; } = "gpt-6.1-sol";
    public ChatGptAccount? ChatGpt { get; set; }
    public string DesktopPassword { get; set; } = "";

    public bool ModeIsReady() => Mode switch
    {
        BrainMode.XaiLive => !string.IsNullOrWhiteSpace(XaiApiKey),
        BrainMode.OpenAiLive => !string.IsNullOrWhiteSpace(OpenAiApiKey),
        BrainMode.ChatGptPlan => ChatGpt?.CanUsePlan == true,
        BrainMode.Anthropic => !string.IsNullOrWhiteSpace(AnthropicApiKey),
        BrainMode.Cerebras => !string.IsNullOrWhiteSpace(CerebrasApiKey),
        BrainMode.OpenRouter => !string.IsNullOrWhiteSpace(OpenRouterApiKey),
        _ => false,
    };

    public bool RunsRealtimeOnPhone => Mode is BrainMode.XaiLive or BrainMode.OpenAiLive;
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bluey", "settings.bin");
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (!File.Exists(_path)) settings = new AppSettings();
            else
            {
                var protectedBytes = File.ReadAllBytes(_path);
                var json = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            settings = new AppSettings();
        }
        if (string.IsNullOrWhiteSpace(settings.CerebrasApiKey))
        {
            var key = DotEnv.Get("CEREBRAS_API_KEY");
            if (!string.IsNullOrWhiteSpace(key)) settings.CerebrasApiKey = key;
        }
        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            var key = DotEnv.Get("OPENROUTER_API_KEY");
            if (!string.IsNullOrWhiteSpace(key)) settings.OpenRouterApiKey = key;
        }
        settings.OpenRouterProvider = OpenRouterProviders.Normalize(settings.OpenRouterProvider);
        settings.OpenRouterModel = OpenRouterModels.Normalize(settings.OpenRouterModel);
        settings.BerrySize = settings.BerrySize is "small" or "medium" or "large" ? settings.BerrySize : "large";
        settings.BerrySpeed = settings.BerrySpeed is 150 or 200 ? settings.BerrySpeed : 100;
        settings.BerrySleepCorner = settings.BerrySleepCorner is "bottom-left" or "top-left" or "top-right" or "bottom-right"
            ? settings.BerrySleepCorner : "bottom-right";
        settings.BerryBubble = settings.BerryBubble == "comic" ? "comic" : "bar";
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
        var protectedBytes = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, _path, true);
    }
}
