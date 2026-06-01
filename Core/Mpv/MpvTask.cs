using System;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Mpv;

internal static class MpvTask
{
    public static bool Wait(Task? task, TimeSpan timeout, string operation)
    {
        if (task == null)
            return true;

        try
        {
            return task.Wait(timeout);
        }
        catch (AggregateException ex)
        {
            Log.Warning(ex.Flatten(), "Failed while waiting for {Operation}", operation);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed while waiting for {Operation}", operation);
            return true;
        }
    }
}
