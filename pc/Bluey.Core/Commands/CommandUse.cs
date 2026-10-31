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
    public string Risk { get; init; } = "Low";
    public string RiskNote { get; init; } = "";
}

public enum CommandRisk
{
    Low,
    Medium,
    High,
    Forbidden,
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

    private static readonly string[] ForbiddenPrograms =
    {
        "cmd", "powershell", "pwsh", "powershell_ise", "bash", "sh", "wsl", "wscript", "cscript", "mshta",
        "conhost", "wt", "cmdkey",
        "format", "diskpart", "cipher", "bcdedit", "bcdboot", "bootsect", "bootrec", "reagentc",
        "vssadmin", "wbadmin", "sdelete", "fsutil", "dism",
        "reg", "regedit", "regini", "rundll32", "regsvr32", "certutil", "bitsadmin", "msiexec", "wmic", "msbuild",
        "shutdown", "sc", "net", "net1", "netsh", "takeown", "icacls", "cacls",
        "del", "erase", "rd", "rmdir", "rm",
    };

    private static readonly string[] HighPrograms =
    {
        "taskkill", "tskill", "schtasks", "attrib", "subst", "mountvol", "sfc", "chkdsk",
        "python", "python3", "py", "pyw", "node", "npm", "npx", "pip", "pip3",
        "ruby", "perl", "php", "winget", "choco", "scoop", "dotnet", "cargo", "java", "javac",
    };

    private static readonly string[] LowPrograms =
    {
        "where", "whoami", "hostname", "ipconfig", "ping", "systeminfo", "tasklist", "driverquery",
        "getmac", "tracert", "nslookup", "arp", "netstat", "pathping",
    };

    private static readonly string[] CodeExecPrograms = { "python", "python3", "py", "pyw", "node", "ruby", "perl", "php" };

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

    public static CommandDecision Decide(CommandFile file, string argumentsJson, bool enabled, bool medium, bool high, bool noTouch, bool stopped, bool helper)
    {
        if (!enabled) return CommandDecision.Refuse("Let him run commands is off.");
        if (helper) return CommandDecision.Refuse("A help session cannot run commands.");
        if (noTouch) return CommandDecision.Refuse("No-touch mode is on, so I can't do that.");
        if (stopped) return CommandDecision.Refuse("Stopped.");
        if (!TryRead(argumentsJson, out var program, out var args, out var folderRequest, out var readReason)) return CommandDecision.Refuse(readReason);
        var risk = Classify(program, args);
        if (risk == CommandRisk.Forbidden) return CommandDecision.Refuse("That command is forbidden.");
        if (!TryFolder(folderRequest, out var folder, out var folderReason)) return CommandDecision.Refuse(folderReason);
        if (risk == CommandRisk.High && !high) return CommandDecision.Refuse("That is a high-risk command, and high-risk commands are off.");
        if (risk == CommandRisk.Medium && !medium) return CommandDecision.Refuse("That is a medium-risk command, and medium-risk commands are off.");
        var proposal = new CommandProposal
        {
            Program = program,
            ProgramName = ProgramName(program),
            Args = args,
            Folder = folder,
            Display = Display(program, args),
            Risk = risk.ToString(),
            RiskNote = RiskNote(risk),
        };
        if (file.Allow.Any(item => Same(item, proposal))) return CommandDecision.Allowed(proposal);
        return CommandDecision.NeedsAsk(proposal);
    }

    public static CommandRisk Classify(string program, string[] args)
    {
        if (IsForbiddenProgram(program) || HasSecret(program) || program.Contains("..", StringComparison.Ordinal))
            return CommandRisk.Forbidden;
        foreach (var arg in args)
        {
            if (arg.Contains("..", StringComparison.Ordinal) || HasSecret(arg) || IsDriveRoot(arg))
                return CommandRisk.Forbidden;
        }
        if (HasCodeExec(program, args) || IsGitDestructive(program, args) || HasForbiddenToken(args))
            return CommandRisk.Forbidden;
        var risk = BaseRisk(program, args);
        return Raise(risk, args);
    }

