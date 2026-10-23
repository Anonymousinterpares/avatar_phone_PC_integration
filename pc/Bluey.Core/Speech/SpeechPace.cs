namespace Bluey.Speech;

/// <summary>
/// Where the voice paused, in seconds from the start of the clip.
/// GapStarts are the moments speech stops between phrases.
/// </summary>
public readonly record struct SpeechMarks(
    double SpeechStart,
    double SpeechEnd,
    double FileEnd,
    double[] GapStarts,
    double[] GapLengths,
    bool FromSpeaker);

/// <summary>
/// Maps a moment in the waveform onto how far through the caption the reader should be.
/// Commas and sentence ends hold the text for the whole pause. Inside a clause, time follows character length.
/// The moment is a position in the clip, so a device stall does not advance it.
/// </summary>
public sealed class SpeechPace
{
    /// <summary>How long a silent caption stays up. The scroll crosses the text once in this time.</summary>
    public static readonly TimeSpan QuietHold = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Shortest silence treated as a comma or a full stop.
    /// Word gaps in this voice stay under it. A comma is about 90ms or more, and a period is longer.
    /// </summary>
    public const double PauseFloor = 0.085;

    private readonly double[] _time;
    private readonly double[] _fraction;

    private SpeechPace(double[] time, double[] fraction, bool fromSpeaker, string note)
    {
        _time = time;
        _fraction = fraction;
        FromSpeaker = fromSpeaker;
        Note = note;
    }

    public bool FromSpeaker { get; }
    public string Note { get; }

