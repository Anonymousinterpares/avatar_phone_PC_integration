using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Bluey.Audio;

namespace Bluey.Host;

/// <summary>
/// Plays audio on the physical speakers even when Windows' default device
/// has been switched to a silent endpoint for phone streaming.
/// </summary>
public sealed class PhysicalSpeaker : IDisposable
{
    private readonly object _gate = new();
    private float[] _samples = new float[48000];
    private int _count;
    private int _rate = 16000;
    private double _pos;
    private Thread? _thread;
    private volatile bool _running;

    private IAudioClient? _client;
    private IAudioRenderClient? _render;
    private string? _deviceId;
    private int _mixRate;
    private int _channels;
    private bool _float;

    public void Enqueue(float[] mono, int sampleRate)
    {
        if (mono.Length == 0 || sampleRate < 8000) return;
        lock (_gate)
        {
            if (_count == 0 || _rate != sampleRate)
            {
                _count = 0;
                _pos = 0;
                _rate = sampleRate;
            }
            var cap = _rate * 3;
            var incoming = mono.Length;
            if (_count >= cap) return;
            if (_count + incoming > cap) incoming = cap - _count;
            if (_samples.Length < _count + incoming)
            {
                var grown = new float[Math.Max(_samples.Length * 2, _count + incoming)];
                Array.Copy(_samples, grown, _count);
                _samples = grown;
            }
            Array.Copy(mono, 0, _samples, _count, incoming);
            _count += incoming;
            if (_thread == null)
            {
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "bluey-speakers" };
                _thread.Start();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _count = 0;
            _pos = 0;
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(800); } catch { }
        _thread = null;
        CloseClient();
    }

    public static bool TryReadWav(string path, out float[] mono, out int sampleRate)
    {
        mono = Array.Empty<float>();
        sampleRate = 0;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44) return false;
            if (Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF") return false;
            if (Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") return false;
            var pos = 12;
            short format = 0, channels = 0, bits = 0;
            var rate = 0;
            var data = -1;
            var dataLen = 0;
            while (pos + 8 <= bytes.Length)
            {
                var id = Encoding.ASCII.GetString(bytes, pos, 4);
                var size = BitConverter.ToInt32(bytes, pos + 4);
                var body = pos + 8;
                if (size < 0 || body > bytes.Length) break;
                var available = Math.Min(size, bytes.Length - body);
                if (id == "fmt " && available >= 16)
                {
                    format = BitConverter.ToInt16(bytes, body);
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                    if (format == -2 && available >= 26)
                        format = BitConverter.ToInt16(bytes, body + 24);
                }
                else if (id == "data")
                {
                    data = body;
                    dataLen = available;
                }
                pos = body + size + (size & 1);
            }
            if (data < 0 || channels < 1 || rate < 8000 || bits < 8) return false;
            var bytesPerSample = bits / 8;
            var frameBytes = bytesPerSample * channels;
            if (frameBytes <= 0) return false;
            var frames = dataLen / frameBytes;
            if (frames <= 0) return false;
            var isFloat = format == 3;
            mono = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++)
                {
                    var offset = data + (i * channels + c) * bytesPerSample;
                    if (isFloat && bits == 32) sum += BitConverter.ToSingle(bytes, offset);
                    else if (bits == 16) sum += BitConverter.ToInt16(bytes, offset) / 32768f;
                    else if (!isFloat && bits == 32) sum += BitConverter.ToInt32(bytes, offset) / 2147483648f;
                    else if (bits == 8) sum += (bytes[offset] - 128) / 128f;
                    else return false;
                }
                mono[i] = sum / channels;
            }
            sampleRate = rate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Loop()
    {
        try { CoInitializeEx(IntPtr.Zero, 0); } catch { }
        while (_running)
        {
            try
            {
                if (!EnsureClient())
                {
                    Thread.Sleep(200);
                    continue;
                }
                if (_client!.GetCurrentPadding(out var padding) != 0 || _client.GetBufferSize(out var size) != 0 || size == 0)
                {
                    CloseClient();
                    Thread.Sleep(200);
                    continue;
                }
                var available = size - padding;
                if (available < size / 8)
                {
                    Thread.Sleep(8);
                    continue;
                }
                var write = Math.Min(available, (uint)Math.Max(1, _mixRate / 50));
                if (_render!.GetBuffer(write, out var data) != 0)
                {
                    CloseClient();
                    continue;
                }
                var audible = WriteFrames(data, (int)write);
                _render.ReleaseBuffer(write, audible ? 0u : 2u);
                Thread.Sleep(8);
            }
            catch
            {
                CloseClient();
                Thread.Sleep(200);
            }
        }
        CloseClient();
    }

