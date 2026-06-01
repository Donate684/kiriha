using System;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Utils;

public static class TaskExtensions
{
    /// <summary>
    /// Safely fire-and-forget a task, logging any unhandled exceptions
    /// instead of letting them become UnobservedTaskExceptions.
    /// </summary>
    public static async void SafeFireAndForget(this Task task, string context = "", Action<Exception>? onException = null)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation — no need to log as error
        }
        catch (Exception ex)
        {
            onException?.Invoke(ex);
            Log.Error(ex, "Unhandled exception in fire-and-forget: {Context}", context);
        }
    }

    /// <summary>
    /// Safely fire-and-forget a ValueTask.
    /// </summary>
    public static async void SafeFireAndForget(this ValueTask task, string context = "", Action<Exception>? onException = null)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            onException?.Invoke(ex);
            Log.Error(ex, "Unhandled exception in fire-and-forget (ValueTask): {Context}", context);
        }
    }

    /// <summary>
    /// Explicitly ignore the Task result (suppresses compiler warnings).
    /// </summary>
    public static void Forget(this Task task)
    {
        // No-op
    }
}
