using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Bluey.Host;

/// <summary>Pixel-art loops from art/out/anims, drawn crisp on the blueberry.</summary>
public sealed class BerryFilm
{
    public sealed class Clip
    {
        public required string Name { get; init; }
        public required (BitmapSource Image, int Ms)[] Frames { get; init; }
    }

    public IReadOnlyDictionary<string, Clip> Clips { get; }

    private BerryFilm(Dictionary<string, Clip> clips) => Clips = clips;

    public static BerryFilm? TryLoad()
    {
        var dir = FindAnims();
        if (dir == null) return null;
        var clips = new Dictionary<string, Clip>(StringComparer.OrdinalIgnoreCase);
        foreach (var jsonPath in Directory.EnumerateFiles(dir, "*.json"))
        {
            var png = Path.ChangeExtension(jsonPath, ".png");
            if (!File.Exists(png)) continue;
            try
            {
                var clip = Load(Path.GetFileNameWithoutExtension(jsonPath), jsonPath, png);
                if (clip.Frames.Length > 0) clips[clip.Name] = clip;
            }
            catch
            {
                // One broken strip should not hide the rest.
            }
        }
        return clips.Count == 0 ? null : new BerryFilm(clips);
    }

    private static Clip Load(string name, string jsonPath, string pngPath)
    {
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.UriSource = new Uri(pngPath);
        sheet.EndInit();
        sheet.Freeze();
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var frames = new List<(int Index, BitmapSource Image, int Ms)>();
        foreach (var prop in doc.RootElement.GetProperty("frames").EnumerateObject())
        {
            var box = prop.Value.GetProperty("frame");
            var rect = new Int32Rect(box.GetProperty("x").GetInt32(), box.GetProperty("y").GetInt32(), box.GetProperty("w").GetInt32(), box.GetProperty("h").GetInt32());
            var crop = new CroppedBitmap(sheet, rect);
            crop.Freeze();
            var ms = prop.Value.TryGetProperty("duration", out var duration) ? duration.GetInt32() : 125;
            frames.Add((FrameIndex(prop.Name), crop, Math.Max(40, ms)));
        }
        frames.Sort((a, b) => a.Index.CompareTo(b.Index));
        return new Clip { Name = name, Frames = frames.Select(f => (f.Image, f.Ms)).ToArray() };
    }

    private static int FrameIndex(string name)
    {
        var cut = name.LastIndexOf('_');
        return cut >= 0 && int.TryParse(name[(cut + 1)..], out var n) ? n : 0;
    }

    private static string? FindAnims()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "art", "out", "anims");
                if (File.Exists(Path.Combine(candidate, "idle_breathe.png"))) return candidate;
            }
        }
        return null;
    }
}
