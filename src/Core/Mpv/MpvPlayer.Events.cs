using System;
using System.Runtime.InteropServices;

namespace Kiriha.Core.Mpv;

public partial class MpvPlayer
{
    private void HandleEvent(MpvEvent mpvEvent)
    {
        switch (mpvEvent.EventId)
        {
            case LibMpvNative.MPV_EVENT_FILE_LOADED:
                InvalidateRuntimeVideoInfo();

                bool isPaused = Read(handle =>
                {
                    LibMpvNative.mpv_get_property_int(handle, "pause", LibMpvNative.MPV_FORMAT_FLAG, out int paused);
                    return paused != 0;
                }, true);

                bool pauseChanged = _propertyCache.TryUpdatePause(isPaused);
                bool loadedChanged = _propertyCache.TryUpdateLoaded(true);

                if (pauseChanged || loadedChanged)
                    PublishPlaybackState();

                FileLoaded?.Invoke(this, EventArgs.Empty);
                break;

            case LibMpvNative.MPV_EVENT_END_FILE:
                var endFile = mpvEvent.Data == IntPtr.Zero
                    ? new MpvEventEndFile()
                    : Marshal.PtrToStructure<MpvEventEndFile>(mpvEvent.Data);
                if (_propertyCache.TryUpdatePlaybackEnded())
                    PublishPlaybackState();
                PlaybackEnded?.Invoke(this, new MpvPlaybackEndedEventArgs(endFile.Reason, endFile.Error));
                break;

            case LibMpvNative.MPV_EVENT_PROPERTY_CHANGE:
                HandlePropertyChange(mpvEvent);
                break;
        }
    }

    private void ObservePlaybackProperties()
    {
        Check(LibMpvNative.mpv_observe_property(_mpvHandle, TimePositionPropertyId, "time-pos", LibMpvNative.MPV_FORMAT_DOUBLE), "observe time position");
        Check(LibMpvNative.mpv_observe_property(_mpvHandle, DurationPropertyId, "duration", LibMpvNative.MPV_FORMAT_DOUBLE), "observe duration");
        Check(LibMpvNative.mpv_observe_property(_mpvHandle, PausePropertyId, "pause", LibMpvNative.MPV_FORMAT_FLAG), "observe pause");
        Check(LibMpvNative.mpv_observe_property(_mpvHandle, SeekablePropertyId, "seekable", LibMpvNative.MPV_FORMAT_FLAG), "observe seekable");
        Check(LibMpvNative.mpv_observe_property(_mpvHandle, IdleActivePropertyId, "idle-active", LibMpvNative.MPV_FORMAT_FLAG), "observe idle active");
        Check(LibMpvNative.mpv_observe_property(_mpvHandle, TrackListPropertyId, "track-list", LibMpvNative.MPV_FORMAT_NONE), "observe track list");
    }

    private static void UnobservePlaybackProperties(IntPtr handle)
    {
        LibMpvNative.mpv_unobserve_property(handle, TimePositionPropertyId);
        LibMpvNative.mpv_unobserve_property(handle, DurationPropertyId);
        LibMpvNative.mpv_unobserve_property(handle, PausePropertyId);
        LibMpvNative.mpv_unobserve_property(handle, SeekablePropertyId);
        LibMpvNative.mpv_unobserve_property(handle, IdleActivePropertyId);
        LibMpvNative.mpv_unobserve_property(handle, TrackListPropertyId);
    }

    private void HandlePropertyChange(MpvEvent mpvEvent)
    {
        if (mpvEvent.Data == IntPtr.Zero)
            return;

        var property = Marshal.PtrToStructure<MpvEventProperty>(mpvEvent.Data);

        if (mpvEvent.ReplyUserData == TrackListPropertyId)
        {
            TracksChanged?.Invoke();
            return;
        }

        if (property.Format == LibMpvNative.MPV_FORMAT_NONE || property.Data == IntPtr.Zero)
            return;

        switch (mpvEvent.ReplyUserData)
        {
            case TimePositionPropertyId when property.Format == LibMpvNative.MPV_FORMAT_DOUBLE:
                var timePosition = Marshal.PtrToStructure<double>(property.Data);
                if (_propertyCache.TryUpdateTimePosition(timePosition))
                    PublishPlaybackState();
                break;

            case DurationPropertyId when property.Format == LibMpvNative.MPV_FORMAT_DOUBLE:
                var duration = Marshal.PtrToStructure<double>(property.Data);
                if (_propertyCache.TryUpdateDuration(duration))
                    PublishPlaybackState();
                break;

            case PausePropertyId when property.Format == LibMpvNative.MPV_FORMAT_FLAG:
                var isPaused = Marshal.ReadInt32(property.Data) != 0;
                if (_propertyCache.TryUpdatePause(isPaused))
                    PublishPlaybackState();
                break;

            case SeekablePropertyId when property.Format == LibMpvNative.MPV_FORMAT_FLAG:
                var isSeekable = Marshal.ReadInt32(property.Data) != 0;
                if (_propertyCache.TryUpdateSeekable(isSeekable))
                    PublishPlaybackState();
                break;

            case IdleActivePropertyId when property.Format == LibMpvNative.MPV_FORMAT_FLAG:
                var isIdleActive = Marshal.ReadInt32(property.Data) != 0;
                if (_propertyCache.TryUpdateLoaded(!isIdleActive))
                    PublishPlaybackState();
                break;
        }
    }

    private void PublishPlaybackState()
    {
        PlaybackStateChanged?.Invoke(_propertyCache.PlaybackState);
    }
}
