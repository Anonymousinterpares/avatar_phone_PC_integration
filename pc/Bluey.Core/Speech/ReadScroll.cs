namespace Bluey.Speech;

/// <summary>
/// How far a caption should move.
/// The opening lines stay put until the voice reaches the last line already on screen.
/// That line then stays near the bottom, so the next line comes in with the voice.
/// </summary>
public static class ReadScroll
{
    public static double Offset(double spokenFraction, double extent, double viewport)
    {
        if (extent <= 0 || viewport <= 1) return 0;
        if (extent <= viewport + 0.5) return 0;
        var readY = Math.Clamp(spokenFraction, 0, 1) * extent;
        var room = extent - viewport;
        var anchor = viewport - LineGap(viewport);
        if (readY <= anchor) return 0;
        var offset = readY - anchor;
        if (offset > room) return room;
        return offset < 0 ? 0 : offset;
    }

    /// <summary>Pixels kept under the spoken line so the following line can enter.</summary>
    internal static double LineGap(double viewport)
    {
        var gap = viewport * 0.18;
        if (gap < 18) gap = 18;
        if (gap > 36) gap = 36;
        if (gap > viewport * 0.45) gap = viewport * 0.45;
        return gap;
    }
}
