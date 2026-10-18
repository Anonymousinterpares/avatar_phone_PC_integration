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
    public string PhoneSeat { get; set; } = "center";
    public string HostId { get; set; } = "urn:uuid:" + Guid.NewGuid().ToString();
    public string OpenAiApiKey { get; set; } = "";
    public string XaiApiKey { get; set; } = "";
    public string AnthropicApiKey { get; set; } = "";
    public string CerebrasApiKey { get; set; } = "";
    public string OpenAiRealtimeModel { get; set; } = "gpt-realtime-2.1";
    public string OpenAiTextModel { get; set; } = "gpt-6.1-sol";
    public string XaiVoiceModel { get; set; } = "grok-voice-latest";
    public string XaiTextModel { get; set; } = "grok-4.7";
    public string AnthropicModel { get; set; } = "claude-sonnet-4-5";
    public string CerebrasModel { get; set; } = "qwen-3.8-27b";
    public string ChatGptModel { get; set; } = "gpt-6.1-sol";
    public ChatGptAccount? ChatGpt { get; set; }

    public bool ModeIsReady() => Mode switch
    {
        BrainMode.XaiLive => !string.IsNullOrWhiteSpace(XaiApiKey),
        BrainMode.OpenAiLive => !string.IsNullOrWhiteSpace(OpenAiApiKey),
        BrainMode.ChatGptPlan => ChatGpt?.CanUsePlan == true,
        BrainMode.Anthropic => !string.IsNullOrWhiteSpace(AnthropicApiKey),
        BrainMode.Cerebras => !string.IsNullOrWhiteSpace(CerebrasApiKey),
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