    public static string RiskNote(CommandRisk risk) => risk switch
    {
        CommandRisk.High => "High risk. This can install software, stop a program, or make a change that is hard to undo.",
        CommandRisk.Medium => "Medium risk. This can change files or use the network.",
        _ => "Low risk. This looks things up or makes no lasting change.",
    };

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
        if (IsForbiddenProgram(name)) return null;
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

    private static bool TryRead(string json, out string program, out string[] args, out string? folder, out string reason)
    {
        program = "";
        args = Array.Empty<string>();
        folder = null;
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
            if (root.TryGetProperty("folder", out var folderValue) && folderValue.ValueKind == JsonValueKind.String)
                folder = folderValue.GetString();
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

    private static bool IsForbiddenProgram(string program)
    {
        return ForbiddenPrograms.Any(name => IsName(program, name));
    }

    private static bool IsName(string program, string expected)
    {
        return string.Equals(ProgramName(program), ProgramName(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static CommandRisk BaseRisk(string program, string[] args)
    {
        if (IsName(program, "git")) return GitRisk(args);
        if (HighPrograms.Any(name => IsName(program, name))) return CommandRisk.High;
        if (LowPrograms.Any(name => IsName(program, name))) return CommandRisk.Low;
        return CommandRisk.Medium;
    }

    private static CommandRisk GitRisk(string[] args)
    {
        var sub = FirstWord(args);
        if (sub is "status" or "diff" or "log" or "show" or "rev-parse" or "blame" or "describe" or "shortlog" or "help" or "version" or "")
            return CommandRisk.Low;
        if (sub == "remote" && !args.Any(arg => arg is "remove" or "add" or "set-url" or "rename")) return CommandRisk.Low;
        if (sub == "branch" && !args.Any(arg => arg is "-d" or "-D" or "--delete")) return CommandRisk.Low;
        if (sub is "reset" or "rebase" || (sub == "push" && args.Any(arg => arg is "--force" or "--force-with-lease" or "-f")))
            return CommandRisk.High;
        if (sub == "branch") return CommandRisk.High;
        return CommandRisk.Medium;
    }

    private static string FirstWord(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-'))
            {
                if (arg is "-C" or "-c" or "--git-dir" or "--work-tree") i++;
                continue;
            }
            return arg.ToLowerInvariant();
        }
        return "";
    }

    private static bool IsGitDestructive(string program, string[] args)
    {
        if (!IsName(program, "git")) return false;
        var sub = FirstWord(args);
        if (sub is "clean" or "filter-branch" or "filter-repo") return true;
        return sub == "reset" && args.Any(arg => arg.Equals("--hard", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCodeExec(string program, string[] args)
    {
        if (!CodeExecPrograms.Any(name => IsName(program, name))) return false;
        return args.Any(arg => arg is "-c" or "-e" or "--eval" or "-r");
    }

    private static bool HasForbiddenToken(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--hard", StringComparison.OrdinalIgnoreCase) || arg.Equals("/mir", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static CommandRisk Raise(CommandRisk risk, string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--force", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--force-with-lease", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-rf", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-fr", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("/f", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("delete", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("remove", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                return CommandRisk.High;
        }
        return risk;
    }

    private static bool TryFolder(string? requested, out string full, out string reason)
    {
        full = "";
        reason = "";
        var chosen = string.IsNullOrWhiteSpace(requested)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : requested;
        if (string.IsNullOrWhiteSpace(chosen) || !Directory.Exists(chosen))
        {
            reason = "That folder cannot be used.";
            return false;
        }
        try
        {
            full = Path.GetFullPath(chosen);
            if (IsDriveRoot(full))
            {
                reason = "That folder cannot be used.";
                return false;
            }
        }
        catch (Exception)
        {
            reason = "That folder cannot be used.";
            return false;
        }
        if (HasSecret(full))
        {
            reason = "That command is forbidden.";
            return false;
        }
        return true;
    }

    private static bool IsDriveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return false;
            return string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool HasSecret(string text)
    {
        foreach (var secret in Secrets)
            if (text.Contains(secret, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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
        if (CommandRules.Classify(proposal.Program, proposal.Args) == CommandRisk.Forbidden)
        {
            _store.Log(proposal.Display + "  forbidden");
            return "That command is forbidden.";
        }
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
