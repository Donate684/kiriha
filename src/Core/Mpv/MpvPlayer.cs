using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Mpv;

public class MpvPlayer : IDisposable
{
    private const ulong TimePositionPropertyId = 1;
    private const ulong DurationPropertyId = 2;
    private const ulong PausePropertyId = 3;
    private const ulong SeekablePropertyId = 4;
    private const ulong IdleActivePropertyId = 5;
    private const ulong TrackListPropertyId = 6;
    private const string AudioNormalizationFilter = "loudnorm=I=-16:TP=-1.5:LRA=11";
    private const string SeekCommandKey = "seek";
    private const string VolumeCommandKey = "volume";
    private const string SpeedCommandKey = "speed";

    private readonly object _gate = new();
    private readonly MpvPropertyCache _propertyCache = new(FormatRuntimeVideoInfo(null, null, null, null, null));
    private readonly MpvCommandQueue _commandQueue;
    private readonly MpvEventLoop _eventLoop;
    private IntPtr _mpvHandle;
    private bool _disposed;

    internal object Gate => _gate;
    internal bool IsDisposed => _disposed;
    internal IntPtr MpvHandle => _mpvHandle;

    public MpvPlaybackController PlaybackController { get; }
    public MpvTrackManager TrackManager { get; }
    public MpvScreenshotManager ScreenshotManager { get; }
    public MpvVideoPipelineConfigurator VideoPipelineConfigurator { get; }
    public MpvOpenGlRenderer OpenGlRenderer { get; }

    public event EventHandler? FileLoaded;
    public event EventHandler<MpvPlaybackEndedEventArgs>? PlaybackEnded;
    public event Action? RenderUpdateRequested;
    public event Action<PlaybackState>? PlaybackStateChanged;
    public event Action? TracksChanged;

