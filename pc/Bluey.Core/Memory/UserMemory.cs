using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bluey.Memory;

public sealed class MemoryHabit
{
    public string Id { get; set; } = "";
    public string When { get; set; } = "";
    public string Do { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Args { get; set; } = "";
    public string Strength { get; set; } = "ask_first";
    public int Seen { get; set; }
    public string Source { get; set; } = "you_said";
    public bool Announced { get; set; }
}

public sealed class MemoryLike
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Use { get; set; } = "use";
    public string Source { get; set; } = "you_said";
}

public sealed class MemoryObservation
{
    public string Phrase { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Args { get; set; } = "";
    public int Count { get; set; }
}

public sealed class MemoryBook
{
    public int Version { get; set; } = UserMemory.FormatVersion;
    public List<MemoryHabit> Habits { get; set; } = new();
    public List<MemoryLike> Likes { get; set; } = new();
    public List<string> Never { get; set; } = new();
    public List<MemoryObservation> Observations { get; set; } = new();
}

public sealed class MemoryMoment
{
    public string? PendingHabitId { get; set; }
    public string? LastHabitId { get; set; }
    public DateTimeOffset LastHabitAt { get; set; }
}

public sealed class GuideResult
{
    public bool Handled { get; init; }
    public bool Changed { get; init; }
    public bool BackupFirst { get; init; }
    public string Say { get; init; } = "";
    public string? Tool { get; init; }
    public string? Args { get; init; }
    public bool Sad { get; init; }
}

public static class UserMemory
{
    public const int FormatVersion = 1;
    public const int PromoteAfter = 3;
    public const int MaxNotes = 200;

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "me", "my", "some", "please", "to", "for", "of", "you", "i",
        "want", "just", "can", "could", "would", "hey", "bluey", "ok", "okay", "and",
        "it", "on", "in", "with", "him", "her", "that", "this", "do", "does", "did",
        "is", "are", "be", "am", "let", "lets", "now", "up", "your",
    };

    private static readonly string[] SecretTopics =
    [
        "password", "passwords", "passcode", "pin code", "credit card", "card number",
        "api key", "apikey", "secret", "token", "cvv", "social security",
    ];

    public static GuideResult Decide(MemoryBook book, MemoryMoment moment, string text, bool computerControl, bool noTouch, DateTimeOffset now)
    {
        var command = TryCommand(book, moment, text);
        if (command != null) return command;

        if (!string.IsNullOrEmpty(moment.PendingHabitId))
        {
            var pending = book.Habits.FirstOrDefault(habit => habit.Id == moment.PendingHabitId);
            if (pending == null)
            {
                moment.PendingHabitId = null;
            }
            else if (IsYes(text))
            {
                moment.PendingHabitId = null;
                if (NeedsComputer(pending) && (noTouch || !computerControl)) return Blocked(noTouch);
                pending.Strength = "just_do_it";
                pending.Announced = true;
                return Run(book, moment, pending, now, "Opening " + Target(pending) + ".", changed: true);
            }
            else if (IsNo(text))
            {
                moment.PendingHabitId = null;
                Silence(book, pending);
                return Say("All right, I won't.", changed: true);
            }
            else
            {
                moment.PendingHabitId = null;
            }
        }

        if (!string.IsNullOrEmpty(moment.LastHabitId) && now - moment.LastHabitAt <= TimeSpan.FromMinutes(2))
        {
            var last = book.Habits.FirstOrDefault(habit => habit.Id == moment.LastHabitId);
            if (last != null && IsCorrection(text, last))
            {
                Silence(book, last);
                if (TryReplacement(text, out var replacement) && !Denied(book, replacement))
                {
                    var habit = AddHabit(book, last.When, replacement, "you_said", announced: false);
                    if (habit == null) return Say("The list is full. Turn something off in What he remembers.", changed: true);
                    return Say("I'll " + habit.Do + " when you ask to " + habit.When + ".", changed: true);
                }
                return Say("All right, I won't do that.", changed: true);
            }
        }

        var matches = Match(book, text);
        if (matches.Count > 1)
        {
            if ((noTouch || !computerControl) && matches.All(NeedsComputer)) return Blocked(noTouch);
            var names = string.Join(", or ", matches.Select(habit => habit.Do));
            return Say("Which one: " + names + "?");
        }
        if (matches.Count == 0) return new GuideResult();
        var one = matches[0];
        if (one.Strength == "ask_first")
        {
            if (NeedsComputer(one) && (noTouch || !computerControl)) return Blocked(noTouch);
            moment.PendingHabitId = one.Id;
            var asked = one.Do.Length == 0 ? "Do that" : char.ToUpperInvariant(one.Do[0]) + one.Do[1..];
            return Say(asked + ", like before?");
        }
        if (NeedsComputer(one) && (noTouch || !computerControl)) return Blocked(noTouch);
        var first = !one.Announced;
        if (first) one.Announced = true;
        var line = first
            ? "I'll " + one.Do + " when you ask to " + one.When + "."
            : "Opening " + Target(one) + ".";
        return Run(book, moment, one, now, line, changed: first);
    }

