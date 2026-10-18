using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bluey.Protocol;
using Makaretu.Dns;

namespace Bluey.Host;

public sealed class PhoneLink : IDisposable
{
    private readonly Func<Packet, Func<Packet, Task>, CancellationToken, Task<Packet?>> _onPacket;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<Stream> _clients = new();
    private readonly object _gate = new();
    private ServiceDiscovery? _discovery;

    public PhoneLink(Func<Packet, Func<Packet, Task>, CancellationToken, Task<Packet?>> onPacket)
    {
        _onPacket = onPacket;
        _listener = new TcpListener(IPAddress.Any, 0);
    }

    public int Port { get; private set; }
    public string AddressText { get; private set; } = "";

    public void Start()
    {
        _listener.Start();
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

    public void Dispose()
    {
        _cancel.Cancel();
        _discovery?.Dispose();
        _listener.Stop();
    }

    private async Task AcceptLoop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cancel.Token); }
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
        var buffer = new byte[64 * 1024];
        var length = 0;
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
            Drop(stream);
            client.Dispose();
        }

        Task SendToThis(Packet packet) => stream.WriteAsync(ProtocolJson.Encode(packet)).AsTask();
    }

    private void Drop(Stream stream)
    {
        lock (_gate) _clients.Remove(stream);
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
