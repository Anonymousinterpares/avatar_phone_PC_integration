using Bluey.Speech;
using Xunit;

namespace Bluey.Core.Tests;

public class SpeechPaceTests
{
    [Fact]
    public void Full_stop_pins_the_sentence_and_words_share_the_span_by_length()
    {
        var text = "Hi. ABCDEFGH.";
        var marks = new SpeechMarks(0, 3, 3, new[] { 1.0 }, new[] { 0.30 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);

        Assert.Equal("pinned 1", pace.Note);
        Assert.Equal(0, pace.Fraction(0), 3);
        Assert.Equal(1.5 / text.Length, pace.Fraction(0.5), 3);
        Assert.Equal(3.0 / text.Length, pace.Fraction(1.0), 3);
        Assert.Equal(3.0 / text.Length, pace.Fraction(1.2), 3);
        var after = 1.3;
        var resume = 3.0;
        var held = 3.0 / text.Length;
        var atTwo = held + (2.0 - after) / (resume - after) * (1 - held);
        Assert.Equal(atTwo, pace.Fraction(2.0), 3);
        Assert.Equal(1, pace.Fraction(3), 3);
        Assert.Equal(1, pace.Fraction(4), 3);
    }

    [Fact]
    public void A_comma_holds_the_line_until_the_pause_ends()
    {
        var text = "Open the file, then save.";
        var comma = text.IndexOf(',') + 1;
        var marks = new SpeechMarks(0, 2, 2, new[] { 0.4 }, new[] { 0.25 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);

        Assert.Equal("pinned 1", pace.Note);
        var atComma = comma / (double)text.Length;
        Assert.Equal(atComma, pace.Fraction(0.4), 3);
        Assert.Equal(atComma, pace.Fraction(0.6), 3);
        Assert.True(pace.Fraction(0.9) > atComma);
        Assert.Equal(1, pace.Fraction(2), 3);
    }

    [Fact]
    public void A_thousands_separator_is_not_a_pause()
    {
        var text = "Use 1,000 today. Next one.";
        var period = text.IndexOf("today.", StringComparison.Ordinal) + "today.".Length;
        var marks = new SpeechMarks(0, 2, 2, new[] { 0.4, 1.1 }, new[] { 0.12, 0.30 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);

        var atPeriod = period / (double)text.Length;
        Assert.Equal("pinned 1", pace.Note);
        Assert.Equal(atPeriod * (0.4 / 1.1), pace.Fraction(0.4), 3);
        Assert.Equal(atPeriod, pace.Fraction(1.1), 3);
        Assert.Equal(atPeriod, pace.Fraction(1.35), 3);
    }

    [Fact]
    public void A_semicolon_is_a_clause_pause()
    {
        var text = "Pobierz go; uruchom instalator.";
        var clause = text.IndexOf(';') + 1;
        var marks = new SpeechMarks(0, 2, 2, new[] { 0.7 }, new[] { 0.16 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);

        Assert.Equal(clause / (double)text.Length, pace.Fraction(0.7), 3);
        Assert.Equal(clause / (double)text.Length, pace.Fraction(0.82), 3);
    }

    [Fact]
    public void Polish_period_pins_the_first_sentence()
    {
        var text = "Cześć. Co słychać?";
        var first = text.IndexOf('.') + 1;
        var marks = new SpeechMarks(0.1, 2.5, 2.7, new[] { 1.0 }, new[] { 0.28 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);

        Assert.Equal("pinned 1", pace.Note);
        Assert.Equal(0, pace.Fraction(0.05), 3);
        Assert.Equal(first / (double)text.Length, pace.Fraction(1.0), 3);
        Assert.Equal(1, pace.Fraction(2.5), 3);
        Assert.Equal(1, pace.Fraction(2.65), 3);
    }

    [Fact]
    public void Decimal_point_stays_inside_the_sentence()
    {
        var text = "Use 3.14 today. Next one.";
        var first = text.IndexOf("today.", StringComparison.Ordinal) + "today.".Length;
        var marks = new SpeechMarks(0, 2, 2, new[] { 1.1 }, new[] { 0.3 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);

        Assert.Equal(first / (double)text.Length, pace.Fraction(1.1), 3);
    }

    [Fact]
    public void Extra_short_pauses_lose_to_the_longer_sentence_gap()
    {
        var text = "1234. 5678.";
        var boundary = text.IndexOf('.') + 1;
        var marks = new SpeechMarks(0, 2, 2, new[] { 0.3, 1.2 }, new[] { 0.12, 0.45 }, true);
        var full = SpeechPace.Align(text, marks, visibleIsPrefix: false);
        var prefix = SpeechPace.Align(text, marks, visibleIsPrefix: true);

        Assert.Equal(boundary / (double)text.Length, full.Fraction(1.2), 3);
        Assert.Equal(boundary / (double)text.Length, prefix.Fraction(0.3), 3);
    }

    [Fact]
    public void Missing_pause_shares_the_remaining_time_by_length()
    {
        var text = "One. Two. Three.";
        var second = text.IndexOf("Two.", StringComparison.Ordinal) + "Two.".Length;
        var marks = new SpeechMarks(0, 3, 3, new[] { 1.0 }, new[] { 0.3 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);

        Assert.Equal("pinned 1/2", pace.Note);
        var first = 4 / (double)text.Length;
        Assert.Equal(first, pace.Fraction(1.0), 3);
        Assert.Equal(first, pace.Fraction(1.2), 3);
        var secondFrac = second / (double)text.Length;
        var resume = 1.3;
        var at = resume + (secondFrac - first) / (1 - first) * (3.0 - resume);
        Assert.Equal(secondFrac, pace.Fraction(at), 2);
    }

    [Fact]
    public void Comma_and_period_pauses_are_kept_and_the_scan_stays_cheap()
    {
        const int rate = 24000;
        var samples = Clip(rate,
            (0.08, false),
            (0.40, true),
            (0.10, false),
            (0.40, true),
            (0.05, false),
            (0.40, true),
            (0.14, false),
            (0.40, true),
            (0.28, false),
            (0.40, true),
            (0.15, false));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var marks = SpeechPace.Measure(samples, rate);
        watch.Stop();

        Assert.True(watch.Elapsed.TotalMilliseconds < 50, "scan took " + watch.Elapsed.TotalMilliseconds.ToString("0.0") + "ms");
        Assert.Equal(3, marks.GapStarts.Length);
        Assert.All(marks.GapLengths, length => Assert.True(length >= SpeechPace.PauseFloor));
        Assert.InRange(marks.GapLengths[0], 0.08, 0.13);
        Assert.InRange(marks.GapLengths[1], 0.10, 0.18);
        Assert.InRange(marks.GapLengths[2], 0.22, 0.36);
        Assert.InRange(marks.SpeechStart, 0.05, 0.12);

        var longClip = new float[rate * 20];
        for (var i = 0; i < longClip.Length; i++) longClip[i] = MathF.Sin(i * 0.07f) * 0.4f;
        watch.Restart();
        SpeechPace.Measure(longClip, rate);
        watch.Stop();
        Assert.True(watch.Elapsed.TotalMilliseconds < 50, "20s scan took " + watch.Elapsed.TotalMilliseconds.ToString("0.0") + "ms");
    }

    [Fact]
    public void Scroll_keeps_the_spoken_line_near_the_bottom()
    {
        var text = "Alpha one. Beta two. Gamma three. Delta four.";
        var second = text.IndexOf('.', text.IndexOf('.') + 1) + 1;
        var marks = new SpeechMarks(0, 4, 4, new[] { 1.0, 2.0, 3.0 }, new[] { 0.3, 0.3, 0.3 }, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: false);
        const double extent = 400;
        var viewport = extent * (second / (double)text.Length);
        var gap = ReadScroll.LineGap(viewport);
        var room = extent - viewport;

        Assert.Equal("pinned 3", pace.Note);
        Assert.Equal(0, ReadScroll.Offset(pace.Fraction(0), extent, viewport), 2);
        Assert.Equal(0, ReadScroll.Offset(pace.Fraction(1), extent, viewport), 2);

        var atFold = ReadScroll.Offset(pace.Fraction(2), extent, viewport);
        Assert.Equal(gap, atFold, 2);
        Assert.Equal(atFold, ReadScroll.Offset(pace.Fraction(2.15), extent, viewport), 2);
        Assert.True(atFold < room * 0.25, "the first move is one line, not a jump to the end");

        var later = ReadScroll.Offset(pace.Fraction(2.5), extent, viewport);
        Assert.True(later > atFold);
        var spokenFromTop = pace.Fraction(2.5) * extent - later;
        Assert.Equal(viewport - gap, spokenFromTop, 2);

        Assert.Equal(room, ReadScroll.Offset(pace.Fraction(4), extent, viewport), 1);

        var previous = 0.0;
        for (var i = 0; i <= 40; i++)
        {
            var offset = ReadScroll.Offset(pace.Fraction(i / 10.0), extent, viewport);
            Assert.True(offset + 0.01 >= previous);
            previous = offset;
        }
    }

    [Fact]
    public void Scroll_stays_at_the_top_when_the_reply_fits()
    {
        Assert.Equal(0, ReadScroll.Offset(0.4, 100, 120));
        Assert.Equal(0, ReadScroll.Offset(1, 100, 100));
    }

    [Fact]
    public void A_chopped_caption_finishes_its_two_lines_at_the_first_pause()
    {
        // Measured in a window: Comic Sans MS 16 bold, width 240.
        // 280 characters are 245px tall. The bubble viewport caps at 200px, so only 45px can move.
        const double choppedExtent = 245.3;
        const double viewport = 200;
        var text = new string('x', 260) + ". tail of the line here!!";
        text = text.Length > 280 ? text[..280] : text;
        var gaps = new double[18];
        var lengths = new double[18];
        for (var i = 0; i < gaps.Length; i++)
        {
            gaps[i] = 0.8 + i * 2.0;
            lengths[i] = 0.22;
        }
        var marks = new SpeechMarks(0.05, 40, 40.2, gaps, lengths, true);
        var pace = SpeechPace.Align(text, marks, visibleIsPrefix: true);

        Assert.Equal("pinned 1", pace.Note);
        var atFirstPause = pace.Fraction(gaps[0]);
        Assert.True(atFirstPause > 0.9, "the only sentence end is pinned to the first pause");
        var offset = ReadScroll.Offset(atFirstPause, choppedExtent, viewport);
        Assert.InRange(offset, 40, 50);
        Assert.Equal(offset, ReadScroll.Offset(pace.Fraction(10), choppedExtent, viewport), 1);
    }

    [Fact]
    public void Commas_keep_the_full_reply_moving_and_a_device_stall_holds_it()
    {
        // Same window measurement: 508 characters are 446px, so a 200px viewport still has 246px to travel.
        const double extent = 445.9;
        const double viewport = 200;
        var text = "C# is a modern, general-purpose, object-oriented programming language, developed by Microsoft in the late 1990s. It runs on .NET, and it is used for desktop apps, web services, games, and phone tools.";
        var marksInText = ClauseEnds(text);
        Assert.True(marksInText.Count >= 6);
        var gapAt = new double[marksInText.Count];
        var gapLen = new double[marksInText.Count];
        for (var i = 0; i < marksInText.Count; i++)
        {
            gapAt[i] = 0.6 + i * 1.1;
            gapLen[i] = text[marksInText[i] - 1] == ',' ? 0.12 : 0.32;
        }
        var speechEnd = gapAt[^1] + gapLen[^1] + 1.2;
        var pace = SpeechPace.Align(text, new SpeechMarks(0.05, speechEnd, speechEnd + 0.1, gapAt, gapLen, true), false);
        var room = extent - viewport;

        Assert.Equal("pinned " + marksInText.Count, pace.Note);
        var first = marksInText[0] / (double)text.Length;
        Assert.Equal(first, pace.Fraction(gapAt[0]), 3);
        Assert.Equal(first, pace.Fraction(gapAt[0] + gapLen[0] * 0.5), 3);
        var opening = ReadScroll.Offset(pace.Fraction(gapAt[0]), extent, viewport);
        Assert.True(opening < room * 0.2, "the first comma does not finish the scroll");
        var mid = ReadScroll.Offset(pace.Fraction(gapAt[marksInText.Count / 2]), extent, viewport);
        Assert.True(mid > opening);
        Assert.Equal(room, ReadScroll.Offset(pace.Fraction(speechEnd), extent, viewport), 1);

        var peak = 0.0;
        double Heard(double deviceFrames)
        {
            var heard = WaveCursor.HeardSecondsFromDevice(deviceFrames, 1000, 48000, 9600, 48000, speechEnd, peak);
            peak = heard;
            return heard;
        }

        var playing = 1000 + 9600 + gapAt[2] * 48000;
        var atWord = pace.Fraction(Heard(playing));
        var stalled = pace.Fraction(Heard(playing));
        Assert.Equal(atWord, stalled, 5);
        var wallRanAhead = pace.Fraction(Heard(playing) + 2.0);
        Assert.True(wallRanAhead > stalled + 0.05, "a wall clock would skip ahead during the stall");

        var insideComma = 1000 + 9600 + (gapAt[2] + gapLen[2] * 0.5) * 48000;
        var held = pace.Fraction(Heard(insideComma));
        Assert.Equal(marksInText[2] / (double)text.Length, held, 3);
        var after = 1000 + 9600 + (gapAt[2] + gapLen[2] + 0.25) * 48000;
        Assert.True(pace.Fraction(Heard(after)) > held);
    }

    [Fact]
    public void Queue_clock_stops_when_the_device_stops_draining()
    {
        var peak = 0.0;
        var heard = WaveCursor.HeardSamplesFromQueue(20000, 4000, 44100, peak);
        peak = heard;
        Assert.Equal(16000, heard, 1);
        var stalled = WaveCursor.HeardSamplesFromQueue(20000, 4000, 44100, peak);
        Assert.Equal(16000, stalled, 1);
        var inflated = WaveCursor.HeardSamplesFromQueue(20000, 12000, 44100, peak);
        Assert.Equal(16000, inflated, 1);
        peak = inflated;
        var drained = WaveCursor.HeardSamplesFromQueue(44100, 0, 44100, peak);
        Assert.Equal(44100, drained, 1);
    }

    private static List<int> ClauseEnds(string text)
    {
        var ends = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var mark = text[i];
            if (mark is not (',' or '.' or ';' or '!' or '?')) continue;
            if (i > 0 && i + 1 < text.Length && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1])) continue;
            var next = i + 1;
            if (next < text.Length && !char.IsWhiteSpace(text[next])) continue;
            if (next >= text.Length) continue;
            ends.Add(i + 1);
        }
        return ends;
    }

    private static float[] Clip(int rate, params (double seconds, bool sound)[] parts)
    {
        var samples = new List<float>();
        foreach (var (seconds, sound) in parts)
        {
            var count = (int)(rate * seconds);
            for (var i = 0; i < count; i++)
                samples.Add(sound ? MathF.Sin(i * 0.31f) * 0.5f : 0f);
        }
        return samples.ToArray();
    }
}