    public double Fraction(double seconds)
    {
        if (_time.Length == 0) return 1;
        if (seconds <= _time[0]) return _fraction[0];
        var last = _time.Length - 1;
        if (seconds >= _time[last]) return _fraction[last];
        var lo = 0;
        var hi = last;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) >> 1;
            if (_time[mid] <= seconds) lo = mid;
            else hi = mid;
        }
        var span = _time[hi] - _time[lo];
        var blend = span <= 1e-6 ? 1 : (seconds - _time[lo]) / span;
        return _fraction[lo] + (_fraction[hi] - _fraction[lo]) * blend;
    }

    /// <summary>
    /// Finds pauses in PCM that is already decoded. A short clip finishes in a few milliseconds.
    /// Call it after playback has been queued so the scan does not hold up the first sample.
    /// </summary>
    public static SpeechMarks Measure(float[]? samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0 || sampleRate < 8000)
            return new SpeechMarks(0, 0, 0, Array.Empty<double>(), Array.Empty<double>(), true);

        var frame = Math.Max(1, sampleRate / 50);
        var hop = Math.Max(1, sampleRate / 100);
        if (samples.Length < frame)
        {
            frame = samples.Length;
            hop = frame;
        }
        var frames = 1 + (samples.Length - frame) / hop;
        var rms = new float[frames];
        float peak = 0;
        for (var n = 0; n < frames; n++)
        {
            var start = n * hop;
            var avail = Math.Min(frame, samples.Length - start);
            double sum = 0;
            for (var j = 0; j < avail; j++)
            {
                var sample = samples[start + j];
                sum += sample * sample;
            }
            var energy = (float)Math.Sqrt(sum / avail);
            rms[n] = energy;
            if (energy > peak) peak = energy;
        }

        var fileEnd = samples.Length / (double)sampleRate;
        if (peak < 1e-5f)
            return new SpeechMarks(0, fileEnd, fileEnd, Array.Empty<double>(), Array.Empty<double>(), true);

        var thresh = peak * 0.08f;
        var first = -1;
        var last = -1;
        for (var n = 0; n < rms.Length; n++)
        {
            if (rms[n] < thresh) continue;
            if (first < 0) first = n;
            last = n;
        }
        if (first < 0)
            return new SpeechMarks(0, fileEnd, fileEnd, Array.Empty<double>(), Array.Empty<double>(), true);

        var speechStart = first * hop / (double)sampleRate;
        var speechEnd = Math.Min(fileEnd, (last * hop + frame) / (double)sampleRate);
        var starts = new List<double>();
        var lengths = new List<double>();
        var inGap = false;
        var gapAt = 0.0;
        for (var n = first; n <= last; n++)
        {
            var at = n * hop / (double)sampleRate;
            if (rms[n] < thresh)
            {
                if (!inGap)
                {
                    inGap = true;
                    gapAt = at;
                }
            }
            else if (inGap)
            {
                var length = at - gapAt;
                if (length >= PauseFloor)
                {
                    starts.Add(gapAt);
                    lengths.Add(length);
                }
                inGap = false;
            }
        }
        return new SpeechMarks(speechStart, speechEnd, fileEnd, starts.ToArray(), lengths.ToArray(), true);
    }

    public static SpeechPace Align(string? text, SpeechMarks marks, bool visibleIsPrefix)
    {
        var source = text ?? "";
        var speechStart = Math.Max(0, marks.SpeechStart);
        var speechEnd = marks.SpeechEnd;
        if (speechEnd <= speechStart) speechEnd = speechStart + 0.05;
        var fileEnd = Math.Max(marks.FileEnd, speechEnd);
        var ends = SentenceEnds(source);
        var total = Math.Max(source.Length, 1);
        var bounds = new List<double>();
        for (var i = 0; i < ends.Count - 1; i++) bounds.Add(ends[i] / (double)total);

        var chosen = ChooseGaps(marks, speechStart, speechEnd, bounds.Count, visibleIsPrefix);
        var times = new List<double> { 0 };
        var fracs = new List<double> { 0 };
        if (speechStart > 0.001)
        {
            times.Add(speechStart);
            fracs.Add(0);
        }

        var note = "linear";
        if (bounds.Count > 0 && chosen.Count > 0)
        {
            var pinned = Math.Min(bounds.Count, chosen.Count);
            for (var i = 0; i < pinned; i++)
            {
                var at = Math.Max(chosen[i].Start, times[^1]);
                times.Add(at);
                fracs.Add(bounds[i]);
                var resume = at + Math.Max(0, chosen[i].Length);
                if (resume > at + 0.01)
                {
                    times.Add(resume);
                    fracs.Add(bounds[i]);
                }
            }
            if (pinned < bounds.Count)
            {
                var prevT = times[^1];
                var prevF = fracs[^1];
                var spanF = 1 - prevF;
                var spanT = Math.Max(0.05, speechEnd - prevT);
                for (var i = pinned; i < bounds.Count; i++)
                {
                    var local = spanF <= 1e-6 ? 1 : (bounds[i] - prevF) / spanF;
                    times.Add(prevT + local * spanT);
                    fracs.Add(bounds[i]);
                }
            }
            note = pinned == bounds.Count ? "pinned " + pinned : "pinned " + pinned + "/" + bounds.Count;
        }

        times.Add(speechEnd);
        fracs.Add(1);
        if (fileEnd > speechEnd + 0.01)
        {
            times.Add(fileEnd);
            fracs.Add(1);
        }
        return Pack(times, fracs, marks.FromSpeaker, note);
    }

    private readonly record struct Gap(double Start, double Length);

    private static List<Gap> ChooseGaps(SpeechMarks marks, double speechStart, double speechEnd, int boundaries, bool visibleIsPrefix)
    {
        var starts = marks.GapStarts ?? Array.Empty<double>();
        var lengths = marks.GapLengths ?? Array.Empty<double>();
        var order = new List<int>();
        for (var i = 0; i < starts.Length; i++)
        {
            var at = starts[i];
            if (at <= speechStart + 0.02 || at >= speechEnd - 0.02) continue;
            order.Add(i);
        }
        order.Sort((a, b) => starts[a].CompareTo(starts[b]));
        if (boundaries <= 0 || order.Count == 0) return new List<Gap>();
        if (order.Count > boundaries)
        {
            if (visibleIsPrefix)
            {
                order.RemoveRange(boundaries, order.Count - boundaries);
            }
            else
            {
                order.Sort((a, b) =>
                {
                    var lengthA = a < lengths.Length ? lengths[a] : 0;
                    var lengthB = b < lengths.Length ? lengths[b] : 0;
                    return lengthB.CompareTo(lengthA);
                });
                order.RemoveRange(boundaries, order.Count - boundaries);
                order.Sort((a, b) => starts[a].CompareTo(starts[b]));
            }
        }
        var chosen = new List<Gap>(order.Count);
        foreach (var index in order)
        {
            var length = index < lengths.Length ? lengths[index] : 0;
            chosen.Add(new Gap(starts[index], length));
        }
        return chosen;
    }

    private static SpeechPace Pack(List<double> times, List<double> fracs, bool fromSpeaker, string note)
    {
        var keptT = new List<double>();
        var keptF = new List<double>();
        for (var i = 0; i < times.Count && i < fracs.Count; i++)
        {
            var time = times[i];
            var frac = Math.Clamp(fracs[i], 0, 1);
            if (keptT.Count > 0 && time < keptT[^1]) time = keptT[^1];
            if (keptT.Count > 0 && time - keptT[^1] < 1e-4)
            {
                if (frac >= keptF[^1]) keptF[^1] = frac;
                continue;
            }
            keptT.Add(time);
            keptF.Add(frac);
        }
        if (keptT.Count == 0)
        {
            keptT.Add(0);
            keptF.Add(1);
        }
        return new SpeechPace(keptT.ToArray(), keptF.ToArray(), fromSpeaker, note);
    }

    private static List<int> SentenceEnds(string text)
    {
        var ends = new List<int>();
        if (text.Length == 0)
        {
            ends.Add(0);
            return ends;
        }
        for (var i = 0; i < text.Length; i++)
        {
            var mark = text[i];
            if (mark == '\n')
            {
                ends.Add(i + 1);
                continue;
            }
            if (mark is ',' or ';' or '，' or '、')
            {
                if (mark == ',' && i > 0 && i + 1 < text.Length && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1])) continue;
                if (!ClosesSentence(text, i)) continue;
                ends.Add(i + 1);
                continue;
            }
            if (mark is not ('.' or '!' or '?' or '…' or '。' or '！' or '？')) continue;
            if (mark == '.')
            {
                if (i > 0 && i + 1 < text.Length && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1])) continue;
                while (i + 1 < text.Length && text[i + 1] == '.') i++;
            }
            if (!ClosesSentence(text, i)) continue;
            ends.Add(i + 1);
        }
        if (ends.Count == 0 || ends[^1] < text.Length) ends.Add(text.Length);
        return ends;
    }

    private static bool ClosesSentence(string text, int index)
    {
        var next = index + 1;
        while (next < text.Length && text[next] is '.' or '!' or '?' or '"' or '\'' or '”' or '’' or ')' or ']') next++;
        if (next >= text.Length) return true;
        return char.IsWhiteSpace(text[next]);
    }
}
