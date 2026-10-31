using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Bluey.Commands;

public enum CommandChoice
{
    No,
    Once,
    Always,
}

public sealed class AllowedCommand
{
    public string Program { get; set; } = "";
    public List<string> Args { get; set; } = new();
    public string Folder { get; set; } = "";
}

public sealed class CommandFile
{
    public string Folder { get; set; } = "";
    public List<AllowedCommand> Allow { get; set; } = new();
}

public sealed class CommandProposal
{
    public string Program { get; init; } = "";
    public string ProgramName { get; init; } = "";
    public string[] Args { get; init; } = Array.Empty<string>();
    public string Folder { get; init; } = "";
    public string Display { get; init; } = "";
}

public sealed class CommandDecision
{
    public bool RunNow { get; init; }
    public bool Ask { get; init; }
    public string Reason { get; init; } = "";
    public CommandProposal? Proposal { get; init; }

    public static CommandDecision Refuse(string reason) => new() { Reason = reason };
    public static CommandDecision NeedsAsk(CommandProposal proposal) => new() { Ask = true, Proposal = proposal };
    public static CommandDecision Allowed(CommandProposal proposal) => new() { RunNow = true, Proposal = proposal };
}

public sealed class CommandStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _folder;
    private readonly object _gate = new();

    public CommandStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bluey", "commands");
    }

    public string Folder => _folder;

    public CommandFile Load()
    {
        lock (_gate)
        {
            var path = Path.Combine(_folder, "allow.json");
            if (!File.Exists(path)) return new CommandFile();
            try
            {
                var file = JsonSerializer.Deserialize<CommandFile>(File.ReadAllText(path), Json) ?? new CommandFile();
                file.Allow ??= new();
                return file;
            }
            catch (JsonException)
            {
                return new CommandFile();
            }
        }
    }

    public string? SetFolder(string folder)
    {
        if (!CommandRules.FolderIsUsable(folder, out var reason)) return reason;
        lock (_gate)
        {
            var file = ReadUnlocked();
            file.Folder = Path.GetFullPath(folder);
            WriteUnlocked(file);
            return null;
        }
    }

    public void Allow(CommandProposal proposal)
    {
        lock (_gate)
        {
            var file = ReadUnlocked();
            if (file.Allow.Any(item => CommandRules.Same(item, proposal))) return;
            file.Allow.Add(new AllowedCommand
            {
                Program = proposal.ProgramName,
                Args = proposal.Args.ToList(),
                Folder = proposal.Folder,
            });
            WriteUnlocked(file);
        }
    }

    public void RemoveAt(int index)
    {
        lock (_gate)
        {
            var file = ReadUnlocked();
            if (index < 0 || index >= file.Allow.Count) return;
            file.Allow.RemoveAt(index);
            WriteUnlocked(file);
        }
    }

    public void Log(string line)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_folder);
            var flat = line.Replace("\r", " ").Replace("\n", " ");
            if (flat.Length > 500) flat = flat[..500];
            File.AppendAllText(Path.Combine(_folder, "commands.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + flat + Environment.NewLine);
        }
    }

    private CommandFile ReadUnlocked()
    {
        var path = Path.Combine(_folder, "allow.json");
        if (!File.Exists(path)) return new CommandFile();
        try
        {
            var file = JsonSerializer.Deserialize<CommandFile>(File.ReadAllText(path), Json) ?? new CommandFile();
            file.Allow ??= new();
            return file;
        }
        catch (JsonException)
        {
            return new CommandFile();
        }
    }

    private void WriteUnlocked(CommandFile file)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "allow.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, path, true);
    }
}

public static class CommandRules
{
    public const int TimeoutSeconds = 20;
    public const int OutputCap = 8000;

    private static readonly string[] Shells =
    {
        "cmd", "powershell", "pwsh", "bash", "wsl", "wscript", "cscript", "mshta",
        "reg", "regedit", "diskpart", "format", "cipher", "bcdedit", "shutdown",
        "sc", "net", "bitsadmin", "certutil", "rundll32", "msiexec", "conhost", "wt", "powershell_ise",
    };

