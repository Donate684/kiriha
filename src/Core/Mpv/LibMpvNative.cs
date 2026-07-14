using System;
using System.Runtime.InteropServices;

namespace Kiriha.Core.Mpv;

/// <summary>
/// Minimal P/Invoke wrapper for libmpv C API.
/// Expects mpv-2.dll (or similar) to be present in the output directory.
/// </summary>
public static class LibMpvNative
{
    private const string LibraryName = @"mpv\libmpv-2.dll"; // Path relative to executable

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr ctx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref long data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command_async(IntPtr ctx, ulong reply_userdata, IntPtr args);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref double data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_int(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out int data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_node(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out MpvNode data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free(IntPtr data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free_node_contents(ref MpvNode node);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_error_string(int error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_wakeup(IntPtr ctx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_observe_property(
        IntPtr ctx,
        ulong replyUserData,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_unobserve_property(IntPtr ctx, ulong registeredReplyUserData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_render_context_create(out IntPtr renderContext, IntPtr mpv, IntPtr parameters);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_render_context_set_update_callback(
        IntPtr renderContext,
        MpvRenderUpdateCallback callback,
        IntPtr callbackContext);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong mpv_render_context_update(IntPtr renderContext);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_render_context_render(IntPtr renderContext, IntPtr parameters);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_render_context_report_swap(IntPtr renderContext);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_render_context_free(IntPtr renderContext);

    public const int MPV_FORMAT_NONE = 0;
    public const int MPV_FORMAT_STRING = 1;
    public const int MPV_FORMAT_FLAG = 3;
    public const int MPV_FORMAT_INT64 = 4;
    public const int MPV_FORMAT_DOUBLE = 5;
    public const int MPV_FORMAT_NODE = 6;
    public const int MPV_FORMAT_NODE_ARRAY = 7;
    public const int MPV_FORMAT_NODE_MAP = 8;

    public const int MPV_EVENT_NONE = 0;
    public const int MPV_EVENT_SHUTDOWN = 1;
    public const int MPV_EVENT_END_FILE = 7;
    public const int MPV_EVENT_FILE_LOADED = 8;
    public const int MPV_EVENT_PROPERTY_CHANGE = 22;

    public const int MPV_RENDER_PARAM_INVALID = 0;
    public const int MPV_RENDER_PARAM_API_TYPE = 1;
    public const int MPV_RENDER_PARAM_OPENGL_INIT_PARAMS = 2;
    public const int MPV_RENDER_PARAM_OPENGL_FBO = 3;
    public const int MPV_RENDER_PARAM_FLIP_Y = 4;
    public const int MPV_RENDER_UPDATE_FRAME = 1 << 0;

    public const string MPV_RENDER_API_TYPE_OPENGL = "opengl";

    public static int mpv_command_string(IntPtr ctx, params string[] args)
    {
        IntPtr[] ptrs = new IntPtr[args.Length + 1];
        IntPtr unmanagedPointer = IntPtr.Zero;

        try
        {
            for (int i = 0; i < args.Length; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            ptrs[args.Length] = IntPtr.Zero;

            unmanagedPointer = Marshal.AllocHGlobal(ptrs.Length * IntPtr.Size);
            Marshal.Copy(ptrs, 0, unmanagedPointer, ptrs.Length);

            return mpv_command(ctx, unmanagedPointer);
        }
        finally
        {
            if (unmanagedPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(unmanagedPointer);

            for (int i = 0; i < args.Length; i++)
            {
                if (ptrs[i] != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(ptrs[i]);
            }
        }
    }

    public static string GetErrorString(int error)
    {
        var ptr = mpv_error_string(error);
        return ptr == IntPtr.Zero
            ? $"mpv error {error}"
            : Marshal.PtrToStringUTF8(ptr) ?? $"mpv error {error}";
    }
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate IntPtr MpvOpenGlGetProcAddressCallback(IntPtr context, IntPtr name);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void MpvRenderUpdateCallback(IntPtr context);

[StructLayout(LayoutKind.Sequential)]
public readonly struct MpvRenderParam
{
    public readonly int Type;
    public readonly IntPtr Data;

    public MpvRenderParam(int type, IntPtr data)
    {
        Type = type;
        Data = data;
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MpvOpenGlInitParams
{
    public readonly IntPtr GetProcAddress;
    public readonly IntPtr GetProcAddressContext;

    public MpvOpenGlInitParams(IntPtr getProcAddress, IntPtr getProcAddressContext)
    {
        GetProcAddress = getProcAddress;
        GetProcAddressContext = getProcAddressContext;
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MpvOpenGlFbo
{
    public readonly int Fbo;
    public readonly int Width;
    public readonly int Height;
    public readonly int InternalFormat;

    public MpvOpenGlFbo(int fbo, int width, int height, int internalFormat)
    {
        Fbo = fbo;
        Width = width;
        Height = height;
        InternalFormat = internalFormat;
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MpvEvent
{
    public readonly int EventId;
    public readonly int Error;
    public readonly ulong ReplyUserData;
    public readonly IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MpvEventEndFile
{
    public readonly int Reason;
    public readonly int Error;
    public readonly long PlaylistEntryId;
    public readonly long PlaylistInsertId;
    public readonly int PlaylistInsertNumEntries;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MpvEventProperty
{
    public readonly IntPtr Name;
    public readonly int Format;
    public readonly IntPtr Data;
}

[StructLayout(LayoutKind.Explicit)]
public struct MpvNodeUnion
{
    [FieldOffset(0)] public IntPtr String;
    [FieldOffset(0)] public int Flag;
    [FieldOffset(0)] public long Int64;
    [FieldOffset(0)] public double Double;
    [FieldOffset(0)] public IntPtr List;
    [FieldOffset(0)] public IntPtr ByteArray;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvNode
{
    public MpvNodeUnion U;
    public int Format;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MpvNodeList
{
    public readonly int Num;
    public readonly IntPtr Values;
    public readonly IntPtr Keys;
}
