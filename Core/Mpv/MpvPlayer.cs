using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    private IntPtr _renderContext;
    private MpvRenderUpdateCallback? _renderUpdateCallback;
    private GCHandle _renderUpdateHandle;
    private bool _disposed;
    private string _screenshotDirectory = GetDefaultScreenshotDirectory();
    private string _screenshotFormat = "png";

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

        // Configure mpv for host-driven rendering.
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "osc", "no"), "disable osc");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "input-default-bindings", "no"), "disable default input bindings");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "input-vo-keyboard", "no"), "disable mpv keyboard input");
        ConfigureVideoPipeline(options ?? MpvOptions.Default);
        
        // Ensure mpv does not quit automatically on playback end or error
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "idle", "yes"), "enable idle");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "keep-open", "yes"), "enable keep-open");

        // Keep the embedded player modest: mpv defaults are tuned for a full player,
        // while Kiriha mostly needs enough buffer for smooth anime playback.
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "demuxer-max-bytes", "64MiB"), "limit demuxer cache");
        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, "demuxer-max-back-bytes", "16MiB"), "limit back buffer");
        ConfigureScreenshots(_mpvHandle);

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

    public void CreateOpenGlRenderContext(MpvOpenGlGetProcAddressCallback getProcAddress)
    {
        lock (_gate)
        {
            if (_disposed || _mpvHandle == IntPtr.Zero || _renderContext != IntPtr.Zero)
                return;

            var getProcAddressPtr = Marshal.GetFunctionPointerForDelegate(getProcAddress);
            var apiTypePtr = Marshal.StringToCoTaskMemUTF8(LibMpvNative.MPV_RENDER_API_TYPE_OPENGL);
            var initParamsPtr = IntPtr.Zero;
            var parametersPtr = IntPtr.Zero;

            try
            {
                var initParams = new MpvOpenGlInitParams(getProcAddressPtr, IntPtr.Zero);
                initParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlInitParams>());
                Marshal.StructureToPtr(initParams, initParamsPtr, false);

                var parameters = new[]
                {
                    new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_API_TYPE, apiTypePtr),
                    new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, initParamsPtr),
                    new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_INVALID, IntPtr.Zero)
                };

                parametersPtr = AllocateRenderParams(parameters);
                Check(LibMpvNative.mpv_render_context_create(out _renderContext, _mpvHandle, parametersPtr), "create OpenGL render context");

                _renderUpdateCallback = OnRenderUpdate;
                _renderUpdateHandle = GCHandle.Alloc(this);
                LibMpvNative.mpv_render_context_set_update_callback(
                    _renderContext,
                    _renderUpdateCallback,
                    GCHandle.ToIntPtr(_renderUpdateHandle));
            }
            finally
            {
                if (parametersPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(parametersPtr);
                if (initParamsPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(initParamsPtr);
                Marshal.FreeCoTaskMem(apiTypePtr);
            }
        }
    }

    public void RenderOpenGl(int framebuffer, int width, int height)
    {
        IntPtr renderContext;
        lock (_gate)
        {
            renderContext = _renderContext;
        }

        if (renderContext == IntPtr.Zero || width <= 0 || height <= 0)
            return;

        var updateFlags = LibMpvNative.mpv_render_context_update(renderContext);
        if ((updateFlags & LibMpvNative.MPV_RENDER_UPDATE_FRAME) == 0)
            return;

        var fboPtr = IntPtr.Zero;
        var flipPtr = IntPtr.Zero;
        var parametersPtr = IntPtr.Zero;

        try
        {
            const int glRgba8 = 0x8058;
            var fbo = new MpvOpenGlFbo(framebuffer, width, height, glRgba8);
            fboPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlFbo>());
            Marshal.StructureToPtr(fbo, fboPtr, false);

            flipPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(flipPtr, 1);

            var parameters = new[]
            {
                new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_OPENGL_FBO, fboPtr),
                new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_FLIP_Y, flipPtr),
                new MpvRenderParam(LibMpvNative.MPV_RENDER_PARAM_INVALID, IntPtr.Zero)
            };

            parametersPtr = AllocateRenderParams(parameters);
            Check(LibMpvNative.mpv_render_context_render(renderContext, parametersPtr), "render OpenGL frame");
            LibMpvNative.mpv_render_context_report_swap(renderContext);
        }
        finally
        {
            if (parametersPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(parametersPtr);
            if (flipPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(flipPtr);
            if (fboPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(fboPtr);
        }
    }

    public void Load(string url)
    {
        Enqueue(handle => Check(LibMpvNative.mpv_command_async_string(handle, 0, "loadfile", url), "load file"));
    }

    public void Play()
    {
        Enqueue(handle => Check(LibMpvNative.mpv_set_property_string(handle, "pause", "no"), "play"));
    }

    public void Pause()
    {
        Enqueue(handle => Check(LibMpvNative.mpv_set_property_string(handle, "pause", "yes"), "pause"));
    }

    public void Seek(double timeInSeconds)
    {
        Enqueue(handle => Check(
            LibMpvNative.mpv_command_string(handle, "seek", timeInSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute"),
            "seek"), SeekCommandKey);
    }

    public void SetVolume(double volume)
    {
        Enqueue(handle =>
        {
            double vol = Math.Max(0, Math.Min(100, volume));
            Check(LibMpvNative.mpv_set_property(handle, "volume", LibMpvNative.MPV_FORMAT_DOUBLE, ref vol), "set volume");
        }, VolumeCommandKey);
    }

    public void SetSpeed(double speed)
    {
        Enqueue(handle =>
        {
            double spd = Math.Max(0.1, Math.Min(4.0, speed));
            Check(LibMpvNative.mpv_set_property(handle, "speed", LibMpvNative.MPV_FORMAT_DOUBLE, ref spd), "set speed");
        }, SpeedCommandKey);
    }

    public void SetAudioNormalization(bool enabled)
    {
        Enqueue(handle => Check(
            LibMpvNative.mpv_set_property_string(handle, "af", enabled ? AudioNormalizationFilter : string.Empty),
            enabled ? "enable audio normalization" : "disable audio normalization"));
    }

    public void SetTrackLanguagePreferences(string audioLanguages, string subtitleLanguages)
    {
        Enqueue(handle =>
        {
            SetMpvOption(handle, "alang", audioLanguages, "set preferred audio languages");
            SetMpvOption(handle, "slang", subtitleLanguages, "set preferred subtitle languages");
        });
    }

    public void SetVideoProcessingOptions(
        string scale,
        string chromaScale,
        string ditherDepth,
        bool correctDownscaling,
        bool deband,
        int debandIterations,
        int debandThreshold)
    {
        Enqueue(handle =>
        {
            SetMpvOption(handle, "scale", scale, "set video scale filter");
            SetMpvOption(handle, "cscale", chromaScale, "set chroma scale filter");
            SetMpvOption(handle, "dither-depth", ditherDepth, "set dither depth");
            SetMpvOption(handle, "correct-downscaling", correctDownscaling ? "yes" : "no", "set correct downscaling");
            SetMpvOption(handle, "deband", deband ? "yes" : "no", "set debanding");
            SetMpvOption(handle, "deband-iterations", Math.Clamp(debandIterations, 0, 16).ToString(System.Globalization.CultureInfo.InvariantCulture), "set deband iterations");
            SetMpvOption(handle, "deband-threshold", Math.Clamp(debandThreshold, 0, 4096).ToString(System.Globalization.CultureInfo.InvariantCulture), "set deband threshold");
        });
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
        Enqueue(handle =>
        {
            Check(LibMpvNative.mpv_set_option_string(handle, "sub-ass-override", enabled ? "force" : "yes"), "set subtitle override");

            if (!enabled)
                return;

            SetMpvOption(handle, "sub-font", font, "set subtitle font");
            SetMpvOption(handle, "sub-font-size", FormatDouble(fontSize), "set subtitle font size");
            SetMpvOption(handle, "sub-color", color, "set subtitle color");
            SetMpvOption(handle, "sub-border-color", borderColor, "set subtitle border color");
            SetMpvOption(handle, "sub-shadow-color", shadowColor, "set subtitle shadow color");
            SetMpvOption(handle, "sub-border-size", FormatDouble(borderSize), "set subtitle border size");
            SetMpvOption(handle, "sub-shadow-offset", FormatDouble(shadowOffset), "set subtitle shadow offset");
            SetMpvOption(handle, "sub-align-y", alignY, "set subtitle vertical alignment");
            SetMpvOption(handle, "sub-align-x", alignX, "set subtitle horizontal alignment");
            SetMpvOption(handle, "sub-margin-y", Math.Max(0, marginY).ToString(System.Globalization.CultureInfo.InvariantCulture), "set subtitle margin");
            SetMpvOption(handle, "sub-scale-by-window", scaleByWindow ? "yes" : "no", "set subtitle scaling");
        });
    }

    public void SetOptionString(string name, string value)
    {
        Enqueue(handle =>
        {
            Check(LibMpvNative.mpv_set_option_string(handle, name, value), $"set {name}");
            InvalidateRuntimeVideoInfo();
        });
    }

    public double GetTimePosition()
    {
        return _propertyCache.TimePosition;
    }

    public PlaybackState GetPlaybackState()
    {
        if (_propertyCache.Duration <= 0)
            _ = GetDuration();

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

    public void CycleSubtitle()
    {
        Enqueue(handle => Check(LibMpvNative.mpv_command_string(handle, "cycle", "sid"), "cycle subtitles"));
    }

    public void AdjustSubtitlePosition(double delta)
    {
        Enqueue(handle => Check(
            LibMpvNative.mpv_command_string(handle, "add", "sub-pos", FormatDouble(delta)),
            "adjust subtitle position"));
    }

    public void CycleAudio()
    {
        Enqueue(handle => Check(LibMpvNative.mpv_command_string(handle, "cycle", "aid"), "cycle audio"));
    }

    public void ReloadSubtitles()
    {
        Enqueue(handle => Check(LibMpvNative.mpv_command_string(handle, "sub-reload"), "reload subtitles"));
    }

    public void FrameStep()
    {
        Enqueue(handle => Check(LibMpvNative.mpv_command_string(handle, "frame-step"), "frame step"));
    }

    public void FrameBackStep()
    {
        Enqueue(handle => Check(LibMpvNative.mpv_command_string(handle, "frame-back-step"), "frame back step"));
    }

    public void TakeScreenshot(bool includeSubtitles, string resolutionMode)
    {
        var flag = string.Equals(resolutionMode, "window", StringComparison.OrdinalIgnoreCase)
            ? "window"
            : includeSubtitles ? "subtitles" : "video";

        Enqueue(handle =>
        {
            Directory.CreateDirectory(_screenshotDirectory);
            var filename = $"Kiriha-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{_screenshotFormat}";
            var path = Path.Combine(_screenshotDirectory, filename);
            Check(LibMpvNative.mpv_command_string(handle, "screenshot-to-file", path, flag), "take screenshot");
        });
    }

    public void SetScreenshotOptions(
        string directory,
        string format,
        int pngCompression,
        int quality,
        bool highBitDepth)
    {
        Enqueue(handle => ConfigureScreenshots(
            handle,
            directory,
            format,
            pngCompression,
            quality,
            highBitDepth));
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

    public void SetTrack(string type, string id)
    {
        Enqueue(handle =>
        {
            string prop = type == "sub" ? "sid" : type == "audio" ? "aid" : "vid";
            Check(LibMpvNative.mpv_set_property_string(handle, prop, id), $"set {prop}");
        });
    }

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

        MpvPlayerLifecycle.Dispose(
            handle,
            _commandQueue,
            _eventLoop,
            FreeRenderContext,
            UnobservePlaybackProperties);
    }

    public void FreeRenderContext()
    {
        IntPtr renderContext;
        lock (_gate)
        {
            renderContext = _renderContext;
            _renderContext = IntPtr.Zero;
        }

        if (renderContext != IntPtr.Zero)
        {
            LibMpvNative.mpv_render_context_set_update_callback(renderContext, null!, IntPtr.Zero);
            LibMpvNative.mpv_render_context_free(renderContext);
        }

        if (_renderUpdateHandle.IsAllocated)
            _renderUpdateHandle.Free();

        _renderUpdateCallback = null;
    }

    private void Enqueue(Action<IntPtr> action, string? coalescingKey = null)
    {
        _commandQueue.Enqueue(action, coalescingKey);
    }

    private T Read<T>(Func<IntPtr, T> read, T defaultValue)
    {
        lock (_gate)
        {
            if (_mpvHandle == IntPtr.Zero) return defaultValue;
            return read(_mpvHandle);
        }
    }

    private T ReadNodeProperty<T>(string name, Func<MpvNode, T> parse, T defaultValue)
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

    private static void Check(int result, string action)
    {
        if (result < 0)
            throw new InvalidOperationException($"mpv failed to {action}: {LibMpvNative.GetErrorString(result)}");
    }

    private static string? GetPropertyString(IntPtr handle, string name)
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

    private double ReadDoubleProperty(string name, double defaultValue)
    {
        return Read(handle =>
        {
            var result = LibMpvNative.mpv_get_property(handle, name, LibMpvNative.MPV_FORMAT_DOUBLE, out var value);
            return result < 0 ? defaultValue : value;
        }, defaultValue);
    }

    private void InvalidateRuntimeVideoInfo()
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

    private void ConfigureVideoPipeline(MpvOptions options)
    {
        SetOptionalStringOption("hwdec", options.Hwdec, "set hardware decoder");
        SetOptionalStringOption("vo", "libmpv", "set video output");
        SetOptionalStringOption("gpu-api", options.GpuApi, "set GPU API");
        SetOptionalStringOption("gpu-context", options.GpuContext, "set GPU context");
    }

    private void SetOptionalStringOption(string name, string? value, string action)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        Check(LibMpvNative.mpv_set_option_string(_mpvHandle, name, value.Trim()), action);
    }

    private static IntPtr AllocateRenderParams(IReadOnlyList<MpvRenderParam> parameters)
    {
        var size = Marshal.SizeOf<MpvRenderParam>();
        var ptr = Marshal.AllocHGlobal(size * parameters.Count);
        for (int i = 0; i < parameters.Count; i++)
            Marshal.StructureToPtr(parameters[i], IntPtr.Add(ptr, i * size), false);
        return ptr;
    }

    private static void OnRenderUpdate(IntPtr context)
    {
        if (context == IntPtr.Zero)
            return;

        var handle = GCHandle.FromIntPtr(context);
        if (handle.Target is MpvPlayer player)
            player.RenderUpdateRequested?.Invoke();
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

    private void ConfigureScreenshots(
        IntPtr handle,
        string? directory = null,
        string format = "png",
        int pngCompression = 4,
        int quality = 95,
        bool highBitDepth = false)
    {
        var screenshotDir = string.IsNullOrWhiteSpace(directory)
            ? GetDefaultScreenshotDirectory()
            : directory;
        var screenshotFormat = NormalizeScreenshotFormat(format);

        Directory.CreateDirectory(screenshotDir);
        _screenshotDirectory = screenshotDir;
        _screenshotFormat = screenshotFormat;
        Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-directory", screenshotDir), "set screenshot directory");
        Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-template", "Kiriha-%F-%P"), "set screenshot template");
        Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-format", screenshotFormat), "set screenshot format");
        Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-png-compression", Math.Clamp(pngCompression, 0, 9).ToString(System.Globalization.CultureInfo.InvariantCulture)), "set screenshot png compression");
        Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-jpeg-quality", Math.Clamp(quality, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)), "set screenshot jpeg quality");
        Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-webp-quality", Math.Clamp(quality, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)), "set screenshot webp quality");
        Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-high-bit-depth", highBitDepth ? "yes" : "no"), "set screenshot bit depth");
    }

    private static void SetMpvOption(IntPtr handle, string name, string value, string action)
    {
        Check(LibMpvNative.mpv_set_option_string(handle, name, value), action);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetDefaultScreenshotDirectory()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return desktop;
    }

    private static string NormalizeScreenshotFormat(string? format)
    {
        return string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : string.Equals(format, "webp", StringComparison.OrdinalIgnoreCase)
                ? "webp"
                : "png";
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
    public double Time  { get; set; }
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