    public MpvPlayer(MpvOptions? options = null)
    {
        _commandQueue = new MpvCommandQueue(_gate, () => _mpvHandle, () => _disposed);
        _eventLoop = new MpvEventLoop(_gate, () => _mpvHandle, HandleEvent);

        _mpvHandle = LibMpvNative.mpv_create();
        if (_mpvHandle == IntPtr.Zero)
            throw new Exception("Failed to create mpv instance. Ensure mpv-2.dll is in the output mpv/ folder.");

        PlaybackController = new MpvPlaybackController(this);
        TrackManager = new MpvTrackManager(this);
        ScreenshotManager = new MpvScreenshotManager(this);
        VideoPipelineConfigurator = new MpvVideoPipelineConfigurator(this);
        OpenGlRenderer = new MpvOpenGlRenderer(this);

        // Configure mpv for host-driven rendering.
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "osc", "no"), "disable osc");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "input-default-bindings", "no"), "disable default input bindings");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "input-vo-keyboard", "no"), "disable mpv keyboard input");
        VideoPipelineConfigurator.ConfigureVideoPipeline(_mpvHandle, options ?? MpvOptions.Default);

        // Ensure mpv does not quit automatically on playback end or error
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "idle", "yes"), "enable idle");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "keep-open", "yes"), "enable keep-open");

        // Keep the embedded player modest: mpv defaults are tuned for a full player,
        // while Kiriha mostly needs enough buffer for smooth anime playback.
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "demuxer-max-bytes", "64MiB"), "limit demuxer cache");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "demuxer-max-back-bytes", "16MiB"), "limit back buffer");
        ScreenshotManager.ConfigureScreenshots(_mpvHandle);

        int res = LibMpvNative.mpv_initialize(_mpvHandle);
        if (res < 0)
        {
            LibMpvNative.mpv_terminate_destroy(_mpvHandle);
            throw new InvalidOperationException($"Failed to initialize mpv: {LibMpvNative.GetErrorString(res)}");
        }

        ObservePlaybackProperties();
        _commandQueue.Start();
        _eventLoop.Start();
    }

    internal void InvokeRenderUpdateRequested() => RenderUpdateRequested?.Invoke();

    public void CreateOpenGlRenderContext(MpvOpenGlGetProcAddressCallback getProcAddress) => OpenGlRenderer.CreateOpenGlRenderContext(getProcAddress);

    public void RenderOpenGl(int framebuffer, int width, int height) => OpenGlRenderer.RenderOpenGl(framebuffer, width, height);

    public void Load(string url) => PlaybackController.Load(url);

    public void AddSubtitle(string path) => TrackManager.AddSubtitle(path);

    public void Play() => PlaybackController.Play();

    public void Pause() => PlaybackController.Pause();

    public void Seek(double timeInSeconds) => PlaybackController.Seek(timeInSeconds);

    public void SetVolume(double volume) => PlaybackController.SetVolume(volume);

    public void SetSpeed(double speed) => PlaybackController.SetSpeed(speed);

    public void SetAudioNormalization(bool enabled) => PlaybackController.SetAudioNormalization(enabled);

    public void SetTrackLanguagePreferences(string audioLanguages, string subtitleLanguages) => TrackManager.SetTrackLanguagePreferences(audioLanguages, subtitleLanguages);

    public void SetVideoProcessingOptions(
        string scale,
        string chromaScale,
        string ditherDepth,
        bool correctDownscaling,
        bool deband,
        int debandIterations,
        int debandThreshold)
    {
        VideoPipelineConfigurator.SetVideoProcessingOptions(scale, chromaScale, ditherDepth, correctDownscaling, deband, debandIterations, debandThreshold);
    }

    public void SetSubtitleStyleOverride(
        bool enabled,
        string font,
        double fontSize,
        string color,
        string borderColor,
        string shadowColor,
        double borderSize,
        double shadowOffset,
        string alignY,
        string alignX,
        int marginY,
        bool scaleByWindow)
    {
        TrackManager.SetSubtitleStyleOverride(enabled, font, fontSize, color, borderColor, shadowColor, borderSize, shadowOffset, alignY, alignX, marginY, scaleByWindow);
    }

    public void SetOptionString(string name, string value)
    {
        Enqueue(handle =>
        {
            Check(LibMpvNative.mpv_set_property_string(handle, name, value), $"set {name}");
            InvalidateRuntimeVideoInfo();
        });
    }

    public double GetTimePosition()
    {
        return _propertyCache.TimePosition;
    }

    public PlaybackState GetPlaybackState()
    {
        return _propertyCache.PlaybackState;
    }

    public double GetDuration()
    {
        var cached = _propertyCache.Duration;
        if (cached > 0)
            return cached;

        var duration = ReadDoubleProperty("duration", 0);
        if (duration > 0)
            _propertyCache.TryUpdateDuration(duration);

        return duration;
    }

    public bool IsPaused()
    {
        return _propertyCache.IsPaused;
    }

    public void CycleSubtitle() => TrackManager.CycleSubtitle();

    public void AdjustSubtitlePosition(double delta) => TrackManager.AdjustSubtitlePosition(delta);

    public void CycleAudio() => TrackManager.CycleAudio();

    public void ReloadSubtitles() => TrackManager.ReloadSubtitles();

    public void FrameStep() => PlaybackController.FrameStep();

    public void FrameBackStep() => PlaybackController.FrameBackStep();

    public void TakeScreenshot(bool includeSubtitles, string resolutionMode) => ScreenshotManager.TakeScreenshot(includeSubtitles, resolutionMode);

    public void SetScreenshotOptions(
        string directory,
        string format,
        int pngCompression,
        int quality,
        bool highBitDepth)
    {
        ScreenshotManager.SetScreenshotOptions(directory, format, pngCompression, quality, highBitDepth);
    }

    public string? GetPropertyString(string name)
    {
        return Read(handle => GetPropertyString(handle, name), null);
    }

    public string GetRuntimeVideoInfo()
    {
        if (_propertyCache.HasFreshRuntimeVideoInfo)
            return _propertyCache.RuntimeVideoInfo;

        var info = Read(handle =>
        {
            var hwdec = GetPropertyString(handle, "hwdec-current");
            var interop = GetPropertyString(handle, "hwdec-interop");
            var vo = GetPropertyString(handle, "current-vo") ?? GetPropertyString(handle, "vo-configured");
            var gpuContext = GetPropertyString(handle, "current-gpu-context");
            var decoder = GetPropertyString(handle, "decoder");

            return FormatRuntimeVideoInfo(hwdec, interop, vo, gpuContext, decoder);
        }, _propertyCache.RuntimeVideoInfo);

        _propertyCache.StoreRuntimeVideoInfo(info);
        return _propertyCache.RuntimeVideoInfo;
    }

    public System.Collections.Generic.List<TrackInfo> GetTracks()
    {
        return ReadNodeProperty("track-list", ParseTracks, new List<TrackInfo>());
    }

    public List<ChapterInfo> GetChapters()
    {
        return ReadNodeProperty("chapter-list", ParseChapters, new List<ChapterInfo>());
    }

    public void SetTrack(string type, string id) => TrackManager.SetTrack(type, id);

    public void Dispose()
    {
        IntPtr handle;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _commandQueue.Complete();
            handle = _mpvHandle;
            _mpvHandle = IntPtr.Zero;
        }

        OpenGlRenderer.Dispose();

        Task.Run(() =>
        {
            try
            {
                MpvPlayerLifecycle.Dispose(
                    handle,
                    _commandQueue,
                    _eventLoop,
                    UnobservePlaybackProperties);
            }
            finally
            {
                // Removed _renderGate from MpvPlayer since it moved to OpenGlRenderer
            }
        });
    }

    public void FreeRenderContext() => OpenGlRenderer.Dispose();

    internal void Enqueue(Action<IntPtr> action, string? coalescingKey = null)
    {
        _commandQueue.Enqueue(action, coalescingKey);
    }

    internal T Read<T>(Func<IntPtr, T> read, T defaultValue)
    {
        IntPtr handle;
        lock (_gate)
        {
            if (_disposed || _mpvHandle == IntPtr.Zero) return defaultValue;
            handle = _mpvHandle;
        }

        return read(handle);
    }

    internal T ReadNodeProperty<T>(string name, Func<MpvNode, T> parse, T defaultValue)
    {
        return Read(handle =>
        {
            int result = LibMpvNative.mpv_get_property_node(handle, name, LibMpvNative.MPV_FORMAT_NODE, out var node);
            if (result < 0)
                return defaultValue;

            try
            {
                return parse(node);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse mpv node property {PropertyName}", name);
                return defaultValue;
            }
            finally
            {
                LibMpvNative.mpv_free_node_contents(ref node);
            }
        }, defaultValue);
    }

    internal static void Check(int result, string action)
    {
        if (result < 0)
            throw new InvalidOperationException($"mpv failed to {action}: {LibMpvNative.GetErrorString(result)}");
    }

    internal static string? GetPropertyString(IntPtr handle, string name)
    {
        IntPtr ptr = LibMpvNative.mpv_get_property_string(handle, name);
        if (ptr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            LibMpvNative.mpv_free(ptr);
        }
    }

    internal double ReadDoubleProperty(string name, double defaultValue)
    {
        return Read(handle =>
        {
            var result = LibMpvNative.mpv_get_property(handle, name, LibMpvNative.MPV_FORMAT_DOUBLE, out var value);
            return result < 0 ? defaultValue : value;
        }, defaultValue);
    }

    internal void InvalidateRuntimeVideoInfo()
    {
        _propertyCache.InvalidateRuntimeVideoInfo();
    }

    private static string FormatRuntimeVideoInfo(
        string? hwdec,
        string? interop,
        string? vo,
        string? gpuContext,
        string? decoder)
    {
        static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

        return $"hwdec: {ValueOrDash(hwdec)}, interop: {ValueOrDash(interop)}, vo: {ValueOrDash(vo)}, context: {ValueOrDash(gpuContext)}, decoder: {ValueOrDash(decoder)}";
    }

    internal static void SetMpvOption(IntPtr handle, string name, string value, string action)
    {
        Check(LibMpvNative.mpv_set_property_string(handle, name, value), action);
    }

    internal static string FormatDouble(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<TrackInfo> ParseTracks(MpvNode root)
    {
        var tracks = new List<TrackInfo>();
        if (!TryGetNodeList(root, LibMpvNative.MPV_FORMAT_NODE_ARRAY, out var list))
            return tracks;

        for (int i = 0; i < list.Num; i++)
        {
            var trackNode = ReadNode(list.Values, i);
            if (trackNode.Format != LibMpvNative.MPV_FORMAT_NODE_MAP)
                continue;

            var type = GetMapString(trackNode, "type");
            var id = GetMapString(trackNode, "id");
            if (type == null || id == null)
                continue;

            tracks.Add(new TrackInfo
            {
                Type = type,
                Id = id,
                Title = GetMapString(trackNode, "title"),
                Lang = GetMapString(trackNode, "lang"),
                Selected = GetMapBool(trackNode, "selected")
            });
        }

        return tracks;
    }

    private static List<ChapterInfo> ParseChapters(MpvNode root)
    {
        var chapters = new List<ChapterInfo>();
        if (!TryGetNodeList(root, LibMpvNative.MPV_FORMAT_NODE_ARRAY, out var list))
            return chapters;

        for (int i = 0; i < list.Num; i++)
        {
            var chapterNode = ReadNode(list.Values, i);
            if (chapterNode.Format != LibMpvNative.MPV_FORMAT_NODE_MAP)
                continue;

            chapters.Add(new ChapterInfo
            {
                Title = GetMapString(chapterNode, "title") ?? $"Chapter {i + 1}",
                Time = GetMapDouble(chapterNode, "time") ?? 0
            });
        }

        return chapters;
    }

    private static bool TryGetNodeList(MpvNode node, int expectedFormat, out MpvNodeList list)
    {
        if (node.Format != expectedFormat || node.U.List == IntPtr.Zero)
        {
            list = default;
            return false;
        }

        list = Marshal.PtrToStructure<MpvNodeList>(node.U.List);
        return list.Num > 0 && list.Values != IntPtr.Zero;
    }

    private static MpvNode ReadNode(IntPtr values, int index)
    {
        return Marshal.PtrToStructure<MpvNode>(IntPtr.Add(values, index * Marshal.SizeOf<MpvNode>()));
    }

    private static bool TryGetMapValue(MpvNode mapNode, string key, out MpvNode value)
    {
        value = default;
        if (!TryGetNodeList(mapNode, LibMpvNative.MPV_FORMAT_NODE_MAP, out var list) || list.Keys == IntPtr.Zero)
            return false;

        for (int i = 0; i < list.Num; i++)
        {
            var keyPtr = Marshal.ReadIntPtr(list.Keys, i * IntPtr.Size);
            var currentKey = keyPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(keyPtr);
            if (!string.Equals(currentKey, key, StringComparison.Ordinal))
                continue;

            value = ReadNode(list.Values, i);
            return true;
        }

        return false;
    }

    private static string? GetMapString(MpvNode mapNode, string key)
    {
        if (!TryGetMapValue(mapNode, key, out var value))
            return null;

        return value.Format switch
        {
            LibMpvNative.MPV_FORMAT_STRING when value.U.String != IntPtr.Zero => Marshal.PtrToStringUTF8(value.U.String),
            LibMpvNative.MPV_FORMAT_INT64 => value.U.Int64.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LibMpvNative.MPV_FORMAT_DOUBLE => value.U.Double.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LibMpvNative.MPV_FORMAT_FLAG => value.U.Flag != 0 ? "yes" : "no",
            _ => null
        };
    }

    private static double? GetMapDouble(MpvNode mapNode, string key)
    {
        if (!TryGetMapValue(mapNode, key, out var value))
            return null;

        return value.Format switch
        {
            LibMpvNative.MPV_FORMAT_DOUBLE => value.U.Double,
            LibMpvNative.MPV_FORMAT_INT64 => value.U.Int64,
            _ => null
        };
    }

    private static bool GetMapBool(MpvNode mapNode, string key)
    {
        if (!TryGetMapValue(mapNode, key, out var value))
            return false;

        return value.Format switch
        {
            LibMpvNative.MPV_FORMAT_FLAG => value.U.Flag != 0,
            LibMpvNative.MPV_FORMAT_STRING when value.U.String != IntPtr.Zero =>
                string.Equals(Marshal.PtrToStringUTF8(value.U.String), "yes", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

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

public class ChapterInfo
{
    public string Title { get; set; } = string.Empty;
    public double Time { get; set; }
}

public class TrackInfo
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Lang { get; set; }
    public bool Selected { get; set; }

    public string DisplayName
    {
        get
        {
            string name = Title ?? Lang ?? "Unknown Track";
            if (Title != null && Lang != null) name = $"{Title} ({Lang})";
            return name;
        }
    }
}
