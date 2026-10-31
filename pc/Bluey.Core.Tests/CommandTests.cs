using Bluey.Commands;
using Xunit;

namespace Bluey.Core.Tests;

public class CommandTests
{
    private static CommandFile Book() => new() { Folder = Path.GetTempPath().TrimEnd('\\') };

    [Fact]
    public void Commands_stay_off_until_the_switch_is_on()
    {
        var decision = CommandRules.Decide(Book(), "{\"program\":\"git\",\"args\":[\"status\"]}", false, false, false, false);
        Assert.False(decision.Ask);
        Assert.False(decision.RunNow);
        Assert.Contains("off", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_shell_string_is_refused_and_an_exact_allow_can_run()
    {
        var folder = Book();
        var shell = CommandRules.Decide(folder, "{\"program\":\"powershell\",\"args\":[\"-Command\",\"Get-ChildItem\"]}", true, false, false, false);
        Assert.Contains("shell", shell.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(CommandRules.Resolve("cmd"));
        Assert.Null(CommandRules.Resolve("powershell.exe"));

        var ask = CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false);
        Assert.True(ask.Ask);
        Assert.False(ask.RunNow);

        folder.Allow.Add(new AllowedCommand { Program = "git.exe", Args = new List<string> { "status" }, Folder = Path.GetFullPath(folder.Folder) });
        var allowed = CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false);
        Assert.True(allowed.RunNow);
        var other = CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"diff\"]}", true, false, false, false);
        Assert.True(other.Ask);
    }

    [Fact]
    public void Private_files_outside_paths_and_no_touch_are_refused()
    {
        var folder = Book();
        Assert.Contains("outside", CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"show\",\"..\\\\..\\\\.env\"]}", true, false, false, false).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside", CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"C:\\\\Windows\\\\notepad.exe\"]}", true, false, false, false).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No-touch", CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"status\"]}", true, true, false, false).Reason, StringComparison.Ordinal);
        Assert.Contains("help", CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, true).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list", CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":\"status\"}", true, false, false, false).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(CommandRules.FolderIsUsable("C:\\", out _));
    }

    [Fact]
    public void One_yes_does_not_allow_the_next_run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bluey-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new CommandStore(directory);
        Assert.Null(store.SetFolder(Path.GetTempPath()));
        var first = CommandRules.Decide(store.Load(), "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false);
        Assert.True(first.Ask);
        var second = CommandRules.Decide(store.Load(), "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false);
        Assert.True(second.Ask);
        store.Allow(first.Proposal!);
        var third = CommandRules.Decide(store.Load(), "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false);
        Assert.True(third.RunNow);
    }
}