    private bool EnsureClient()
    {
        var id = AudioDeviceManager.PhysicalPlaybackId();
        if (_client != null && string.Equals(_deviceId, id, StringComparison.OrdinalIgnoreCase)) return true;
        CloseClient();
        if (!string.IsNullOrEmpty(id) && Open(id)) return true;
        var fallback = AudioDeviceManager.GetPhysicalDefaultDeviceId();
        return !string.IsNullOrEmpty(fallback)
            && !string.Equals(fallback, id, StringComparison.OrdinalIgnoreCase)
            && Open(fallback);
    }

    private bool Open(string deviceId)
    {
        var enumType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        if (enumType == null) return false;
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumType)!;
        if (enumerator.GetDevice(deviceId, out var device) != 0 || device == null) return false;
        var iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        if (device.Activate(ref iidClient, 1, IntPtr.Zero, out var activated) != 0 || activated is not IAudioClient client)
            return false;
        if (client.GetMixFormat(out var formatPtr) != 0 || formatPtr == IntPtr.Zero)
            return false;
        try
        {
            if (!ReadMix(formatPtr)) return false;
            if (client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, 0, 4_000_000, 0, formatPtr, Guid.Empty) != 0)
                return false;
        }
        finally
        {
            Marshal.FreeCoTaskMem(formatPtr);
        }
        var iidRender = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
        if (client.GetService(ref iidRender, out var service) != 0 || service is not IAudioRenderClient render)
            return false;
        if (client.Start() != 0) return false;
        _client = client;
        _render = render;
        _deviceId = deviceId;
        return true;
    }

    private bool ReadMix(IntPtr formatPtr)
    {
        var format = Marshal.PtrToStructure<WAVEFORMATEX>(formatPtr);
        _mixRate = (int)format.nSamplesPerSec;
        _channels = format.nChannels;
        if (_mixRate < 8000 || _channels < 1 || _channels > 8) return false;
        var tag = (int)format.wFormatTag;
        if (format.wFormatTag == 0xFFFE)
        {
            if (format.cbSize < 22) return false;
            tag = Marshal.ReadInt16(formatPtr, 24);
        }
        if (tag == 3 && format.wBitsPerSample == 32)
        {
            _float = true;
            return true;
        }
        if (tag == 1 && format.wBitsPerSample == 16)
        {
            _float = false;
            return true;
        }
        return false;
    }

    private unsafe bool WriteFrames(IntPtr dest, int frames)
    {
        var audible = false;
        var step = 0.0;
        lock (_gate)
        {
            step = _mixRate > 0 ? (double)_rate / _mixRate : 1;
            for (var i = 0; i < frames; i++)
            {
                float sample = 0;
                var index = (int)_pos;
                if (index + 1 < _count)
                {
                    var frac = (float)(_pos - index);
                    sample = _samples[index] * (1f - frac) + _samples[index + 1] * frac;
                    _pos += step;
                    audible = true;
                }
                else if (index < _count)
                {
                    sample = _samples[index];
                    _pos += step;
                    audible = true;
                }
                sample = Math.Clamp(sample, -1f, 1f);
                if (_float)
                {
                    var target = (float*)dest + i * _channels;
                    for (var c = 0; c < _channels; c++) target[c] = sample;
                }
                else
                {
                    var value = (short)Math.Clamp((int)(sample * 32767f), short.MinValue, short.MaxValue);
                    var target = (short*)dest + i * _channels;
                    for (var c = 0; c < _channels; c++) target[c] = value;
                }
            }
            var drop = (int)_pos;
            if (drop > 0)
            {
                if (drop >= _count)
                {
                    _count = 0;
                    _pos = 0;
                }
                else
                {
                    Array.Copy(_samples, drop, _samples, 0, _count - drop);
                    _count -= drop;
                    _pos -= drop;
                }
            }
        }
        return audible;
    }

    private void CloseClient()
    {
        try { _client?.Stop(); } catch { }
        _client = null;
        _render = null;
        _deviceId = null;
    }

    private enum AUDCLNT_SHAREMODE { AUDCLNT_SHAREMODE_SHARED, AUDCLNT_SHAREMODE_EXCLUSIVE }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate([In] ref Guid iid, uint clsCtx, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(AUDCLNT_SHAREMODE shareMode, uint flags, long bufferDuration, long periodicity, IntPtr format, [MarshalAs(UnmanagedType.LPStruct)] Guid session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(AUDCLNT_SHAREMODE shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService([In] ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint model);
}
