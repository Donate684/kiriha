using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kiriha.Core.Mpv;

public class MpvOpenGlRenderer : IDisposable
{
    private readonly MpvPlayer _player;
    private readonly ReaderWriterLockSlim _renderGate = new();
    private IntPtr _renderContext;
    private MpvRenderUpdateCallback? _renderUpdateCallback;
    private GCHandle _renderUpdateHandle;

    // This lock is static because it's used within a native callback (OnRenderUpdate) 
    // that lacks an instance context. If multiple instances are created, 
    // they will compete for this shared lock.
    private static readonly object _renderUpdateLock = new();

    public MpvOpenGlRenderer(MpvPlayer player)
    {
        _player = player;
    }

    public void CreateOpenGlRenderContext(MpvOpenGlGetProcAddressCallback getProcAddress)
    {
        lock (_player.Gate)
        {
            if (_player.IsDisposed || _player.MpvHandle == IntPtr.Zero || _renderContext != IntPtr.Zero)
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
                MpvPlayer.Check(LibMpvNative.mpv_render_context_create(out _renderContext, _player.MpvHandle, parametersPtr), "create OpenGL render context");

                _renderUpdateCallback = OnRenderUpdate;
                _renderUpdateHandle = GCHandle.Alloc(this);
                try
                {
                    LibMpvNative.mpv_render_context_set_update_callback(
                        _renderContext,
                        _renderUpdateCallback,
                        GCHandle.ToIntPtr(_renderUpdateHandle));
                }
                catch
                {
                    _renderUpdateHandle.Free();
                    throw;
                }
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
        _renderGate.EnterReadLock();
        try
        {
            IntPtr renderContext;
            lock (_player.Gate)
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
                MpvPlayer.Check(LibMpvNative.mpv_render_context_render(renderContext, parametersPtr), "render OpenGL frame");
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
        finally
        {
            _renderGate.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _renderGate.EnterWriteLock();
        try
        {
            IntPtr renderContext;
            lock (_player.Gate)
            {
                renderContext = _renderContext;
                _renderContext = IntPtr.Zero;
            }

            if (renderContext != IntPtr.Zero)
            {
                LibMpvNative.mpv_render_context_set_update_callback(renderContext, null!, IntPtr.Zero);
                LibMpvNative.mpv_render_context_free(renderContext);
            }

            lock (_renderUpdateLock)
            {
                if (_renderUpdateHandle.IsAllocated)
                    _renderUpdateHandle.Free();

                _renderUpdateCallback = null;
            }
        }
        finally
        {
            _renderGate.ExitWriteLock();
            _renderGate.Dispose();
        }
    }

    private static IntPtr AllocateRenderParams(System.Collections.Generic.IReadOnlyList<MpvRenderParam> parameters)
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

        MpvOpenGlRenderer? renderer = null;
        lock (_renderUpdateLock)
        {
            try
            {
                var handle = GCHandle.FromIntPtr(context);
                if (handle.IsAllocated)
                    renderer = handle.Target as MpvOpenGlRenderer;
            }
            catch (InvalidOperationException)
            {
                // Handle was freed concurrently
            }
        }

        renderer?._player.InvokeRenderUpdateRequested();
    }
}
