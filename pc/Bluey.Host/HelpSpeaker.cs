using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Bluey.Host;

/// <summary>Plays 16 kHz mono PCM from the helper's phone on the PC speakers.</summary>
public sealed class HelpSpeaker : IDisposable
{
    private const int Rate = 16000;
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint WhdrDone = 0x00000001;
    private readonly object _gate = new();
    private readonly Queue<Pending> _pending = new();
    private IntPtr _wave;
    private bool _open;

    public void Play(byte[] pcm)
    {
        if (pcm.Length == 0 || pcm.Length > 32000) return;
        lock (_gate)
        {
            if (!_open && !Open()) return;
            Reap();
            if (_pending.Count > 25) return;

            var copy = (byte[])pcm.Clone();
            var pin = GCHandle.Alloc(copy, GCHandleType.Pinned);
            var header = new WAVEHDR
            {
                lpData = pin.AddrOfPinnedObject(),
                dwBufferLength = (uint)copy.Length,
            };
            var headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
            Marshal.StructureToPtr(header, headerPtr, false);
            var size = (uint)Marshal.SizeOf<WAVEHDR>();
            if (waveOutPrepareHeader(_wave, headerPtr, size) != 0 || waveOutWrite(_wave, headerPtr, size) != 0)
            {
                waveOutUnprepareHeader(_wave, headerPtr, size);
                pin.Free();
                Marshal.FreeHGlobal(headerPtr);
                return;
            }
            _pending.Enqueue(new Pending(headerPtr, pin));
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (!_open) return;
            waveOutReset(_wave);
            Reap(force: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_open)
            {
                waveOutReset(_wave);
                Reap(force: true);
                waveOutClose(_wave);
                _wave = IntPtr.Zero;
                _open = false;
            }
        }
    }

    private bool Open()
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag = 1,
            nChannels = 1,
            nSamplesPerSec = Rate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = Rate * 2,
        };
        if (waveOutOpen(out _wave, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
        {
            _wave = IntPtr.Zero;
            return false;
        }
        _open = true;
        return true;
    }

    private void Reap(bool force = false)
    {
        var size = (uint)Marshal.SizeOf<WAVEHDR>();
        while (_pending.Count > 0)
        {
            var pending = _pending.Peek();
            var header = Marshal.PtrToStructure<WAVEHDR>(pending.Header);
            if (!force && (header.dwFlags & WhdrDone) == 0) break;
            _pending.Dequeue();
            waveOutUnprepareHeader(_wave, pending.Header, size);
            if (pending.Pin.IsAllocated) pending.Pin.Free();
            Marshal.FreeHGlobal(pending.Header);
        }
    }

    private sealed class Pending(IntPtr header, GCHandle pin)
    {
        public IntPtr Header { get; } = header;
        public GCHandle Pin { get; } = pin;
    }

    [StructLayout(LayoutKind.Sequential)]
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

    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr phwo, uint device, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr hwo, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr hwo, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr hwo, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr hwo);
}
