using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bluey.Audio;
using Bluey.Protocol;

namespace Bluey.Host;

/// <summary>
/// Captures PC audio playback via WASAPI Loopback and streams it to the phone.
/// Supports configurable sample rate, channel count, and optional PC speaker rerouting
/// (switching default audio playback device to a virtual/silent endpoint so PC speakers
/// remain silent while full unattenuated audio streams cleanly to the phone).
/// </summary>
public sealed class AudioStreamer : IDisposable
{
    private CancellationTokenSource? _cancel;
    private Task? _loop;
    private Func<Packet, Task>? _send;

    private int _targetRate = 24000;
    private int _targetChannels = 1;
    private bool _mutePc;
    private string? _originalDefaultDeviceId;

    public bool Running => _cancel != null;

    public void Start(Func<Packet, Task> send, int sampleRate, int channels, bool mutePc)
    {
        Stop();
        _send = send;
        _targetRate = sampleRate switch
        {
            <= 16000 => 16000,
            >= 44100 => 44100,
            _ => 24000
        };
        _targetChannels = channels == 2 ? 2 : 1;
        _mutePc = mutePc;

        if (_mutePc && _originalDefaultDeviceId == null)
        {
            AudioDeviceManager.SwitchToRerouteDevice(out _originalDefaultDeviceId);
            Thread.Sleep(60);
        }

        _cancel = new CancellationTokenSource();
        _loop = Task.Run(() => CaptureLoop(_cancel.Token));
    }

    public void UpdateSettings(int sampleRate, int channels, bool mutePc)
    {
        bool rerouteChanged = _mutePc != mutePc;
        _targetRate = sampleRate switch
        {
            <= 16000 => 16000,
            >= 44100 => 44100,
            _ => 24000
        };
        _targetChannels = channels == 2 ? 2 : 1;
        _mutePc = mutePc;

        if (rerouteChanged && Running && _send != null)
        {
            // Restart capture loop to bind to the newly selected default device
            Start(_send, _targetRate, _targetChannels, _mutePc);
        }
    }

    public void Stop()
    {
        _cancel?.Cancel();
        try { _loop?.Wait(2000); } catch { }
        _cancel?.Dispose();
        _cancel = null;
        _loop = null;

        if (_originalDefaultDeviceId != null)
        {
            AudioDeviceManager.RestoreDefaultDevice(_originalDefaultDeviceId);
            _originalDefaultDeviceId = null;
        }
    }

    public void Dispose() => Stop();

    private async Task CaptureLoop(CancellationToken token)
    {
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        IntPtr pMixFormat = IntPtr.Zero;

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
            if (hr != 0 || device == null) return;

            var iidAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            hr = device.Activate(ref iidAudioClient, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var acObj);
            if (hr != 0 || acObj is not IAudioClient client) return;
            audioClient = client;

            hr = audioClient.GetMixFormat(out pMixFormat);
            if (hr != 0 || pMixFormat == IntPtr.Zero) return;

            var mixFormat = Marshal.PtrToStructure<WAVEFORMATEX>(pMixFormat);
            int srcRate = (int)mixFormat.nSamplesPerSec;
            int srcChannels = mixFormat.nChannels;
            bool isFloat = mixFormat.wFormatTag == 3 /* IEEE_FLOAT */ ||
                           (mixFormat.wFormatTag == 0xFFFE && mixFormat.wBitsPerSample == 32);

            // 100ms capture buffer in 100ns units
            long hnsBufferDuration = 10_000_000;
            hr = audioClient.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_LOOPBACK,
                hnsBufferDuration, 0, pMixFormat, Guid.Empty);
            if (hr != 0) return;

            var iidCaptureClient = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            hr = audioClient.GetService(ref iidCaptureClient, out var ccObj);
            if (hr != 0 || ccObj is not IAudioCaptureClient capture) return;
            captureClient = capture;

