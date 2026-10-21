using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bluey.Protocol;
using Makaretu.Dns;

namespace Bluey.Host;

public sealed class PhoneLink : IDisposable
{
    public const int PreferredPort = 47821;

    private readonly Func<Packet, Func<Packet, Task>, CancellationToken, Task<Packet?>> _onPacket;
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<Stream> _clients = new();
    private readonly Dictionary<string, Func<Packet, Task>> _senders = new();
    private readonly object _gate = new();
    private TcpListener? _listener;
    private ServiceDiscovery? _discovery;
    private static readonly System.Threading.AsyncLocal<string?> CurrentClient = new();

    public static string CurrentClientId => CurrentClient.Value ?? "";

    public PhoneLink(Func<Packet, Func<Packet, Task>, CancellationToken, Task<Packet?>> onPacket)
    {
        _onPacket = onPacket;
    }

    public int Port { get; private set; }
    public string AddressText { get; private set; } = "";
    public Action? OnClientsCleared { get; set; }
    public Action<string>? OnClientClosed { get; set; }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, PreferredPort);
        try
        {
            _listener.Start();
        }
        catch (SocketException)
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
        }
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        AddressText = string.Join(", ", LanAddresses().Select(address => address + ":" + Port));
        try
        {
            var profile = new ServiceProfile(Environment.MachineName, ServiceNames.Type, (ushort)Port);
            _discovery = new ServiceDiscovery();
            _discovery.Advertise(profile);
        }
        catch (Exception)
        {
            // The phone can still connect by the address shown in the tray.
        }
        _ = AcceptLoop();
    }

    public async Task BroadcastAsync(Packet packet)
    {
        var bytes = ProtocolJson.Encode(packet);
        List<Stream> clients;
        lock (_gate) clients = _clients.ToList();
        foreach (var stream in clients)
        {
            try { await stream.WriteAsync(bytes); }
            catch (Exception) { Drop(stream); }
        }
    }

    public Task SendToAsync(string clientId, Packet packet)
    {
        Func<Packet, Task>? send;
        lock (_gate) _senders.TryGetValue(clientId, out send);
        return send?.Invoke(packet) ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _discovery?.Dispose();
        _listener?.Stop();
    }

    private async Task AcceptLoop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(_cancel.Token); }
            catch (Exception) { return; }
            client.NoDelay = true;
            var stream = client.GetStream();
            lock (_gate) _clients.Add(stream);
            _ = Task.Run(() => ReadLoop(client, stream));
            await BroadcastAsync(Packet.Of("hello", Environment.MachineName));
        }
    }

    private async Task ReadLoop(TcpClient client, Stream stream)
    {
        var clientId = Guid.NewGuid().ToString("N");
        var buffer = new byte[64 * 1024];
        var length = 0;
        var writeLock = new SemaphoreSlim(1, 1);
        CurrentClient.Value = clientId;
        lock (_gate) _senders[clientId] = SendToThis;
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                if (length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), _cancel.Token);
                if (read == 0) break;
                length += read;
                foreach (var packet in ProtocolJson.Decode(ref buffer, ref length))
                {
                    CurrentClient.Value = clientId;
                    var reply = await _onPacket(packet, SendToThis, _cancel.Token);
                    if (reply != null) await SendToThis(reply);
                }
            }
        }
        catch (Exception)
        {
            // The phone went away.
        }
        finally
        {
            lock (_gate) _senders.Remove(clientId);
            Drop(stream);
            OnClientClosed?.Invoke(clientId);
            client.Dispose();
            writeLock.Dispose();
        }

        async Task SendToThis(Packet packet)
        {
            var bytes = ProtocolJson.Encode(packet);
            try
            {
                await writeLock.WaitAsync(_cancel.Token);
                try
                {
                    await stream.WriteAsync(bytes, _cancel.Token);
                }
                finally
                {
                    writeLock.Release();
                }
            }
            catch { }
        }
    }

    private void Drop(Stream stream)
    {
        bool cleared = false;
        lock (_gate)
        {
            _clients.Remove(stream);
            if (_clients.Count == 0) cleared = true;
        }
        if (cleared) OnClientsCleared?.Invoke();
    }

    private static IEnumerable<string> LanAddresses()
    {
        foreach (var address in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
        {
            if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                yield return address.ToString();
        }
    }
}
