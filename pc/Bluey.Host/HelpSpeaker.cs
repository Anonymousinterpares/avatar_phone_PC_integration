namespace Bluey.Host;

/// <summary>Plays the helper's 16 kHz voice on the physical speakers, not on the muted default device.</summary>
public sealed class HelpSpeaker : IDisposable
{
    private readonly PhysicalSpeaker _speaker = new();

    public void Play(byte[] pcm)
    {
        if (pcm.Length < 2 || pcm.Length > 32000) return;
        var count = pcm.Length / 2;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        _speaker.Enqueue(samples, 16000);
    }

    public void Reset() => _speaker.Clear();

    public void Dispose() => _speaker.Dispose();
}
