using Bluey.Memory;
using Xunit;

namespace Bluey.Core.Tests;

public class MemoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-30T12:00:00Z");

    [Fact]
    public void Remember_play_music_becomes_the_default_open()
    {
        var book = new MemoryBook();
        var moment = new MemoryMoment();
        var saved = UserMemory.Decide(book, moment, "Remember that play music means Spotify", true, false, Now);
        Assert.True(saved.Changed);
        Assert.Contains("Spotify", saved.Say, StringComparison.Ordinal);
        var next = UserMemory.Decide(book, moment, "Play me some music", true, false, Now);
        Assert.Equal("open_app", next.Tool);
        Assert.Contains("Spotify", next.Args, StringComparison.Ordinal);
        Assert.Contains("when you ask to play music", next.Say, StringComparison.Ordinal);
        var again = UserMemory.Decide(book, moment, "play some music please", true, false, Now);
        Assert.Equal("Opening Spotify.", again.Say);
        Assert.Null(UserMemory.Decide(book, new MemoryMoment(), "What is the weather?", true, false, Now).Tool);
    }

    [Fact]
    public void Three_repeats_become_a_habit_and_a_correction_turns_it_off()
    {
        var book = new MemoryBook();
        var args = "{\"name\":\"Spotify\"}";
        Assert.True(UserMemory.Observe(book, "play me some music", "open_app", args));
        Assert.True(UserMemory.Observe(book, "play me some music", "open_app", args));
        Assert.Empty(book.Habits);
        Assert.True(UserMemory.Observe(book, "play me some music", "open_app", args));
        Assert.Equal("just_do_it", book.Habits[0].Strength);
        Assert.False(book.Habits[0].Announced);
        var moment = new MemoryMoment();
        var run = UserMemory.Decide(book, moment, "play me some music", true, false, Now);
        Assert.Equal("open_app", run.Tool);
        var corrected = UserMemory.Decide(book, moment, "No, open YouTube instead", true, false, Now.AddMinutes(1));
        Assert.Equal("off", book.Habits[0].Strength);
        Assert.Contains("Youtube", corrected.Say, StringComparison.Ordinal);
        Assert.Contains(book.Habits, habit => habit.Strength == "just_do_it" && habit.Do.Contains("Youtube", StringComparison.Ordinal));
    }

    [Fact]
    public void No_touch_and_computer_control_block_a_habit()
    {
        var book = new MemoryBook();
        var moment = new MemoryMoment();
        UserMemory.Decide(book, moment, "remember that play music means Spotify", true, false, Now);
        var blocked = UserMemory.Decide(book, moment, "play music", true, true, Now);
        Assert.True(blocked.Sad);
        Assert.Null(blocked.Tool);
        Assert.Contains("No-touch", blocked.Say, StringComparison.Ordinal);
        var off = UserMemory.Decide(book, moment, "play music", false, false, Now);
        Assert.Null(off.Tool);
        Assert.Contains("computer", off.Say, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Secrets_and_never_topics_are_not_stored()
    {
        var book = new MemoryBook();
        var moment = new MemoryMoment();
        var secret = UserMemory.Decide(book, moment, "Remember that my password is hunter2", true, false, Now);
        Assert.False(secret.Changed);
        Assert.Empty(book.Habits);
        Assert.Empty(book.Likes);
        UserMemory.Decide(book, moment, "Don't remember my health", true, false, Now);
        var blocked = UserMemory.Decide(book, moment, "Remember that my health is private", true, false, Now);
        Assert.False(blocked.Changed);
        Assert.Empty(book.Likes);
        Assert.Contains("health", book.Never[0], StringComparison.Ordinal);
    }

    [Fact]
    public void What_he_knows_comes_only_from_the_list()
    {
        var book = new MemoryBook();
        var moment = new MemoryMoment();
        Assert.Contains("don't know", UserMemory.Decide(book, moment, "What do you remember?", true, false, Now).Say, StringComparison.Ordinal);
        UserMemory.Decide(book, moment, "Remember that I like instrumental jazz in the evening", true, false, Now);
        var said = UserMemory.Decide(book, moment, "what do you know about me", true, false, Now).Say;
        Assert.Contains("instrumental jazz", said, StringComparison.Ordinal);
        var brief = UserMemory.Brief(book, "play some jazz");
        Assert.Contains("instrumental jazz", brief, StringComparison.Ordinal);
        Assert.Null(UserMemory.Brief(book, "what time is it"));
    }

    [Fact]
    public void Ask_first_waits_for_yes()
    {
        var book = new MemoryBook();
        book.Habits.Add(new MemoryHabit
        {
            Id = "h1",
            When = "play music",
            Do = "open Spotify",
            Tool = "open_app",
            Args = "{\"name\":\"Spotify\"}",
            Strength = "ask_first",
        });
        var moment = new MemoryMoment();
        var asked = UserMemory.Decide(book, moment, "play me some music", true, false, Now);
        Assert.Null(asked.Tool);
        Assert.Contains("like before", asked.Say, StringComparison.Ordinal);
        var yes = UserMemory.Decide(book, moment, "yes", true, false, Now);
        Assert.Equal("open_app", yes.Tool);
        Assert.Equal("just_do_it", book.Habits[0].Strength);
    }

    [Fact]
    public void A_newer_file_is_refused_and_forget_keeps_a_backup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bluey-memory-" + Guid.NewGuid().ToString("N"));
        var store = new MemoryStore(folder);
        var book = new MemoryBook();
        book.Never.Add("health");
        book.Likes.Add(new MemoryLike { Id = "l1", Text = "short answers", Use = "use" });
        Assert.True(store.Save(book));
        store.ForgetEverything();
        var cleared = store.Load();
        Assert.Empty(cleared.Likes);
        Assert.Contains("health", cleared.Never);
        Assert.NotEmpty(store.Backups());

        File.WriteAllText(Path.Combine(folder, "memory.json"), "{\"version\":2,\"habits\":[{\"id\":\"future\"}]}");
        var reloaded = store.Load();
        Assert.True(store.TooNew);
        Assert.Empty(reloaded.Habits);
        reloaded.Likes.Add(new MemoryLike { Id = "x", Text = "nope", Use = "use" });
        Assert.False(store.Save(reloaded));
        Assert.Contains("\"version\":2", File.ReadAllText(Path.Combine(folder, "memory.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_loads_the_one_file_and_does_not_invent_an_empty_one()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bluey-memory-" + Guid.NewGuid().ToString("N"));
        var store = new MemoryStore(folder);
        store.Open();
        Assert.False(File.Exists(Path.Combine(folder, "memory.json")));

        Directory.CreateDirectory(Path.Combine(folder, "backups"));
        File.WriteAllText(
            Path.Combine(folder, "backups", "2026-10-30.json"),
            "{\"version\":1,\"likes\":[{\"id\":\"l1\",\"text\":\"jazz\",\"use\":\"use\"}]}");
        var restored = store.Open();
        Assert.Contains(restored.Likes, like => like.Text == "jazz");
        Assert.True(File.Exists(Path.Combine(folder, "memory.json")));

        File.WriteAllText(
            Path.Combine(folder, "memory.json"),
            "{\"version\":1,\"likes\":[{\"id\":\"l2\",\"text\":\"kept\",\"use\":\"use\"}]}");
        var kept = store.Open();
        Assert.Contains(kept.Likes, like => like.Text == "kept");
        Assert.DoesNotContain(kept.Likes, like => like.Text == "jazz");
    }

    [Fact]
    public void The_prompt_note_is_appended_only_when_there_is_one()
    {
        var plain = Bluey.Brains.Personality.Instructions(true);
        Assert.DoesNotContain("Notes this person", plain, StringComparison.Ordinal);
        var noted = Bluey.Brains.Personality.Instructions(true, memoryNote: "Notes this person keeps on their PC.\n- short answers");
        Assert.Contains("short answers", noted, StringComparison.Ordinal);
        Assert.Contains("click", noted, StringComparison.OrdinalIgnoreCase);
    }
}
