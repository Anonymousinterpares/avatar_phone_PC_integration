namespace Bluey.Speech;

/// <summary>
/// Where playback has really gotten to.
/// The position comes from the device play cursor, not from the wall clock.
/// A stall that stops the device, or stops these updates, does not move it.
/// </summary>
public static class WaveCursor
{
    /// <summary>
    /// Seconds of the clip the device has played.
    /// devicePosition and originPosition use the device clock (GetPosition / GetFrequency).
    /// originPaddingFrames is how much other audio was already queued ahead of the first sample.
    /// </summary>
    public static double HeardSecondsFromDevice(
        double devicePosition,
        double originPosition,
        double frequency,
        double originPaddingFrames,
        double mixRate,
        double clipSeconds,
        double previousPeakSeconds)
    {
        if (frequency <= 0 || mixRate <= 0 || clipSeconds <= 0) return Peak(0, clipSeconds, previousPeakSeconds);
        var heard = (devicePosition - originPosition) / frequency - originPaddingFrames / mixRate;
        return Peak(heard, clipSeconds, previousPeakSeconds);
    }

    /// <summary>
    /// Source samples heard when the device clock is unavailable.
    /// releasedSource samples have been copied to the device.
    /// queuedSource samples are still sitting in its buffer.
    /// If the device stops draining, queuedSource stays up and this stops.
    /// </summary>
    public static double HeardSamplesFromQueue(double releasedSource, double queuedSource, double clipSamples, double previousPeakSamples)
    {
        if (clipSamples <= 0) return 0;
        return Peak(releasedSource - queuedSource, clipSamples, previousPeakSamples);
    }

    private static double Peak(double heard, double clip, double previousPeak)
    {
        if (double.IsNaN(heard) || heard < 0) heard = 0;
        if (heard > clip) heard = clip;
        if (previousPeak > clip) previousPeak = clip;
        if (heard < previousPeak) heard = previousPeak;
        return heard;
    }
}
