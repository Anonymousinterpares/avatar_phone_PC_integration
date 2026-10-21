using System.Runtime.InteropServices;

namespace Bluey.Host;

/// <summary>Microphone capture as 16 kHz mono PCM for the computer being helped.</summary>
public sealed class HelperMic : IDisposable
{
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint CallbackFunction = 0x00030000;
    private const uint WimData = 0x3C0;
    private readonly object _gate = new();
    private WaveInProc? _proc;
    private IntPtr _wave;
    private bool _open;
    private readonly IntPtr[] _headers = new IntPtr[3];
    private readonly GCHandle[] _pins = new GCHandle[3];

    public event Action<byte[]>? Pcm;

    public void Start()
    {
        Stop();
        _proc = OnWave;
        var format = new WAVEFORMATEX
        {
            wFormatTag = 1,
            nChannels = 1,
            nSamplesPerSec = 16000,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = 32000,
        };
        if (waveInOpen(out _wave, WaveMapper, ref format, Marshal.GetFunctionPointerForDelegate(_proc), IntPtr.Zero, CallbackFunction) != 0)
        {
            _wave = IntPtr.Zero;
            return;
        }
        var headerSize = (uint)Marshal.SizeOf<WAVEHDR>();
        for (var i = 0; i < _headers.Length; i++)
        {
            var bytes = new byte[1280];
            _pins[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var header = new WAVEHDR { lpData = _pins[i].AddrOfPinnedObject(), dwBufferLength = (uint)bytes.Length };
            _headers[i] = Marshal.AllocHGlobal((int)headerSize);
            Marshal.StructureToPtr(header, _headers[i], false);
            waveInPrepareHeader(_wave, _headers[i], headerSize);
            waveInAddBuffer(_wave, _headers[i], headerSize);
        }
        waveInStart(_wave);
        _open = true;
    }

    public void Stop()
    {
        lock (_gate)
        {
            _open = false;
            if (_wave != IntPtr.Zero)
            {
                try { waveInStop(_wave); } catch { }
                try { waveInReset(_wave); } catch { }
                var headerSize = (uint)Marshal.SizeOf<WAVEHDR>();
                foreach (var header in _headers)
                {
                    if (header != IntPtr.Zero)
                    {
                        try { waveInUnprepareHeader(_wave, header, headerSize); } catch { }
                    }
                }
                try { waveInClose(_wave); } catch { }
                _wave = IntPtr.Zero;
            }
            for (var i = 0; i < _headers.Length; i++)
            {
                if (_headers[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_headers[i]);
                    _headers[i] = IntPtr.Zero;
                }
                if (_pins[i].IsAllocated) _pins[i].Free();
            }
        }
    }

    public void Dispose() => Stop();

    private void OnWave(IntPtr wave, uint message, IntPtr instance, IntPtr headerPtr, IntPtr reserved)
    {
        if (message != WimData || headerPtr == IntPtr.Zero || !_open) return;
        try
        {
            var header = Marshal.PtrToStructure<WAVEHDR>(headerPtr);
            var count = (int)header.dwBytesRecorded;
            if (count > 0 && header.lpData != IntPtr.Zero)
            {
                var copy = new byte[count];
                Marshal.Copy(header.lpData, copy, 0, count);
                Pcm?.Invoke(copy);
            }
            if (_open) waveInAddBuffer(wave, headerPtr, (uint)Marshal.SizeOf<WAVEHDR>());
        }
        catch
        {
            // The microphone stopped.
        }
    }

    private delegate void WaveInProc(IntPtr wave, uint message, IntPtr instance, IntPtr header, IntPtr reserved);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr wave, uint device, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr wave);
    [DllImport("winmm.dll")] private static extern uint waveInStop(IntPtr wave);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr wave);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr wave);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr wave, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr wave, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr wave, IntPtr header, uint size);
}