    public static bool Observe(MemoryBook book, string phrase, string tool, string arguments)
    {
        if (tool is not ("open_app" or "open_url")) return false;
        var key = KeyPhrase(phrase);
        if (key.Length == 0 || Denied(book, phrase) || Denied(book, arguments)) return false;
        var arg = CanonicalArg(tool, arguments);
        if (arg.Length == 0) return false;
        var existing = book.Observations.FirstOrDefault(item =>
            item.Phrase == key && item.Tool == tool && item.Args == arg);
        if (existing == null)
        {
            existing = new MemoryObservation { Phrase = key, Tool = tool, Args = arg, Count = 0 };
            book.Observations.Add(existing);
        }
        existing.Count++;
        if (existing.Count < PromoteAfter) return true;
        book.Observations.Remove(existing);
        var plain = PlainAction(tool, arg);
        var already = book.Habits.FirstOrDefault(habit =>
            habit.When == key && habit.Tool == tool && CanonicalArg(habit.Tool, habit.Args) == arg);
        if (already != null)
        {
            if (already.Strength == "off")
            {
                already.Strength = "just_do_it";
                already.Announced = false;
                already.Source = "repeated";
            }
            already.Seen = Math.Max(already.Seen, existing.Count);
            return true;
        }
        var habit = AddHabit(book, key, plain, "repeated", announced: false);
        if (habit != null) habit.Seen = existing.Count;
        return true;
    }

    public static string? Brief(MemoryBook book, string utterance)
    {
        var lines = new List<string>();
        foreach (var habit in Match(book, utterance))
            lines.Add("When they ask to " + habit.When + ", " + habit.Do + ".");
        foreach (var like in book.Likes)
        {
            if (like.Use == "off" || !Overlaps(like.Text, utterance)) continue;
            lines.Add(like.Use == "ask_first" ? "Ask before assuming this: " + like.Text : like.Text);
        }
        foreach (var topic in book.Never)
        {
            if (Overlaps(topic, utterance)) lines.Add("Do not remember or discuss " + topic + ".");
        }
        return FormatNotes(lines, 5);
    }

    public static string? SessionBrief(MemoryBook book)
    {
        var lines = new List<string>();
        foreach (var habit in book.Habits)
        {
            if (habit.Strength == "off") continue;
            var extra = habit.Strength == "ask_first" ? " Ask first." : "";
            lines.Add("When they ask to " + habit.When + ", " + habit.Do + "." + extra);
        }
        foreach (var like in book.Likes)
        {
            if (like.Use == "off") continue;
            lines.Add(like.Use == "ask_first" ? "Ask before assuming this: " + like.Text : like.Text);
        }
        foreach (var topic in book.Never)
            lines.Add("Do not remember or discuss " + topic + ".");
        return FormatNotes(lines, 8);
    }

    public static string Describe(MemoryBook book)
    {
        var lines = new List<string>();
        foreach (var habit in book.Habits)
        {
            if (habit.Strength == "off") continue;
            lines.Add("When you ask to " + habit.When + ", I " + habit.Do + ".");
        }
        foreach (var like in book.Likes)
        {
            if (like.Use == "off") continue;
            lines.Add(like.Text);
        }
        foreach (var topic in book.Never)
            lines.Add("I leave out " + topic + ".");
        if (lines.Count == 0)
        {
            var parked = book.Habits.Count + book.Likes.Count > 0;
            return parked
                ? "I have some notes turned off. The list is in What he remembers."
                : "I don't know anything about you yet.";
        }
        var spoken = string.Join(" ", lines);
        if (spoken.Length <= 420) return spoken;
        return spoken[..420].TrimEnd() + "… The rest is in What he remembers.";
    }

