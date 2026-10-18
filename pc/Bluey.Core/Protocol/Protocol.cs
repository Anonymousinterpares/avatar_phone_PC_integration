using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bluey.Protocol;

public enum Mood
{
    Listening,
    Resting,
    Thinking,
    Talking,
    Pointing,
    Happy,
    Sleepy,
}

public sealed class FaceState
{
    [JsonPropertyName("gazeX")] public double GazeX { get; set; }
    [JsonPropertyName("gazeY")] public double GazeY { get; set; }
    [JsonPropertyName("mood")] public string Mood { get; set; } = "listening";
    [JsonPropertyName("talk")] public double Talk { get; set; }

    public static FaceState Make(double gazeX, double gazeY, Mood mood, double talk = 0) => new()
    {
        GazeX = gazeX,
        GazeY = gazeY,
        Mood = mood.ToString().ToLowerInvariant(),
        Talk = talk,
    };
}

/// <summary>
/// One newline-delimited JSON message. Field names match the Mac/iPhone app.
/// </summary>
public sealed class Packet
{
    [JsonPropertyName("face")] public FaceState? Face { get; set; }
    [JsonPropertyName("hello")] public string? Hello { get; set; }
    [JsonPropertyName("volume")] public double? Volume { get; set; }
    [JsonPropertyName("command")] public string? Command { get; set; }
    [JsonPropertyName("audio")] public string? Audio { get; set; }
    [JsonPropertyName("speech")] public int? Speech { get; set; }
    [JsonPropertyName("callID")] public string? CallId { get; set; }
    [JsonPropertyName("tool")] public string? Tool { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }

    public static Packet Of(string command, string? text = null, string? callId = null) => new()
    {
        Command = command,
        Text = text,
        CallId = callId,
    };
}

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static byte[] Encode(Packet packet)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, Options);
        var line = new byte[bytes.Length + 1];
        bytes.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        return line;
    }

    public static List<Packet> Decode(ref byte[] buffer, ref int length)
    {
        var found = new List<Packet>();
        var start = 0;
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] != (byte)'\n') continue;
            var count = i - start;
            if (count > 0)
            {
                try
                {
                    var packet = JsonSerializer.Deserialize<Packet>(buffer.AsSpan(start, count), Options);
                    if (packet != null) found.Add(packet);
                }
                catch (JsonException)
                {
                    // Ignore a corrupt line and keep the connection.
                }
            }
            start = i + 1;
        }
        if (start > 0)
        {
            Buffer.BlockCopy(buffer, start, buffer, 0, length - start);
            length -= start;
        }
        if (length > 8 * 1024 * 1024) length = 0;
        return found;
    }
}

public static class ServiceNames
{
    public const string Type = "_googly._tcp";
    public const string TypeLocal = "_googly._tcp.local";
}
