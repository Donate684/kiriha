using System;
using System.Runtime.InteropServices;
using Serilog;

namespace Kiriha.Services.Tracking.Anisthesia;

/// <summary>
/// Per-process state of the audio session in the Windows audio engine.
/// Mirrors the native <c>AudioSessionState</c> enum from <c>audiopolicy.h</c>,
/// plus a <see cref="Unknown"/> fallback we use when no session exists for
/// the queried PID (e.g. the player just launched and hasn't opened audio
/// yet) or when the COM call failed.
/// </summary>
internal enum AudioState
{
    Unknown = -1,
    Inactive = 0, // session is silent Ã¢â‚¬â€ paused, muted internally, or quiet scene
    Active = 1,   // session is currently producing samples
    Expired = 2,  // session ended (process exited / device removed)
}

/// <summary>
/// Reads the Windows audio session state for a given player PID through the
/// CoreAudio APIs (<c>IMMDeviceEnumerator</c> -> default render endpoint ->
/// <c>IAudioSessionManager2</c> -> session enumeration). Used as a generic
/// pause-detection signal: a player whose audio session has been
/// <see cref="AudioState.Inactive"/> for a sustained period has almost
/// certainly been paused (real silent scenes don't last that long).
///
/// All P/Invoke surface is contained in this file so the rest of the codebase
/// only deals with the small <see cref="AudioState"/> enum.
/// </summary>
internal static class AudioSessionProbe
{
    /// <summary>
    /// Returns the current <see cref="AudioState"/> of the audio session
    /// owned by <paramref name="pid"/>, or <see cref="AudioState.Unknown"/>
    /// when no such session exists or the query fails. Safe to call on
    /// non-Windows platforms (returns Unknown). Thread-safe Ã¢â‚¬â€ each call
    /// instantiates fresh COM objects and releases them before returning.
    /// </summary>
    public static AudioState GetStateForPid(uint pid)
    {
        if (!OperatingSystem.IsWindows() || pid == 0) return AudioState.Unknown;

        object? enumeratorObj = null;
        IMMDevice? device = null;
        object? mgrObj = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumeratorObj = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)!);
            var enumerator = (IMMDeviceEnumerator)enumeratorObj!;

            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
            if (hr != 0 || device == null) return AudioState.Unknown;

            var iidMgr = typeof(IAudioSessionManager2).GUID;
            hr = device.Activate(ref iidMgr, 0, IntPtr.Zero, out mgrObj);
            if (hr != 0 || mgrObj == null) return AudioState.Unknown;

            var mgr = (IAudioSessionManager2)mgrObj;
            hr = mgr.GetSessionEnumerator(out sessions);
            if (hr != 0 || sessions == null) return AudioState.Unknown;

            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl? ctl = null;
                try
                {
                    if (sessions.GetSession(i, out ctl) != 0 || ctl == null) continue;
                    var ctl2 = (IAudioSessionControl2)ctl;
                    if (ctl2.GetProcessId(out uint sessionPid) != 0) continue;
                    if (sessionPid != pid) continue;

                    if (ctl.GetState(out int rawState) != 0) return AudioState.Unknown;
                    return rawState switch
                    {
                        0 => AudioState.Inactive,
                        1 => AudioState.Active,
                        2 => AudioState.Expired,
                        _ => AudioState.Unknown,
                    };
                }
                finally
                {
                    if (ctl != null) Marshal.ReleaseComObject(ctl);
                }
            }
            return AudioState.Unknown;
        }
        catch (Exception ex)
        {
            Log.Debug("AudioSessionProbe: query failed for PID {Pid}: {Msg}", pid, ex.Message);
            return AudioState.Unknown;
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (mgrObj != null) Marshal.ReleaseComObject(mgrObj);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumeratorObj != null) Marshal.ReleaseComObject(enumeratorObj);
        }
    }

    // Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
    // CoreAudio COM interop. Only the minimal surface we need to walk
    // from the default endpoint to per-session state is declared here.
    // Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

    private static readonly Guid CLSID_MMDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);

        // Remaining methods omitted Ã¢â‚¬â€ we only need the default endpoint.
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
                     [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // Two predecessor methods on IAudioSessionManager that we must
        // declare in order to keep the v-table layout correct.
        int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);
        int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

        // Remaining methods (RegisterSessionNotification etc.) omitted.
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int retVal);

        // Remaining methods omitted.
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl predecessors Ã¢â‚¬â€ keep v-table aligned.
        int GetState(out int retVal);
        int GetDisplayName(out IntPtr retVal);
        int SetDisplayName(string value, ref Guid eventContext);
        int GetIconPath(out IntPtr retVal);
        int SetIconPath(string value, ref Guid eventContext);
        int GetGroupingParam(out Guid retVal);
        int SetGroupingParam(ref Guid grouping, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr newNotifications);
        int UnregisterAudioSessionNotification(IntPtr newNotifications);

        int GetSessionIdentifier(out IntPtr retVal);
        int GetSessionInstanceIdentifier(out IntPtr retVal);

        [PreserveSig]
        int GetProcessId(out uint retVal);

        // Remaining methods (IsSystemSoundsSession, SetDuckingPreference) omitted.
    }
}