    public static bool SetUse(MemoryBook book, string id, string use)
    {
        var habit = book.Habits.FirstOrDefault(item => item.Id == id);
        if (habit != null)
        {
            var strength = use switch
            {
                "use" or "just_do_it" => "just_do_it",
                "ask_first" => "ask_first",
                "off" => "off",
                _ => "",
            };
            if (strength.Length == 0 || habit.Strength == strength) return false;
            if (strength == "off") Silence(book, habit);
            else habit.Strength = strength;
            return true;
        }
        var like = book.Likes.FirstOrDefault(item => item.Id == id);
        if (like != null)
        {
            var next = use switch
            {
                "just_do_it" or "use" => "use",
                "ask_first" => "ask_first",
                "off" => "off",
                _ => "",
            };
            if (next.Length == 0 || like.Use == next) return false;
            like.Use = next;
            return true;
        }
        if (use == "forget") return ForgetId(book, id);
        return false;
    }

    public static bool ForgetId(MemoryBook book, string id)
    {
        var habit = book.Habits.FirstOrDefault(item => item.Id == id);
        if (habit != null)
        {
            book.Habits.Remove(habit);
            return true;
        }
        var like = book.Likes.FirstOrDefault(item => item.Id == id);
        if (like != null)
        {
            book.Likes.Remove(like);
            return true;
        }
        var topic = book.Never.FirstOrDefault(item => IdFor("never", item) == id);
        if (topic == null) return false;
        book.Never.Remove(topic);
        return true;
    }

    public static void ForgetEverything(MemoryBook book)
    {
        book.Habits.Clear();
        book.Likes.Clear();
        book.Observations.Clear();
    }

    public static void Merge(MemoryBook into, MemoryBook incoming)
    {
        foreach (var habit in incoming.Habits)
        {
            if (into.Habits.Any(item => item.Id == habit.Id)) continue;
            if (!Room(into)) break;
            into.Habits.Add(habit);
        }
        foreach (var like in incoming.Likes)
        {
            if (into.Likes.Any(item => item.Id == like.Id)) continue;
            if (!Room(into)) break;
            into.Likes.Add(like);
        }
        foreach (var topic in incoming.Never)
        {
            if (into.Never.Any(item => Normalize(item) == Normalize(topic))) continue;
            if (into.Never.Count >= 40) break;
            into.Never.Add(topic);
        }
    }

    public static string IdFor(string kind, string text) => kind + ":" + Normalize(text);