    private static readonly string[] Secrets =
    {
        "settings.bin", "memory.json", ".env", "id_rsa", ".ssh", "ntuser.dat", "login data", "commands.log", "allow.json",
    };

    public static bool FolderIsUsable(string folder, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            reason = "Choose a command folder in the tray first.";
            return false;
        }
        try
        {
            var full = Path.GetFullPath(folder);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                reason = "Pick a folder inside a drive, not the whole drive.";
                return false;
            }
        }
        catch (Exception)
        {
            reason = "That folder cannot be used.";
            return false;
        }
        if (HasSecret(folder))
        {
            reason = "That folder is not available for commands.";
            return false;
        }
        return true;
    }

    public static CommandDecision Decide(CommandFile file, string argumentsJson, bool enabled, bool noTouch, bool stopped, bool helper)
    {
        if (!enabled) return CommandDecision.Refuse("Let him run commands is off.");
        if (helper) return CommandDecision.Refuse("A help session cannot run commands.");
        if (noTouch) return CommandDecision.Refuse("No-touch mode is on, so I can't do that.");
        if (stopped) return CommandDecision.Refuse("Stopped.");
        if (!FolderIsUsable(file.Folder, out var folderReason)) return CommandDecision.Refuse(folderReason);
        if (!TryRead(argumentsJson, out var program, out var args, out var readReason)) return CommandDecision.Refuse(readReason);
        if (IsShell(program) || HasSecret(program) || program.Contains("..", StringComparison.Ordinal))
            return CommandDecision.Refuse("I won't run a shell or a command that reaches into private files.");
        foreach (var arg in args)
        {
            if (arg.Contains("..", StringComparison.Ordinal) || HasSecret(arg) || Escapes(arg, file.Folder))
                return CommandDecision.Refuse("That stays outside the command folder, so I won't run it.");
        }
        var proposal = new CommandProposal
        {
            Program = program,
            ProgramName = ProgramName(program),
            Args = args,
            Folder = Path.GetFullPath(file.Folder),
            Display = Display(program, args),
        };
        if (file.Allow.Any(item => Same(item, proposal))) return CommandDecision.Allowed(proposal);
        return CommandDecision.NeedsAsk(proposal);
    }

    public static bool Same(AllowedCommand item, CommandProposal proposal)
    {
        if (!string.Equals(ProgramName(item.Program), proposal.ProgramName, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(Path.GetFullPath(item.Folder), proposal.Folder, StringComparison.OrdinalIgnoreCase)) return false;
        if (item.Args.Count != proposal.Args.Length) return false;
        for (var i = 0; i < proposal.Args.Length; i++)
            if (!string.Equals(item.Args[i], proposal.Args[i], StringComparison.Ordinal)) return false;
        return true;
    }

    public static string? Resolve(string program)
    {
        var name = ProgramName(program);
        if (IsShell(name)) return null;
        if (program.Contains('\\') || program.Contains('/'))
        {
            if (!File.Exists(program) || HasSecret(program)) return null;
            return Path.GetFullPath(program);
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), name);
            if (File.Exists(full) && !HasSecret(full)) return Path.GetFullPath(full);
        }
        return null;
    }

    public static string ProgramName(string program)
    {
        var name = Path.GetFileName(program.Trim().Trim('"'));
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return name;
        return name + ".exe";
    }

    private static bool TryRead(string json, out string program, out string[] args, out string reason)
    {
        program = "";
        args = Array.Empty<string>();
        reason = "";
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("program", out var programValue) || programValue.ValueKind != JsonValueKind.String)
            {
                reason = "Name the program, and pass arguments as a list.";
                return false;
            }
            program = (programValue.GetString() ?? "").Trim();
            if (program.Length == 0 || program.Length > 240)
            {
                reason = "Name the program, and pass arguments as a list.";
                return false;
            }
            if (!root.TryGetProperty("args", out var argsValue))
            {
                args = Array.Empty<string>();
                return true;
            }
            if (argsValue.ValueKind == JsonValueKind.String)
            {
                reason = "Pass arguments as a list, not one shell line.";
                return false;
            }
            if (argsValue.ValueKind != JsonValueKind.Array)
            {
                reason = "Pass arguments as a list.";
                return false;
            }
            var list = new List<string>();
            foreach (var item in argsValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    reason = "Pass arguments as a list of words.";
                    return false;
                }
                var word = item.GetString() ?? "";
                if (word.Length > 180)
                {
                    reason = "That argument is too long.";
                    return false;
                }
                list.Add(word);
            }
            if (list.Count > 16)
            {
                reason = "That is too many arguments.";
                return false;
            }
            args = list.ToArray();
            return true;
        }
        catch (JsonException)
        {
            reason = "Name the program, and pass arguments as a list.";
            return false;
        }
    }

    private static bool IsShell(string program)
    {
        var name = ProgramName(program);
        return Shells.Any(shell => string.Equals(ProgramName(shell), name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasSecret(string text)
    {
        foreach (var secret in Secrets)
            if (text.Contains(secret, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool Escapes(string arg, string folder)
    {
        var absolute = arg.StartsWith("\\\\", StringComparison.Ordinal)
            || (arg.Length >= 3 && char.IsLetter(arg[0]) && arg[1] == ':' && (arg[2] == '\\' || arg[2] == '/'));
        if (!absolute) return false;
        try
        {
            var full = Path.GetFullPath(arg);
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return !full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static string Display(string program, string[] args)
    {
        var parts = new List<string> { Path.GetFileName(program) };
        foreach (var arg in args)
            parts.Add(arg.Contains(' ') ? "\"" + arg + "\"" : arg);
        return string.Join(' ', parts);
    }
}

public sealed class CommandRunner
{
    private readonly CommandStore _store;
    private readonly object _gate = new();
    private Process? _process;

    public CommandRunner(CommandStore store) => _store = store;

    private int _killed;

    public void Kill()
    {
        Interlocked.Exchange(ref _killed, 1);
        Process? process;
        lock (_gate) process = _process;
        try { process?.Kill(entireProcessTree: true); } catch (Exception) { }
    }

    public string Run(CommandProposal proposal, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _killed, 0);
        var exe = CommandRules.Resolve(proposal.Program);
        if (exe == null)
        {
            _store.Log(proposal.Display + "  not found");
            return "I can't find " + proposal.ProgramName + ".";
        }
        var start = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = proposal.Folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in proposal.Args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) => Append(output, eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Append(output, eventArgs.Data);
        try
        {
            if (!process.Start())
            {
                _store.Log(proposal.Display + "  did not start");
                return "That program did not start.";
            }
        }
        catch (Exception ex)
        {
            _store.Log(proposal.Display + "  did not start");
            return "That program did not start. " + ex.Message;
        }
        lock (_gate) _process = process;
        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var stop = cancellationToken.Register(Kill);
            if (!process.WaitForExit(CommandRules.TimeoutSeconds * 1000))
            {
                Kill();
                try { process.WaitForExit(2000); } catch (Exception) { }
                _store.Log(proposal.Display + "  timed out");
                return "Stopped. That program ran too long.";
            }
            process.WaitForExit();
            if (Volatile.Read(ref _killed) != 0)
            {
                _store.Log(proposal.Display + "  stopped");
                return "Stopped.";
            }
            var text = output.ToString().Trim();
            if (text.Length > CommandRules.OutputCap) text = text[..CommandRules.OutputCap] + "…";
            _store.Log(proposal.Display + "  exit " + process.ExitCode + "  " + text);
            return "Exit " + process.ExitCode + (text.Length == 0 ? "" : "\n" + text);
        }
        finally
        {
            lock (_gate)
            {
                if (_process == process) _process = null;
            }
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line == null) return;
        lock (output)
        {
            if (output.Length >= CommandRules.OutputCap) return;
            if (output.Length > 0) output.Append('\n');
            var room = CommandRules.OutputCap - output.Length;
            output.Append(line.Length <= room ? line : line[..room]);
        }
    }
}