            // Keep the Windows Audio engine mixer continuously active with a silent render stream.
            // By design, WASAPI loopback only receives clocking when the audio engine has an active stream.
            IAudioClient? renderClient = null;
            IAudioRenderClient? renderService = null;
            try
            {
                var iidRenderClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
                if (device.Activate(ref iidRenderClient, 1, IntPtr.Zero, out var rAcObj) == 0 && rAcObj is IAudioClient rc)
                {
                    renderClient = rc;
                    if (renderClient.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, 0, 10_000_000, 0, pMixFormat, Guid.Empty) == 0)
                    {
                        var iidRenderService = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
                        if (renderClient.GetService(ref iidRenderService, out var rsObj) == 0 && rsObj is IAudioRenderClient rs)
                        {
                            renderService = rs;
                            renderClient.GetBufferSize(out var bufFrames);
                            if (renderService.GetBuffer(bufFrames, out _) == 0)
                            {
                                renderService.ReleaseBuffer(bufFrames, 0x00000002 /* AUDCLNT_BUFFERFLAGS_SILENT */);
                            }
                            renderClient.Start();
                        }
                    }
                }
            }
            catch { }

            audioClient.Start();

            var chunkBuffer = new MemoryStream();
            int chunkThresholdBytes = (_targetRate * _targetChannels * 2 * 40) / 1000; // ~40ms chunk

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Feed silent render stream to keep mixer clocked
                    if (renderClient != null && renderService != null)
                    {
                        if (renderClient.GetCurrentPadding(out var padding) == 0 &&
                            renderClient.GetBufferSize(out var totalBuf) == 0)
                        {
                            uint needed = totalBuf - padding;
                            if (needed > 0 && renderService.GetBuffer(needed, out _) == 0)
                            {
                                renderService.ReleaseBuffer(needed, 0x00000002 /* AUDCLNT_BUFFERFLAGS_SILENT */);
                            }
                        }
                    }

                    while (!token.IsCancellationRequested)
                    {
                        hr = captureClient.GetNextPacketSize(out var nextPacketSize);
                        if (hr != 0 || nextPacketSize == 0) break;

                        hr = captureClient.GetBuffer(out var pData, out var numFramesToRead, out var flags, out _, out _);
                        if (hr != 0) break;

                        bool isSilent = (flags & AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0;

                        if (numFramesToRead > 0)
                        {
                            var processed = ProcessFrames(pData, (int)numFramesToRead, srcRate, srcChannels, isFloat, isSilent, _targetRate, _targetChannels);
                            if (processed.Length > 0)
                            {
                                chunkBuffer.Write(processed, 0, processed.Length);
                            }
                        }

                        captureClient.ReleaseBuffer(numFramesToRead);
                    }

                    if (chunkBuffer.Length >= chunkThresholdBytes && _send != null)
                    {
                        var pcmBytes = chunkBuffer.ToArray();
                        chunkBuffer.SetLength(0);

                        var base64 = Convert.ToBase64String(pcmBytes);
                        var packet = new Packet
                        {
                            Command = "deskAudio",
                            Audio = base64,
                            Text = $"{{\"rate\":{_targetRate},\"ch\":{_targetChannels}}}",
                        };
                        await _send(packet);
                    }

                    await Task.Delay(20, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception)
                {
                    try { await Task.Delay(40, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            try { audioClient?.Stop(); } catch { }
            if (pMixFormat != IntPtr.Zero)
            {
                try { Marshal.FreeCoTaskMem(pMixFormat); } catch { }
            }
        }
    }

    private static unsafe byte[] ProcessFrames(
        IntPtr pSrcData,
        int srcFrames,
        int srcRate,
        int srcChannels,
        bool isFloat,
        bool isSilent,
        int dstRate,
        int dstChannels)
    {
        if (srcFrames <= 0) return Array.Empty<byte>();

        // Calculate destination frame count
        int dstFrames = (int)Math.Round((double)srcFrames * dstRate / srcRate);
        if (dstFrames <= 0) dstFrames = 1;

        var result = new byte[dstFrames * dstChannels * 2];
        if (isSilent || pSrcData == IntPtr.Zero)
        {
            // Already zero-filled
            return result;
        }

        fixed (byte* pDstBytes = result)
        {
            short* pDst = (short*)pDstBytes;

            for (int iDst = 0; iDst < dstFrames; iDst++)
            {
                double srcPos = (double)iDst * srcRate / dstRate;
                int i0 = (int)srcPos;
                int i1 = Math.Min(i0 + 1, srcFrames - 1);
                float frac = (float)(srcPos - i0);

                if (dstChannels == 1)
                {
                    // Downmix to Mono
                    float sample0 = ReadMonoSample(pSrcData, i0, srcChannels, isFloat);
                    float sample1 = ReadMonoSample(pSrcData, i1, srcChannels, isFloat);
                    float interp = sample0 * (1f - frac) + sample1 * frac;
                    pDst[iDst] = (short)Math.Clamp((int)(interp * 32767f), -32768, 32767);
                }
                else
                {
                    // Stereo
                    float left0 = ReadChannelSample(pSrcData, i0, 0, srcChannels, isFloat);
                    float left1 = ReadChannelSample(pSrcData, i1, 0, srcChannels, isFloat);
                    float right0 = ReadChannelSample(pSrcData, i0, srcChannels > 1 ? 1 : 0, srcChannels, isFloat);
                    float right1 = ReadChannelSample(pSrcData, i1, srcChannels > 1 ? 1 : 0, srcChannels, isFloat);

                    float leftInterp = left0 * (1f - frac) + left1 * frac;
                    float rightInterp = right0 * (1f - frac) + right1 * frac;

                    pDst[iDst * 2] = (short)Math.Clamp((int)(leftInterp * 32767f), -32768, 32767);
                    pDst[iDst * 2 + 1] = (short)Math.Clamp((int)(rightInterp * 32767f), -32768, 32767);
                }
            }
        }

        return result;
    }

    private static unsafe float ReadChannelSample(IntPtr pData, int frameIndex, int channel, int totalChannels, bool isFloat)
    {
        int offset = frameIndex * totalChannels + channel;
        if (isFloat)
        {
            float* pFloat = (float*)pData;
            return pFloat[offset];
        }
        else
        {
            short* pShort = (short*)pData;
            return pShort[offset] / 32768f;
        }
    }

    private static unsafe float ReadMonoSample(IntPtr pData, int frameIndex, int totalChannels, bool isFloat)
    {
        if (totalChannels == 1)
        {
            return ReadChannelSample(pData, frameIndex, 0, 1, isFloat);
        }

        float left = ReadChannelSample(pData, frameIndex, 0, totalChannels, isFloat);
        float right = ReadChannelSample(pData, frameIndex, 1, totalChannels, isFloat);
        return (left + right) * 0.5f;
    }

    #region COM Interop Definitions

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole { eConsole, eMultimedia, eCommunications }

    private enum AUDCLNT_SHAREMODE { AUDCLNT_SHAREMODE_SHARED, AUDCLNT_SHAREMODE_EXCLUSIVE }

    [Flags]
    private enum AUDCLNT_STREAMFLAGS : uint
    {
        AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000,
        AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000,
    }

    [Flags]
    private enum AUDCLNT_BUFFERFLAGS
    {
        AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY = 0x1,
        AUDCLNT_BUFFERFLAGS_SILENT = 0x2,
        AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR = 0x4
    }

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
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate([In] ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(AUDCLNT_SHAREMODE ShareMode, AUDCLNT_STREAMFLAGS StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, [MarshalAs(UnmanagedType.LPStruct)] Guid AudioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig] int IsFormatSupported(AUDCLNT_SHAREMODE ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out AUDCLNT_BUFFERFLAGS pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        [PreserveSig] int ReleaseBuffer(uint NumFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    [ComImport]
    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint NumFramesRequested, out IntPtr ppData);
        [PreserveSig] int ReleaseBuffer(uint NumFramesWritten, uint dwFlags);
    }

    #endregion
}