    public static string Normalize(string text)
    {
        var buffer = new char[text.Length];
        var count = 0;
        foreach (var raw in text)
        {
            if (raw is '\'' or '\u2019' or '\u2018' or '\u02BC') continue;
            var ch = char.ToLowerInvariant(raw);
            buffer[count++] = char.IsLetterOrDigit(ch) ? ch : ' ';
        }
        return string.Join(' ', new string(buffer, 0, count).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static GuideResult? TryCommand(MemoryBook book, MemoryMoment moment, string text)
    {
        var n = Normalize(text);
        if (n is "what do you remember" or "what do you know about me" or "what do you know" or "what have you learned")
            return Say(Describe(book));
        if (n is "forget everything" or "forget all of that" or "forget it all" or "forget all")
        {
            ForgetEverything(book);
            moment.PendingHabitId = null;
            moment.LastHabitId = null;
            return new GuideResult { Handled = true, Changed = true, BackupFirst = true, Say = "Forgotten. A copy is still in the backups." };
        }
        if (n is "forget that" or "forget the last thing" or "forget the last one" or "forget it")
            return ForgetRecent(book, moment);
        if (n.StartsWith("forget ", StringComparison.Ordinal))
        {
            var rest = n["forget ".Length..].Trim();
            if (rest is "that" or "the last thing" or "the last one" or "it") return ForgetRecent(book, moment);
            if (ForgetMatching(book, rest)) return Say("Forgotten.", changed: true);
            return Say("I don't have that on the list.");
        }
        string? topic = null;
        if (n.StartsWith("dont remember ", StringComparison.Ordinal)) topic = n["dont remember ".Length..].Trim();
        else if (n.StartsWith("do not remember ", StringComparison.Ordinal)) topic = n["do not remember ".Length..].Trim();
        else if (n.StartsWith("never remember ", StringComparison.Ordinal)) topic = n["never remember ".Length..].Trim();
        if (topic != null)
        {
            if (topic.Length == 0) return Say("What should I leave out?");
            if (HasSecretValue(text)) return Say("I don't keep those.");
            if (ForgetMatching(book, topic)) return Say("I won't keep that.", changed: true);
            var clean = topic.Length > 80 ? topic[..80] : topic;
            if (book.Never.Any(item => Normalize(item) == Normalize(clean))) return Say("I already leave that out.");
            if (book.Never.Count >= 40) return Say("The do-not-remember list is full.");
            book.Never.Add(clean);
            return Say("I won't remember " + clean + ".", changed: true);
        }
        string? remember = null;
        if (n.StartsWith("remember that ", StringComparison.Ordinal)) remember = n["remember that ".Length..].Trim();
        else if (n.StartsWith("remember this ", StringComparison.Ordinal)) remember = n["remember this ".Length..].Trim();
        else if (n.StartsWith("remember ", StringComparison.Ordinal)) remember = n["remember ".Length..].Trim();
        if (remember == null) return null;
        if (remember.Length == 0) return Say("What should I remember?");
        if (HasSecretValue(text) || Denied(book, remember)) return Say("I don't keep those.");
        if (TryMeans(remember, out var when, out var action))
        {
            var habit = AddHabit(book, when, action, "you_said", announced: false);
            if (habit == null) return Say("The list is full. Turn something off in What he remembers.");
            habit.Strength = "just_do_it";
            return Say("I'll " + habit.Do + " when you ask to " + habit.When + ".", changed: true);
        }
        var likeText = remember.Length > 160 ? remember[..160] : remember;
        if (book.Likes.Any(item => Normalize(item.Text) == Normalize(likeText)))
            return Say("I already have that.");
        if (!Room(book)) return Say("The list is full. Turn something off in What he remembers.");
        book.Likes.Add(new MemoryLike
        {
            Id = NewId(),
            Text = likeText,
            Use = "use",
            Source = "you_said",
        });
        return Say("I'll remember that.", changed: true);
    }

    private static GuideResult ForgetRecent(MemoryBook book, MemoryMoment moment)
    {
        if (!string.IsNullOrEmpty(moment.LastHabitId) && ForgetId(book, moment.LastHabitId))
        {
            moment.LastHabitId = null;
            return Say("Forgotten.", changed: true);
        }
        var habit = book.Habits.LastOrDefault();
        if (habit != null)
        {
            book.Habits.Remove(habit);
            return Say("Forgotten.", changed: true);
        }
        var like = book.Likes.LastOrDefault();
        if (like != null)
        {
            book.Likes.Remove(like);
            return Say("Forgotten.", changed: true);
        }
        return Say("There is nothing to forget.");
    }

    private static bool ForgetMatching(MemoryBook book, string text)
    {
        var tokens = Tokens(text);
        var habit = book.Habits.LastOrDefault(item => Tokens(item.When).Any(token => tokens.Contains(token)) || Overlaps(item.Do, text));
        if (habit != null)
        {
            book.Habits.Remove(habit);
            return true;
        }
        var like = book.Likes.LastOrDefault(item => Overlaps(item.Text, text));
        if (like == null) return false;
        book.Likes.Remove(like);
        return true;
    }

    private static bool NeedsComputer(MemoryHabit habit) => habit.Tool is "open_app" or "open_url";

    private static GuideResult Run(MemoryBook book, MemoryMoment moment, MemoryHabit habit, DateTimeOffset now, string line, bool changed)
    {
        if (habit.Tool is not ("open_app" or "open_url"))
            return Say(line, changed);
        moment.LastHabitId = habit.Id;
        moment.LastHabitAt = now;
        moment.PendingHabitId = null;
        return new GuideResult
        {
            Handled = true,
            Changed = changed,
            Say = line,
            Tool = habit.Tool,
            Args = habit.Args,
        };
    }

    public static GuideResult Blocked(bool noTouch) => noTouch
        ? new GuideResult { Handled = true, Sad = true, Say = "No-touch mode is on, so I can't do that." }
        : new GuideResult { Handled = true, Say = "Let him use the computer is off, so I can't do that." };

    private static GuideResult Say(string line, bool changed = false) =>
        new() { Handled = true, Changed = changed, Say = line };

    public static List<MemoryHabit> Match(MemoryBook book, string text)
    {
        var utterance = Tokens(text);
        var hits = new List<MemoryHabit>();
        foreach (var habit in book.Habits)
        {
            if (habit.Strength == "off") continue;
            var when = Tokens(habit.When);
            if (when.Count == 0) continue;
            if (when.Count < 2 && Normalize(habit.When) != Normalize(text)) continue;
            if (when.All(token => utterance.Contains(token))) hits.Add(habit);
        }
        var specific = hits.Where(habit => !hits.Any(other =>
            other != habit && Tokens(habit.When).All(token => Tokens(other.When).Contains(token)) && Tokens(other.When).Count > Tokens(habit.When).Count)).ToList();
        var distinct = new List<MemoryHabit>();
        foreach (var habit in specific)
        {
            if (distinct.Any(item => item.Tool == habit.Tool && CanonicalArg(item.Tool, item.Args) == CanonicalArg(habit.Tool, habit.Args)))
                continue;
            distinct.Add(habit);
        }
        return distinct;
    }

    public static bool IsYes(string text)
    {
        var n = Normalize(text);
        if (n.Length == 0 || n.Split(' ').Length > 4) return false;
        return n is "yes" or "yeah" or "yep" or "yup" or "ok" or "okay" or "sure" or "do it" or "go on" or "please"
            or "yes please" or "yeah please" or "ok do it";
    }

    public static bool IsNo(string text)
    {
        var n = Normalize(text);
        if (n.Length == 0 || n.Split(' ').Length > 5) return false;
        return n is "no" or "nope" or "nah" or "dont" or "do not" or "no thanks" or "no thank you" or "dont do that" or "stop";
    }

    public static bool IsCorrection(string text, MemoryHabit habit)
    {
        var n = Normalize(text);
        if (n is "no" or "nope" or "nah" or "wrong" or "stop" or "dont" or "do not" or "not that" or "thats wrong"
            or "not this" or "stop that" or "dont do that" or "stop doing that")
            return true;
        var refusal = n.StartsWith("no ", StringComparison.Ordinal) || n.StartsWith("not ", StringComparison.Ordinal)
            || n.StartsWith("dont ", StringComparison.Ordinal) || n.StartsWith("do not ", StringComparison.Ordinal)
            || n.StartsWith("stop ", StringComparison.Ordinal);
        if (!refusal) return false;
        return Mentions(n, habit) || n.Contains("instead", StringComparison.Ordinal)
            || n.Contains("open ", StringComparison.Ordinal) || n.Contains("use ", StringComparison.Ordinal);
    }

    public static bool HasSecretValue(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:\d[ -]?){13,19}\b")) return true;
        if (Regex.IsMatch(text, @"\bsk-[A-Za-z0-9]{10,}\b")) return true;
        var n = Normalize(text);
        return Regex.IsMatch(n, @"\b(password|passwords|passcode|pin|token|secret|cvv|apikey|api key)\s+(is|equals)\b");
    }

    public static bool Denied(MemoryBook book, string text)
    {
        if (HasSecretValue(text)) return true;
        var n = Normalize(text);
        foreach (var topic in book.Never)
        {
            var t = Normalize(topic);
            if (t.Length >= 3 && n.Contains(t, StringComparison.Ordinal)) return true;
        }
        foreach (var word in SecretTopics)
        {
            if (n.Contains(word, StringComparison.Ordinal) && Regex.IsMatch(n, @"\b(is|equals)\b")) return true;
        }
        return false;
    }

    private static bool TryMeans(string remember, out string when, out string action)
    {
        when = "";
        action = "";
        var patterns = new[]
        {
            @"^when i say (.+) (?:then )?(?:open|use|play) (.+)$",
            @"^when i ask for (.+) (?:then )?(?:open|use|play) (.+)$",
            @"^(.+) means (?:open |use |play )?(.+)$",
        };
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(remember, pattern);
            if (!match.Success) continue;
            when = KeyPhrase(match.Groups[1].Value);
            var target = match.Groups[2].Value.Trim();
            if (when.Length == 0 || target.Length == 0) continue;
            action = target.StartsWith("http", StringComparison.Ordinal) ? "open " + target : "open " + Title(target);
            return true;
        }
        return false;
    }

