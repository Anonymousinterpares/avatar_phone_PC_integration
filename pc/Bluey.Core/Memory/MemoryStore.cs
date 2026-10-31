using System.Text.Json;

namespace Bluey.Memory;

public sealed class MemoryBackup
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public int Count { get; init; }
}

public sealed class MemoryStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _folder;
    private bool _tooNew;
    private bool _unreadable;

    public MemoryStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bluey", "memory");
    }

    public string Folder => _folder;
    public bool TooNew => _tooNew;
    public bool Unreadable => _unreadable;

    public MemoryBook Load()
    {
        lock (_gate) return Read();
    }

    /// <summary>
    /// Looks for this Windows user's one memory file. Loads it when it is already there.
    /// If it is missing, copies the newest readable backup into that place.
    /// Does not create an empty file.
    /// </summary>
    public MemoryBook Open()
    {
        lock (_gate)
        {
            if (File.Exists(FilePath())) return Read();
            var backup = NewestBackupPath();
            if (backup == null)
            {
                _tooNew = false;
                _unreadable = false;
                return new MemoryBook();
            }
            Directory.CreateDirectory(_folder);
            File.Copy(backup, FilePath(), false);
            return Read();
        }
    }

    public bool Save(MemoryBook book)
    {
        lock (_gate)
        {
            RefreshFlags();
            if (Blocked()) return false;
            if (book.Version > UserMemory.FormatVersion) return false;
            book.Version = UserMemory.FormatVersion;
            Write(book);
            return true;
        }
    }

    public string? Backup()
    {
        lock (_gate) return Copy();
    }

    public void ForgetEverything()
    {
        lock (_gate)
        {
            var book = Read();
            if (Blocked()) return;
            Copy();
            UserMemory.ForgetEverything(book);
            Write(book);
        }
    }

    public IReadOnlyList<MemoryBackup> Backups()
    {
        lock (_gate) return List();
    }

    public string Restore(string name, bool add)
    {
        lock (_gate)
        {
            var file = SafeBackupPath(name);
            return Import(File.ReadAllText(file), add);
        }
    }

    public string RestorePath(string path, bool add)
    {
        lock (_gate) return Import(File.ReadAllText(path), add);
    }

    public string Snapshot(bool learn, bool tell, bool quiet, string? note = null)
    {
        lock (_gate)
        {
            var book = _tooNew || _unreadable ? new MemoryBook() : Read();
            var items = new List<object>();
            foreach (var habit in book.Habits)
            {
                items.Add(new
                {
                    id = habit.Id,
                    kind = "habit",
                    text = "When you ask to " + habit.When + ", " + habit.Do + ".",
                    use = habit.Strength,
                    source = SourceLabel(habit.Source),
                });
            }
            foreach (var like in book.Likes)
            {
                items.Add(new
                {
                    id = like.Id,
                    kind = "like",
                    text = like.Text,
                    use = like.Use,
                    source = SourceLabel(like.Source),
                });
            }
            foreach (var topic in book.Never)
            {
                items.Add(new
                {
                    id = UserMemory.IdFor("never", topic),
                    kind = "never",
                    text = "Don't remember " + topic + ".",
                    use = "never",
                    source = "You said it",
                });
            }
            foreach (var heard in book.Observations)
            {
                var times = heard.Count == 1 ? "1 time" : heard.Count + " times";
                items.Add(new
                {
                    id = "heard:" + heard.Phrase,
                    kind = "heard",
                    text = "Heard \"" + heard.Phrase + "\" " + times + ". Not a habit yet.",
                    use = "heard",
                    source = "It happened " + times,
                });
            }
            return JsonSerializer.Serialize(new
            {
                learn,
                tell,
                quiet,
                tooNew = _tooNew,
                unreadable = _unreadable,
                found = File.Exists(FilePath()),
                note = note ?? "",
                items,
                backups = List().Select(copy => new { name = copy.Name, label = copy.Label, count = copy.Count }),
            }, Json);
        }
    }

    private string Import(string json, bool add)
    {
        RefreshFlags();
        var incoming = Parse(json);
        if (incoming.Version > UserMemory.FormatVersion)
            throw new InvalidDataException("That copy is from a newer Bluey. It was not loaded.");
        if (Blocked())
            throw new InvalidDataException("The current memory file could not be replaced.");
        Copy();
        if (add)
        {
            var book = Read();
            UserMemory.Merge(book, incoming);
            Write(book);
            return "Added to what he remembers.";
        }
        incoming.Version = UserMemory.FormatVersion;
        Write(incoming);
        return "Replaced what he remembers.";
    }

    private MemoryBook Read()
    {
        var path = FilePath();
        if (!File.Exists(path))
        {
            _tooNew = false;
            _unreadable = false;
            return new MemoryBook();
        }
        try
        {
            var book = Parse(File.ReadAllText(path));
            if (book.Version > UserMemory.FormatVersion)
            {
                _tooNew = true;
                _unreadable = false;
                return new MemoryBook { Version = book.Version };
            }
            _tooNew = false;
            _unreadable = false;
            Daily(path);
            return book;
        }
        catch (JsonException)
        {
            _tooNew = false;
            _unreadable = true;
            return new MemoryBook();
        }
    }

    private static MemoryBook Parse(string json)
    {
        var book = JsonSerializer.Deserialize<MemoryBook>(json, Json) ?? new MemoryBook();
        book.Habits ??= new();
        book.Likes ??= new();
        book.Never ??= new();
        book.Observations ??= new();
        if (book.Version <= 0) book.Version = UserMemory.FormatVersion;
        return book;
    }

    private string? NewestBackupPath()
    {
        var backups = Path.Combine(_folder, "backups");
        if (!Directory.Exists(backups)) return null;
        foreach (var file in new DirectoryInfo(backups).GetFiles("*.json").OrderByDescending(item => item.Name))
        {
            try
            {
                var book = Parse(File.ReadAllText(file.FullName));
                if (book.Version > UserMemory.FormatVersion) continue;
                return file.FullName;
            }
            catch (JsonException)
            {
                continue;
            }
        }
        return null;
    }

    private void Write(MemoryBook book)
    {
        var path = FilePath();
        var empty = book.Habits.Count + book.Likes.Count + book.Never.Count + book.Observations.Count == 0;
        if (!File.Exists(path) && empty) return;
        Directory.CreateDirectory(_folder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(book, Json));
        File.Move(temp, path, true);
        _tooNew = false;
        _unreadable = false;
    }

    private string? Copy()
    {
        var path = FilePath();
        if (!File.Exists(path)) return null;
        var backups = Path.Combine(_folder, "backups");
        Directory.CreateDirectory(backups);
        var name = DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".json";
        var dest = Path.Combine(backups, name);
        File.Copy(path, dest, true);
        Trim(backups);
        return dest;
    }

    private void Daily(string path)
    {
        var backups = Path.Combine(_folder, "backups");
        Directory.CreateDirectory(backups);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (Directory.EnumerateFiles(backups, today + "*.json").Any()) return;
        File.Copy(path, Path.Combine(backups, today + ".json"), true);
        Trim(backups);
    }

    private static void Trim(string backups)
    {
        var files = new DirectoryInfo(backups).GetFiles("*.json").OrderByDescending(file => file.Name).Skip(14).ToList();
        foreach (var file in files)
        {
            try { file.Delete(); } catch (IOException) { }
        }
    }

    private List<MemoryBackup> List()
    {
        var backups = Path.Combine(_folder, "backups");
        if (!Directory.Exists(backups)) return new();
        var list = new List<MemoryBackup>();
        foreach (var file in new DirectoryInfo(backups).GetFiles("*.json").OrderByDescending(item => item.Name))
        {
            var count = 0;
            try
            {
                var book = Parse(File.ReadAllText(file.FullName));
                if (book.Version > UserMemory.FormatVersion) continue;
                count = book.Habits.Count + book.Likes.Count + book.Never.Count;
            }
            catch (JsonException)
            {
                continue;
            }
            list.Add(new MemoryBackup
            {
                Name = file.Name,
                Label = file.Name.Replace(".json", "").Replace('-', ' '),
                Count = count,
            });
        }
        return list;
    }

    private string SafeBackupPath(string name)
    {
        var file = Path.GetFileName(name);
        if (!string.Equals(file, name, StringComparison.Ordinal) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("That backup was not found.");
        var path = Path.Combine(_folder, "backups", file);
        if (!File.Exists(path)) throw new InvalidDataException("That backup was not found.");
        return path;
    }

    private void RefreshFlags()
    {
        var path = FilePath();
        if (!File.Exists(path))
        {
            _tooNew = false;
            _unreadable = false;
            return;
        }
        try
        {
            var book = Parse(File.ReadAllText(path));
            _tooNew = book.Version > UserMemory.FormatVersion;
            _unreadable = false;
        }
        catch (JsonException)
        {
            _tooNew = false;
            _unreadable = true;
        }
    }

    private bool Blocked() => _tooNew || _unreadable;

    private string FilePath() => Path.Combine(_folder, "memory.json");

    private static string SourceLabel(string source) => source == "repeated" ? "It happened 3 times" : "You said it";
}
