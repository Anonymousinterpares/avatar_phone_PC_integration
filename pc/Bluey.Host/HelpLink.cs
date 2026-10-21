using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using Bluey.Protocol;

namespace Bluey.Host;

/// <summary>Outbound connection from a helper computer to the computer being helped.</summary>
public sealed class HelpLink : IDisposable
{
    private readonly object _write = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Packet>> _waiting = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cancel;
    private int _generation;

    public event Action<Packet>? Received;
    public event Action? Closed;
    public bool Connected => _tcp?.Connected == true;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        Close();
        var tcp = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        await tcp.ConnectAsync(host, port, timeout.Token);
        var generation = Interlocked.Increment(ref _generation);
        _tcp = tcp;
        _stream = tcp.GetStream();
        _cancel = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoop(_stream, generation, _cancel.Token));
    }

    public async Task<Packet> RequestAsync(string command, string? text, int timeoutMs)
    {
        var callId = Guid.NewGuid().ToString("N");
        var wait = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[callId] = wait;
        try
        {
            Send(command, text, callId: callId);
            var finished = await Task.WhenAny(wait.Task, Task.Delay(timeoutMs));
            if (finished != wait.Task) throw new TimeoutException("The computer did not answer.");
            return await wait.Task;
        }
        finally
        {
            _waiting.TryRemove(callId, out _);
        }
    }

    public void Send(string command, string? text = null, string? audio = null, string? callId = null)
    {
        var packet = new Packet { Command = command, Text = text, Audio = audio, CallId = callId };
        var bytes = ProtocolJson.Encode(packet);
        var stream = _stream ?? throw new InvalidOperationException("Not connected.");
        lock (_write)
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }

    public void Close()
    {
        Interlocked.Increment(ref _generation);
        try { _cancel?.Cancel(); } catch { }
        try { _stream?.Close(); } catch { }
        try { _tcp?.Close(); } catch { }
        _stream = null;
        _tcp = null;
        foreach (var wait in _waiting.Values)
            wait.TrySetException(new InvalidOperationException("The connection closed."));
        _waiting.Clear();
    }

    public void Dispose() => Close();

    private async Task ReadLoop(NetworkStream stream, int generation, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var length = 0;
        try
        {
            while (!token.IsCancellationRequested && generation == _generation)
            {
                if (length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), token);
                if (read == 0) break;
                length += read;
                foreach (var packet in ProtocolJson.Decode(ref buffer, ref length))
                {
                    if (!string.IsNullOrEmpty(packet.CallId) && _waiting.TryRemove(packet.CallId, out var wait))
                        wait.TrySetResult(packet);
                    else
                        Received?.Invoke(packet);
                }
            }
        }
        catch (Exception)
        {
            // The other computer closed the connection.
        }
        if (generation == _generation)
        {
            Close();
            Closed?.Invoke();
        }
    }
}