    private static bool TryReplacement(string text, out string action)
    {
        action = "";
        var n = Normalize(text);
        var match = Regex.Match(n, @"(?:open|use|play)\s+(.+?)(?:\s+instead)?$");
        if (!match.Success) match = Regex.Match(n, @"instead\s+(?:open |use |play )?(.+)$");
        if (!match.Success) return false;
        var target = match.Groups[1].Value.Trim();
        if (target.Length == 0 || target is "that" or "this" or "it") return false;
        action = target.StartsWith("http", StringComparison.Ordinal) ? "open " + target : "open " + Title(target);
        return true;
    }

    private static MemoryHabit? AddHabit(MemoryBook book, string when, string action, string source, bool announced)
    {
        var tool = action.StartsWith("open http", StringComparison.Ordinal) ? "open_url" : "open_app";
        var value = action["open ".Length..].Trim();
        if (tool == "open_app") value = Title(value);
        var args = tool == "open_url"
            ? JsonSerializer.Serialize(new Dictionary<string, string> { ["url"] = value })
            : JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = value });
        var habit = book.Habits.FirstOrDefault(item => item.When == when && item.Tool == tool && CanonicalArg(item.Tool, item.Args) == CanonicalArg(tool, args));
        if (habit != null)
        {
            habit.Strength = "just_do_it";
            habit.Do = tool == "open_url" ? "open " + value : "open " + value;
            habit.Source = source;
            habit.Announced = announced;
            habit.Seen++;
            return habit;
        }
        if (!Room(book)) return null;
        habit = new MemoryHabit
        {
            Id = NewId(),
            When = when,
            Do = "open " + value,
            Tool = tool,
            Args = args,
            Strength = "just_do_it",
            Seen = 1,
            Source = source,
            Announced = announced,
        };
        book.Habits.Add(habit);
        return habit;
    }

    private static void Silence(MemoryBook book, MemoryHabit habit)
    {
        habit.Strength = "off";
        var arg = CanonicalArg(habit.Tool, habit.Args);
        book.Observations.RemoveAll(item => item.Tool == habit.Tool && item.Args == arg && item.Phrase == habit.When);
    }

    private static bool Room(MemoryBook book)
    {
        while (book.Habits.Count + book.Likes.Count >= MaxNotes)
        {
            var like = book.Likes.FirstOrDefault(item => item.Use is "off" or "ask_first");
            if (like != null)
            {
                book.Likes.Remove(like);
                continue;
            }
            var habit = book.Habits.FirstOrDefault(item => item.Strength is "off" or "ask_first");
            if (habit == null) return false;
            book.Habits.Remove(habit);
        }
        return true;
    }

    private static string? FormatNotes(List<string> lines, int take)
    {
        if (lines.Count == 0) return null;
        return "Notes this person keeps on their PC. Use one only if it fits this request. Do not invent memories that are not written here.\n- "
            + string.Join("\n- ", lines.Take(take));
    }

    private static bool Mentions(string normalized, MemoryHabit habit)
    {
        var target = Tokens(Target(habit));
        var said = Tokens(normalized);
        return target.Any(token => said.Contains(token));
    }

    private static string Target(MemoryHabit habit)
    {
        var value = CanonicalArg(habit.Tool, habit.Args);
        return value.Length == 0 ? habit.Do : value;
    }

    public static string CanonicalArg(string tool, string arguments)
    {
        var key = tool == "open_url" ? "url" : "name";
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
            if (doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                return (value.GetString() ?? "").Trim();
        }
        catch (JsonException)
        {
            return "";
        }
        return "";
    }

    private static string PlainAction(string tool, string arg) =>
        tool == "open_url" ? "open " + arg : "open " + Title(arg);

    public static string KeyPhrase(string text)
    {
        var tokens = Tokens(text);
        if (tokens.Count == 0) return Normalize(text);
        return string.Join(' ', tokens.Take(8));
    }

    private static List<string> Tokens(string text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(token => !Stop.Contains(token) && token.Length > 1).ToList();

    private static bool Overlaps(string left, string right)
    {
        var a = Tokens(left);
        var b = Tokens(right);
        return a.Any(token => token.Length >= 4 && b.Contains(token));
    }

    private static string Title(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(part =>
            part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part[1..].ToLowerInvariant() : "")));
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
