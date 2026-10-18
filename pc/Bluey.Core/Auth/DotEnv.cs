namespace Bluey.Auth;

public static class DotEnv
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _file;

    public static string? Get(string name)
    {
        var fromProcess = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(fromProcess)) return fromProcess.Trim();
        var values = FileValues();
        return values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static Dictionary<string, string> FileValues()
    {
        lock (Gate)
        {
            if (_file != null) return _file;
            foreach (var path in Candidates())
            {
                if (!File.Exists(path)) continue;
                _file = Parse(File.ReadAllText(path));
                return _file;
            }
            _file = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return _file;
        }
    }

    public static Dictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) line = line[7..].Trim();
            var split = line.IndexOf('=');
            if (split <= 0) continue;
            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            values[key] = value;
        }
        return values;
    }

    private static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var path = Path.Combine(dir.FullName, ".env");
                if (seen.Add(path)) yield return path;
                dir = dir.Parent;
            }
        }
    }
}
