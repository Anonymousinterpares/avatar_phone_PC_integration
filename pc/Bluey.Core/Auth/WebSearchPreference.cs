using System.Text.Json;

namespace Bluey.Auth;

/// <summary>
/// Web search provider. Until the user picks one and saves, it follows the chat mode.
/// The file is plain JSON in the Bluey app-data folder.
/// </summary>
public static class WebSearchPreference
{
    public const string FileName = "web-search.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Bluey", FileName);

    public static bool IsPinned(string? path = null) => Read(path ?? DefaultPath).Pinned;

    public static BrainMode Resolve(BrainMode chat, string? path = null)
    {
        var saved = Read(path ?? DefaultPath);
        if (saved.Pinned && Enum.TryParse<BrainMode>(saved.Provider, out var provider))
            return provider;
        return chat;
    }

    public static void Save(bool pinned, BrainMode provider, string? path = null)
    {
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var json = JsonSerializer.Serialize(new FileShape { Pinned = pinned, Provider = provider.ToString() }, Options);
        File.WriteAllText(file, json);
    }

    public static int Index(BrainMode mode) => mode switch
    {
        BrainMode.OpenAiLive => 1,
        BrainMode.ChatGptPlan => 2,
        BrainMode.Anthropic => 3,
        BrainMode.Cerebras => 4,
        BrainMode.OpenRouter => 5,
        _ => 0,
    };

    public static BrainMode FromIndex(int index) => index switch
    {
        1 => BrainMode.OpenAiLive,
        2 => BrainMode.ChatGptPlan,
        3 => BrainMode.Anthropic,
        4 => BrainMode.Cerebras,
        5 => BrainMode.OpenRouter,
        _ => BrainMode.XaiLive,
    };

    private static FileShape Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return new FileShape();
            return JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), Options) ?? new FileShape();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new FileShape();
        }
    }

    private sealed class FileShape
    {
        public bool Pinned { get; set; }
        public string Provider { get; set; } = "";
    }
}
