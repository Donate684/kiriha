using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Services.Api;

/// <summary>
/// DelegatingHandler that adds retry logic with exponential backoff
/// for transient HTTP errors and rate-limiting (429).
/// </summary>
public class ResilientHttpHandler : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] Delays = {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    };

    public ResilientHttpHandler() : base() { }
    public ResilientHttpHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content != null && request.Content is not ByteArrayContent)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var newContent = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            var oldContent = request.Content;
            request.Content = newContent;
            oldContent.Dispose();
        }

        // Keep a reference to the "original" template request to clone from
        // We wrap it in a using block so that the request (and the new ByteArrayContent) is disposed.
        using var templateRequest = request;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60)); // 60 seconds timeout per attempt

            // Clone for this specific attempt (always clone to be safe with retry logic)
            using var currentRequest = await CloneRequestAsync(templateRequest, timeoutCts.Token);

            try
            {
                var response = await base.SendAsync(currentRequest, timeoutCts.Token);

                if (response.IsSuccessStatusCode || !IsTransient(response.StatusCode) || attempt == MaxRetries)
                {
                    // Restore the original request before returning
                    response.RequestMessage = templateRequest;
                    return response;
                }

                var delay = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? GetRetryAfter(response) ?? Delays[attempt]
                    : Delays[attempt];
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;

                Log.Warning("HTTP {StatusCode} on attempt {Attempt}/{Max}. Retrying in {Delay}s...",
                    (int)response.StatusCode, attempt + 1, MaxRetries, delay.TotalSeconds);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxRetries && (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                string reason = ex is TaskCanceledException ? "timeout" : "error";
                Log.Warning(ex, "HTTP request {Reason} on attempt {Attempt}/{Max}. Retrying in {Delay}s...",
                    reason, attempt + 1, MaxRetries, Delays[attempt].TotalSeconds);
                await Task.Delay(Delays[attempt], cancellationToken);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Genuine cancellation, don't retry
            }
            catch (TaskCanceledException ex) when (attempt == MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                // Per-attempt timeout fired on the LAST retry. Re-throw as HttpRequestException so
                // upstream code that special-cases OperationCanceledException doesn't mistake this
                // for a user-initiated cancellation (and, e.g., suppress retries in SyncManager).
                throw new HttpRequestException("HTTP request timed out after maximum retry attempts.", ex);
            }
            catch (HttpRequestException) when (attempt == MaxRetries)
            {
                throw; // Surface the real network failure on the last attempt
            }
        }

        throw new HttpRequestException("Maximum retry attempts reached without a successful response.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta;
        if (response.Headers.RetryAfter?.Date is { } date)
            return date - DateTimeOffset.UtcNow;
        return null;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original, CancellationToken cancellationToken = default)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        if (original.Content != null)
        {
            var contentBytes = await original.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(contentBytes);
            foreach (var header in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}
