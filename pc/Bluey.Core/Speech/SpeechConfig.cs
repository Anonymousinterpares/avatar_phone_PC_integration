using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bluey.Speech;

public sealed class SpeechConfig
{
    public const string FileName = "bluey.config.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> Languages = new(StringComparer.Ordinal)
    {
        "na", "ar", "bg", "cs", "da", "de", "el", "en", "es", "et", "fi", "fr", "hi", "hr", "hu",
        "id", "it", "ja", "ko", "lt", "lv", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "tr", "uk", "vi",
    };

    [JsonPropertyName("textToSpeech")]
    public bool TextToSpeech { get; set; }

    [JsonPropertyName("sex")]
    public string Sex { get; set; } = "male";

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "na";

    public static string NormalizeSex(string? value)
    {
        var sex = (value ?? "").Trim().ToLowerInvariant();
        return sex is "female" or "f" ? "female" : "male";
    }

    public static string NormalizeLang(string? value)
    {
        var lang = (value ?? "").Trim().ToLowerInvariant();
        return Languages.Contains(lang) ? lang : "na";
    }

    public static string? FindExisting(IEnumerable<string> roots)
    {
        foreach (var start in roots)
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            DirectoryInfo? dir;
            try
            {
                dir = new DirectoryInfo(start);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or IOException)
            {
                continue;
            }
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, FileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static string ResolvePath()
    {
        var found = FindExisting(new[] { Environment.CurrentDirectory, AppContext.BaseDirectory });
        if (found != null) return found;
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bluey", FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
        if (!File.Exists(fallback)) new SpeechConfig().Save(fallback);
        return fallback;
    }

    public static SpeechConfig Load(string path)
    {
        SpeechConfig config;
        try
        {
            if (!File.Exists(path)) config = new SpeechConfig();
            else config = JsonSerializer.Deserialize<SpeechConfig>(File.ReadAllText(path), Options) ?? new SpeechConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            config = new SpeechConfig();
        }
        config.Sex = NormalizeSex(config.Sex);
        config.Lang = NormalizeLang(config.Lang);
        return config;
    }

    public void Save(string path)
    {
        Sex = NormalizeSex(Sex);
        Lang = NormalizeLang(Lang);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, path, true);
    }
}

public sealed class SpeechConfigStore
{
    public string Path { get; }
    public SpeechConfig Current { get; }

    private SpeechConfigStore(string path, SpeechConfig current)
    {
        Path = path;
        Current = current;
    }

    public static SpeechConfigStore Open()
    {
        var path = SpeechConfig.ResolvePath();
        return new SpeechConfigStore(path, SpeechConfig.Load(path));
    }

    public void Save() => Current.Save(Path);
}

public static class SpeechText
{
    public const int MaxChars = 600;

    public static string Prepare(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= MaxChars) return collapsed;
        var slice = collapsed[..MaxChars];
        var end = Math.Max(slice.LastIndexOf(". "), Math.Max(slice.LastIndexOf("! "), slice.LastIndexOf("? ")));
        if (end >= 240) return slice[..(end + 1)];
        return slice;
    }
}

public static class SpokenSpan
{
    public const string Open = "[[spoken]]";
    public const string Close = "[[/spoken]]";

    private static readonly System.Text.RegularExpressions.Regex Span = new(
        @"\[\[spoken\]\](.*?)\[\[/spoken\]\]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex Leftover = new(
        @"\[\[/?spoken\]\]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex GenericBracket = new(
        @"^\[\[(.*?)\]\]$",
        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ActTag = new(
        @"\[\[act:([a-zA-Z0-9_]+)\]\]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static readonly HashSet<string> ActNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "wave", "greet", "hop", "cheer", "laugh", "wink", "yawn", "sneeze",
        "point", "sad", "surprise", "love", "confused", "huff", "sleep", "talk",
    };

    public static string? Extract(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in Span.Matches(text))
        {
            var spoken = match.Groups[1].Value.Trim();
            if (spoken.Length > 0) parts.Add(spoken);
        }
        if (parts.Count > 0) return string.Join(" ", parts);

        // If the model wrapped the entire output in [[words]] or [[spoken: words]] instead of [[spoken]]words[[/spoken]]
        var generic = GenericBracket.Match(text.Trim());
        if (generic.Success)
        {
            var inner = generic.Groups[1].Value.Trim();
            if (inner.StartsWith("spoken:", StringComparison.OrdinalIgnoreCase))
                inner = inner["spoken:".Length..].Trim();
            if (inner.Length > 0) return inner;
        }

        return null;
    }

    public static IReadOnlyList<string> TakeActs(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        var found = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in ActTag.Matches(text))
        {
            var name = match.Groups[1].Value.ToLowerInvariant();
            if (ActNames.Contains(name)) found.Add(name);
        }
        return found;
    }

    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var stripped = ActTag.Replace(Leftover.Replace(Span.Replace(text, match => match.Groups[1].Value), ""), "");
        var trimmed = stripped.Trim();
        var generic = GenericBracket.Match(trimmed);
        if (generic.Success)
        {
            var inner = generic.Groups[1].Value.Trim();
            if (inner.StartsWith("spoken:", StringComparison.OrdinalIgnoreCase))
                inner = inner["spoken:".Length..].Trim();
            trimmed = inner;
        }
        return trimmed;
    }
}

public interface IReplySpeaker
{
    bool Enabled { get; }
    void Speak(string text);
    void Stop();
}
