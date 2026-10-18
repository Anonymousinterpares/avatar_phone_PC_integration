namespace Bluey.Desk;

public readonly record struct PointD(double X, double Y);

public readonly record struct SizeD(double Width, double Height);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
    public double MaxY => Y + Height;
    public bool Contains(PointD p, double pad = 0) =>
        p.X >= X - pad && p.X <= X + Width + pad && p.Y >= Y - pad && p.Y <= Y + Height + pad;
}

public sealed record ScreenTarget(string Id, string Kind, string Text, RectD Rect);

public sealed record ScreenCapture(byte[] Jpeg, IReadOnlyList<ScreenTarget> Targets, string? AppName, SizeD Size);

public interface IDesk
{
    bool ComputerControl { get; }
    SizeD ScreenSize { get; }
    PointD Mouse { get; }
    Task<ScreenCapture> CaptureAsync(CancellationToken cancellationToken);
    Task ClickAsync(PointD point, bool right, int count, CancellationToken cancellationToken);
    Task TypeAsync(string text, Func<string, Task> onChunk, CancellationToken cancellationToken);
    Task PressAsync(string keys, CancellationToken cancellationToken);
    Task ScrollAsync(PointD point, int dx, int dy, CancellationToken cancellationToken);
    Task DragAsync(PointD from, PointD to, CancellationToken cancellationToken);
    Task<string> OpenAppAsync(string name, CancellationToken cancellationToken);
    string OpenUrl(string url);
    bool FocusedFieldIsPassword();
    void Warp(PointD point);
}

public static class Grid
{
    public static PointD ToPixel(double x, double y, SizeD size) => new(
        Math.Clamp(x, 0, 1000) / 1000 * size.Width,
        Math.Clamp(y, 0, 1000) / 1000 * size.Height);

    public static (int X, int Y) ToGrid(PointD point, SizeD size)
    {
        if (size.Width <= 0 || size.Height <= 0) return (0, 0);
        var x = (int)(Math.Clamp(point.X / size.Width, 0, 1) * 1000);
        var y = (int)(Math.Clamp(point.Y / size.Height, 0, 1) * 1000);
        return (x, y);
    }

    public static string Describe(ScreenCapture shot, PointD mouse)
    {
        string At(RectD r)
        {
            var (x, y) = ToGrid(new PointD(r.MidX, r.MidY), shot.Size);
            return $"@{x},{y}";
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(shot.AppName)) lines.Add($"Frontmost app: {shot.AppName}");
        var controls = shot.Targets.Where(t => t.Id.StartsWith('C')).ToList();
        if (controls.Count > 0)
        {
            lines.Add("Controls (click these by id):");
            lines.AddRange(controls.Select(c =>
                $"{c.Id} {c.Kind} {At(c.Rect)}" + (string.IsNullOrEmpty(c.Text) ? "" : $" \"{c.Text}\"")));
        }
        lines.Add("Text on screen (L = line, W = word, @x,y on a 0-1000 grid):");
        var text = shot.Targets.Where(t => t.Id.StartsWith('L') || t.Id.StartsWith('W')).ToList();
        if (text.Count == 0) lines.Add("(no text found)");
        foreach (var line in text.Where(t => t.Id.StartsWith('L')))
        {
            var words = text.Where(w => w.Id.StartsWith('W') && line.Rect.Contains(new PointD(w.Rect.MidX, w.Rect.MidY))).ToList();
            var wordText = words.Count > 1 ? " | " + string.Join(" ", words.Select(w => $"{w.Id}={w.Text}")) : "";
            lines.Add($"{line.Id} {At(line.Rect)} \"{line.Text}\"{wordText}");
        }
        var (mx, my) = ToGrid(mouse, shot.Size);
        var under = Nearest(shot, mouse);
        var where = $"The user's mouse pointer is at @{mx},{my}";
        if (under != null) where += $", on {under.Id} \"{under.Text}\"";
        lines.Add(where + ". When they say \"this\", \"that\" or \"here\", they mean what's at their mouse pointer.");
        return string.Join('\n', lines);
    }

    public static ScreenTarget? ById(ScreenCapture shot, string id)
    {
        var key = id.Trim().ToUpperInvariant();
        return shot.Targets.FirstOrDefault(t => t.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static ScreenTarget? Nearest(ScreenCapture shot, PointD point)
    {
        ScreenTarget? best = null;
        var bestArea = double.MaxValue;
        foreach (var target in shot.Targets)
        {
            if (!target.Rect.Contains(point, 6)) continue;
            var area = target.Rect.Width * target.Rect.Height;
            if (area < bestArea)
            {
                best = target;
                bestArea = area;
            }
        }
        if (best != null) return best;
        var nearest = double.MaxValue;
        foreach (var target in shot.Targets)
        {
            var dx = Math.Max(target.Rect.X - point.X, Math.Max(0, point.X - (target.Rect.X + target.Rect.Width)));
            var dy = Math.Max(target.Rect.Y - point.Y, Math.Max(0, point.Y - (target.Rect.Y + target.Rect.Height)));
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 60 && distance < nearest)
            {
                nearest = distance;
                best = target;
            }
        }
        return best;
    }
}

public static class ActionGuard
{
    public static readonly HashSet<string> ActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "click", "type_text", "press_keys", "scroll", "drag", "open_app", "open_url",
    };

    private static readonly HashSet<string> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        "win+l", "meta+l", "super+l", "ctrl+alt+delete", "ctrl+alt+del", "control+alt+delete",
    };

    public static string? Refusal(string name, bool computerControl, bool stopped, string argumentsJson)
    {
        if (!ActionNames.Contains(name)) return null;
        if (!computerControl)
            return "The user has turned off computer control. You can still look and point.";
        if (stopped)
            return "The user pressed stop. Don't do anything else until they ask again.";
        if (name.Equals("press_keys", StringComparison.OrdinalIgnoreCase))
        {
            var keys = ReadString(argumentsJson, "keys");
            var normalized = keys.ToLowerInvariant().Replace(" ", "").Replace("windows", "win").Replace("meta", "win");
            if (Refused.Contains(normalized))
                return "I won't press that one (it locks the computer). Ask the user to do it.";
        }
        return null;
    }

    public static string ReadString(string json, string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                return value.GetString() ?? "";
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
        return "";
    }

    public static double? ReadNumber(string json, string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (!doc.RootElement.TryGetProperty(key, out var value)) return null;
            if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
            if (value.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        return null;
    }

    public static bool ReadBool(string json, string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True) return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        return false;
    }
}
