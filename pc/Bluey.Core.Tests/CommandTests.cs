using Bluey.Brains;
using Bluey.Commands;
using Xunit;

namespace Bluey.Core.Tests;

public class CommandTests
{
    private static CommandDecision Ask(string json, bool enabled = true, bool medium = false, bool high = false, bool noTouch = false, bool stopped = false, bool helper = false)
    {
        return CommandRules.Decide(new CommandFile(), json, enabled, medium, high, noTouch, stopped, helper);
    }

    [Fact]
    public void Commands_stay_off_until_the_switch_is_on()
    {
        var decision = Ask("{\"program\":\"git\",\"args\":[\"status\"]}", enabled: false);
        Assert.False(decision.Ask);
        Assert.False(decision.RunNow);
        Assert.Contains("off", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_shell_string_is_refused_and_an_exact_allow_can_run()
    {
        var shell = Ask("{\"program\":\"powershell\",\"args\":[]}", medium: true, high: true);
        Assert.Contains("forbidden", shell.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(CommandRules.Resolve("cmd"));
        Assert.Null(CommandRules.Resolve("powershell.exe"));

        var ask = Ask("{\"program\":\"git\",\"args\":[\"status\"]}");
        Assert.True(ask.Ask);
        Assert.Equal("Low", ask.Proposal!.Risk);

        var folder = new CommandFile();
        folder.Allow.Add(new AllowedCommand { Program = "git.exe", Args = new List<string> { "status" }, Folder = ask.Proposal.Folder });
        var allowed = CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false, false, false);
        Assert.True(allowed.RunNow);
        var other = CommandRules.Decide(folder, "{\"program\":\"git\",\"args\":[\"diff\"]}", true, false, false, false, false, false);
        Assert.True(other.Ask);
    }

    [Fact]
    public void Destructive_commands_stay_forbidden_even_when_high_risk_is_on()
    {
        string[] programs = ["powershell", "cmd", "format", "diskpart", "shutdown", "reg", "vssadmin", "bcdedit", "netsh"];
        foreach (var program in programs)
        {
            var decision = Ask("{\"program\":\"" + program + "\",\"args\":[]}", medium: true, high: true);
            Assert.Contains("forbidden", decision.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.False(decision.RunNow);
            Assert.False(decision.Ask);
        }
        Assert.Contains("forbidden", Ask("{\"program\":\"git\",\"args\":[\"clean\"]}", medium: true, high: true).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forbidden", Ask("{\"program\":\"git\",\"args\":[\"reset\",\"--hard\"]}", medium: true, high: true).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forbidden", Ask("{\"program\":\"python\",\"args\":[\"-c\"]}", medium: true, high: true).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Medium_and_high_risk_need_their_own_switches()
    {
        var medium = Ask("{\"program\":\"curl\",\"args\":[]}");
        Assert.Contains("medium-risk", medium.Reason, StringComparison.OrdinalIgnoreCase);
        var mediumOn = Ask("{\"program\":\"curl\",\"args\":[]}", medium: true);
        Assert.True(mediumOn.Ask);
        Assert.Equal("Medium", mediumOn.Proposal!.Risk);

        var high = Ask("{\"program\":\"taskkill\",\"args\":[]}", medium: true);
        Assert.Contains("high-risk", high.Reason, StringComparison.OrdinalIgnoreCase);
        var highOn = Ask("{\"program\":\"taskkill\",\"args\":[]}", medium: true, high: true);
        Assert.True(highOn.Ask);
        Assert.Equal("High", highOn.Proposal!.Risk);
    }

    [Fact]
    public void Private_files_and_no_touch_are_refused()
    {
        Assert.Contains("forbidden", Ask("{\"program\":\"git\",\"args\":[\"show\",\"..\\\\..\\\\.env\"]}").Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No-touch", Ask("{\"program\":\"git\",\"args\":[\"status\"]}", noTouch: true).Reason, StringComparison.Ordinal);
        Assert.Contains("help", Ask("{\"program\":\"git\",\"args\":[\"status\"]}", helper: true).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list", Ask("{\"program\":\"git\",\"args\":\"status\"}").Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be used", Ask("{\"program\":\"git\",\"args\":[\"status\"],\"folder\":\"C:\\\\\"}").Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_yes_does_not_allow_the_next_run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bluey-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new CommandStore(directory);
        var first = CommandRules.Decide(store.Load(), "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false, false, false);
        Assert.True(first.Ask);
        var second = CommandRules.Decide(store.Load(), "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false, false, false);
        Assert.True(second.Ask);
        store.Allow(first.Proposal!);
        var third = CommandRules.Decide(store.Load(), "{\"program\":\"git\",\"args\":[\"status\"]}", true, false, false, false, false, false);
        Assert.True(third.RunNow);
    }

    [Fact]
    public void The_prompt_mentions_a_switch_only_as_off_until_that_switch_is_on()
    {
        var off = Personality.Instructions(false);
        Assert.Contains("Let him run commands is off.", off);
        Assert.Contains("Let him use the computer is off.", off);
        Assert.Contains("Learn about me is off.", off);
        Assert.Contains("Speak replies is off.", off);
        Assert.DoesNotContain("run_command", off);
        Assert.DoesNotContain("Medium-risk", off);
        Assert.DoesNotContain("High-risk", off);

        var on = Personality.Instructions(false, commands: true);
        Assert.Contains("run_command", on);
        Assert.Contains("Medium-risk commands are off.", on);
        Assert.Contains("High-risk commands are off.", on);
        Assert.DoesNotContain("Let him run commands is off.", on);
        Assert.DoesNotContain("command folder", on, StringComparison.OrdinalIgnoreCase);

        var risks = Personality.Instructions(false, commands: true, commandsMedium: true, commandsHigh: true);
        Assert.Contains("Medium-risk commands are on.", risks);
        Assert.Contains("High-risk commands are on.", risks);
        Assert.DoesNotContain("Medium-risk commands are off.", risks);
    }
}
