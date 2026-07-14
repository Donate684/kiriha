using System;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Mpv;

internal static class MpvPlayerLifecycle
{
    public static void Dispose(
        IntPtr handle,
        MpvCommandQueue commandQueue,
        MpvEventLoop eventLoop,
        Action<IntPtr> unobservePlaybackProperties)
    {
        if (handle == IntPtr.Zero)
        {
            eventLoop.Dispose();
            return;
        }

        if (!commandQueue.WaitForStop(TimeSpan.FromSeconds(2)))
            Log.Warning("Timed out waiting for mpv command loop to stop");

        if (!eventLoop.Stop(handle, TimeSpan.FromSeconds(1)))
            Log.Warning("Timed out waiting for mpv event loop to stop");

        unobservePlaybackProperties(handle);
        Terminate(handle);
        eventLoop.Dispose();
    }

    private static void Terminate(IntPtr handle)
    {
        var terminateTask = Task.Run(() =>
        {
            try
            {
                LibMpvNative.mpv_terminate_destroy(handle);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to terminate mpv");
            }
        });

        if (!MpvTask.Wait(terminateTask, TimeSpan.FromSeconds(2), "mpv termination"))
            Log.Warning("Timed out waiting for mpv to terminate");
    }
}
