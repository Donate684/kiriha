namespace Kiriha.Services.Api;

/// <summary>
/// Outcome of a single tracker mutation (update / remove). Replaces the old
/// <c>bool</c> return so <see cref="SyncManager"/> can distinguish "server is
/// unreachable, retry later" from "anime doesn't exist on this tracker, never
/// going to succeed". The old contract conflated both as <c>false</c> and the
/// queue happily retried 404s for weeks.
///
/// Mapping rule of thumb for HTTP-backed trackers:
/// <list type="bullet">
///   <item><description>2xx → <see cref="Success"/></description></item>
///   <item><description>4xx that survived an auth refresh (404/400/422/401/403) → <see cref="PermanentFailure"/></description></item>
///   <item><description>5xx, 408, 429, network exceptions → <see cref="TransientFailure"/></description></item>
/// </list>
/// </summary>
public enum SyncOutcome
{
    /// <summary>Operation succeeded; the local change is now reflected on the tracker.</summary>
    Success,

    /// <summary>
    /// Server-side or network failure that is expected to clear up on its own.
    /// SyncManager will exponential-backoff and try again.
    /// </summary>
    TransientFailure,

    /// <summary>
    /// The tracker rejected the request in a way that won't change with retries
    /// (resource doesn't exist, payload invalid, auth permanently revoked).
    /// SyncManager treats this as "done with this tracker" — drops the per-tracker
    /// retry but doesn't surface a SyncFailed in history because the user-visible
    /// state is already consistent (the tracker doesn't have the entry to update).
    /// </summary>
    PermanentFailure,
}
