using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Kiriha.Core.Mpv;
using Serilog;

namespace Kiriha.Views.Controls;

public sealed class MpvOpenGlVideoView : OpenGlControlBase
{
    private MpvPlayer? _player;
    private GlInterface? _gl;
    private MpvOpenGlGetProcAddressCallback? _getProcAddress;
    private bool _renderContextReady;
    private int _renderRequestPending;

    public event EventHandler? RenderContextReady;

    public MpvPlayer? Player
    {
        get => _player;
        set
        {
            if (ReferenceEquals(_player, value))
                return;

            if (_player != null)
                _player.RenderUpdateRequested -= OnRenderUpdateRequested;

            _player = value;
            _renderContextReady = false;

            if (_player != null)
            {
                _player.RenderUpdateRequested += OnRenderUpdateRequested;
                RequestNextFrameRendering();
            }
        }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        _gl = gl;
        _getProcAddress = GetProcAddress;
        TryCreateRenderContext();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        _gl = gl;
        TryCreateRenderContext();

        if (_player == null || !_renderContextReady)
            return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));

        _player.RenderOpenGl(fb, width, height);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_player != null)
        {
            _player.RenderUpdateRequested -= OnRenderUpdateRequested;
            _player.FreeRenderContext();
        }

        _renderContextReady = false;
        _gl = null;
        _getProcAddress = null;
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlLost()
    {
        _renderContextReady = false;
        _gl = null;
        base.OnOpenGlLost();
    }

    private void TryCreateRenderContext()
    {
        if (_player == null || _gl == null || _getProcAddress == null || _renderContextReady)
            return;

        try
        {
            _player.CreateOpenGlRenderContext(_getProcAddress);
            _renderContextReady = true;
            RenderContextReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create mpv OpenGL render context");
        }
    }

    private IntPtr GetProcAddress(IntPtr context, IntPtr name)
    {
        if (_gl == null || name == IntPtr.Zero)
            return IntPtr.Zero;

        var procName = Marshal.PtrToStringUTF8(name);
        return string.IsNullOrWhiteSpace(procName)
            ? IntPtr.Zero
            : _gl.GetProcAddress(procName);
    }

    private void OnRenderUpdateRequested()
    {
        if (Interlocked.CompareExchange(ref _renderRequestPending, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _renderRequestPending, 0);
                RequestNextFrameRendering();
            });
        }
    }
}
