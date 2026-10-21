using System.Runtime.InteropServices;

namespace Bluey.Audio;

public sealed record PlaybackDevice(string Id, string Name, bool IsDefault, bool IsLikelySilentOrVirtual);

public static class AudioDeviceManager
{
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    private static readonly PropertyKey PkeyDeviceFriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14,
    };
    private static readonly PropertyKey PkeyDeviceDesc = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 2,
    };

    private static readonly object _lock = new();
    private static string? _lastReroutedOriginalDeviceId;

    static AudioDeviceManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_lastReroutedOriginalDeviceId))
                {
                    try { SetDefaultPlaybackDeviceInternal(_lastReroutedOriginalDeviceId); } catch { }
                    _lastReroutedOriginalDeviceId = null;
                }
            }
        };
    }

    public static List<PlaybackDevice> ListPlaybackDevices()
    {
        var list = new List<PlaybackDevice>();
        try
        {
            var enumType = Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator);
            if (enumType == null) return list;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumType)!;

            string defaultId = "";
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defDev) == 0 && defDev != null)
                {
                    defDev.GetId(out defaultId);
                }
            }
            catch { }

            if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out var collection) == 0 && collection != null)
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var dev) == 0 && dev != null)
                    {
                        dev.GetId(out string id);
                        string name = id;
                        try
                        {
                            if (dev.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0 && store != null)
                            {
                                var key = PkeyDeviceFriendlyName;
                                if (store.GetValue(ref key, out var pv) == 0 && pv.pwszVal != IntPtr.Zero)
                                {
                                    name = Marshal.PtrToStringUni(pv.pwszVal) ?? id;
                                    PropVariantClear(ref pv);
                                }
                                else
                                {
                                    var key2 = PkeyDeviceDesc;
                                    if (store.GetValue(ref key2, out var pv2) == 0 && pv2.pwszVal != IntPtr.Zero)
                                    {
                                        name = Marshal.PtrToStringUni(pv2.pwszVal) ?? id;
                                        PropVariantClear(ref pv2);
                                    }
                                }
                            }
                        }
                        catch { }

                        bool isDefault = !string.IsNullOrEmpty(defaultId) && string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                        bool isVirtual = IsVirtualOrSilentDevice(name);

                        list.Add(new PlaybackDevice(id, name, isDefault, isVirtual));
                    }
                }
            }
        }
        catch { }

        return list;
    }

    /// <summary>
    /// The speakers a person can hear. While PC audio is muted, that is the device
    /// Bluey will restore, not the silent device currently set as the Windows default.
    /// </summary>
    public static string? PhysicalPlaybackId()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_lastReroutedOriginalDeviceId))
                return _lastReroutedOriginalDeviceId;
        }
        return GetPhysicalDefaultDeviceId();
    }

    public static string? GetDefaultDeviceId()
    {
        var devices = ListPlaybackDevices();
        return devices.FirstOrDefault(d => d.IsDefault)?.Id;
    }

    /// <summary>
    /// Gets the primary physical/audible playback device ID (non-virtual).
    /// </summary>
    public static string? GetPhysicalDefaultDeviceId()
    {
        var devices = ListPlaybackDevices();
        var current = devices.FirstOrDefault(d => d.IsDefault);
        if (current != null && !current.IsLikelySilentOrVirtual)
        {
            return current.Id;
        }

        var physical = devices.FirstOrDefault(d => !d.IsLikelySilentOrVirtual);
        return physical?.Id ?? current?.Id;
    }

    /// <summary>
    /// Switches the default playback device to a virtual/silent reroute device.
    /// Provides both the target virtual device ID (for loopback capture) and the original physical device ID (for restore).
    /// If the current default is already virtual/silent, it maintains that device and identifies the physical restore device.
    /// </summary>
    public static bool SwitchToRerouteDevice(out string? targetDeviceId, out string? originalDeviceId)
    {
        targetDeviceId = null;
        originalDeviceId = null;
        lock (_lock)
        {
            var devices = ListPlaybackDevices();
            if (devices.Count == 0) return false;

            var currentDefault = devices.FirstOrDefault(d => d.IsDefault);
            var physicalDevice = devices.FirstOrDefault(d => !d.IsLikelySilentOrVirtual);

            // Determine the original physical device to restore to later
            originalDeviceId = (!currentDefault?.IsLikelySilentOrVirtual ?? false)
                ? currentDefault!.Id
                : (_lastReroutedOriginalDeviceId ?? physicalDevice?.Id ?? currentDefault?.Id);

            // If the current default is ALREADY a virtual/silent device, we are already rerouted!
            if (currentDefault != null && currentDefault.IsLikelySilentOrVirtual)
            {
                targetDeviceId = currentDefault.Id;
                _lastReroutedOriginalDeviceId = originalDeviceId;
                return true;
            }

            // Find all candidate virtual/silent devices that can be set as default
            var virtualCandidates = devices
                .Where(d => d.IsLikelySilentOrVirtual && !d.IsDefault)
                .OrderByDescending(d => d.Name.Contains("Oculus", StringComparison.OrdinalIgnoreCase) ? 10 : 1)
                .ThenBy(d => d.Name)
                .ToList();

            foreach (var cand in virtualCandidates)
            {
                if (SetDefaultPlaybackDeviceInternal(cand.Id))
                {
                    targetDeviceId = cand.Id;
                    _lastReroutedOriginalDeviceId = originalDeviceId;
                    return true;
                }
            }

            // Fallback: If no virtual device succeeded, try any other non-default device
            var origId = originalDeviceId;
            var otherCandidates = devices
                .Where(d => !d.IsDefault && !string.Equals(d.Id, origId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var cand in otherCandidates)
            {
                if (SetDefaultPlaybackDeviceInternal(cand.Id))
                {
                    targetDeviceId = cand.Id;
                    _lastReroutedOriginalDeviceId = originalDeviceId;
                    return true;
                }
            }

            return false;
        }
    }

    public static bool RestoreDefaultDevice(string? originalDeviceId)
    {
        lock (_lock)
        {
            var target = originalDeviceId ?? _lastReroutedOriginalDeviceId ?? GetPhysicalDefaultDeviceId();
            if (string.IsNullOrWhiteSpace(target)) return false;

            bool ok = SetDefaultPlaybackDeviceInternal(target);
            if (ok)
            {
                _lastReroutedOriginalDeviceId = null;
            }
            return ok;
        }
    }

    /// <summary>
    /// Ensures that the PC's default playback device is set to a physical speaker/headphones,
    /// restoring normal PC audio if the PC was previously left in a rerouted virtual state.
    /// </summary>
    public static void EnsurePhysicalDefault()
    {
        lock (_lock)
        {
            var devices = ListPlaybackDevices();
            var current = devices.FirstOrDefault(d => d.IsDefault);
            if (current != null && current.IsLikelySilentOrVirtual)
            {
                var physical = GetPhysicalDefaultDeviceId();
                if (!string.IsNullOrEmpty(physical) && physical != current.Id)
                {
                    SetDefaultPlaybackDeviceInternal(physical);
                }
            }
        }
    }

    public static bool SetDefaultPlaybackDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        lock (_lock)
        {
            return SetDefaultPlaybackDeviceInternal(deviceId);
        }
    }

    private static bool SetDefaultPlaybackDeviceInternal(string deviceId)
    {
        bool success = false;
        var thread = new Thread(() =>
        {
            try
            {
                var client = new CPolicyConfigClient();
                if (client is IPolicyConfig10 pc10)
                {
                    int hr1 = pc10.SetDefaultEndpoint(deviceId, ERole.eConsole);
                    int hr2 = pc10.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                    int hr3 = pc10.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                    if (hr1 == 0 && hr2 == 0) { success = true; return; }
                }

                if (client is IPolicyConfig pc)
                {
                    int hr1 = pc.SetDefaultEndpoint(deviceId, ERole.eConsole);
                    int hr2 = pc.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                    int hr3 = pc.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                    if (hr1 == 0 && hr2 == 0) { success = true; return; }
                }
            }
            catch { }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return success;
    }

    private static bool IsVirtualOrSilentDevice(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("cable") ||
               lower.Contains("virtual") ||
               lower.Contains("voice.ai") ||
               lower.Contains("oculus") ||
               lower.Contains("digital") ||
               lower.Contains("s/pdif") ||
               lower.Contains("null");
    }

    #region COM Interfaces

    private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate([In] ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;
    }

    [ComImport, Guid("CA286FC3-91FD-42C3-8E9B-CAAFA66242E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig10
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppEndpointFormat, IntPtr ppMixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr pKey, IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr pKey, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, bool bVisible);
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool bVisible);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class CPolicyConfigClient { }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    #endregion
}
