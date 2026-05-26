using System.Net;
using Kiriha.Services.Api;

namespace Kiriha.Tests;

public sealed class ResilientHttpHandlerTests
{
    [Fact]
    public async Task SendAsync_RetriesTransientResponseAndReturnsSuccess()
    {
        var inner = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(new ResilientHttpHandler(inner));

        using var response = await client.GetAsync("https://example.test/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task SendAsync_ReplaysBufferedRequestContentOnRetry()
    {
        var inner = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(new ResilientHttpHandler(inner));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/resource")
        {
            Content = new StringContent("payload")
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "payload", "payload" }, inner.Bodies);
    }

    [Fact]
    public async Task SendAsync_PastRetryAfterDoesNotCrashDelayCalculation()
    {
        var tooManyRequests = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        tooManyRequests.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            DateTimeOffset.UtcNow.AddMinutes(-1));

        var inner = new QueueHandler(tooManyRequests, new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(new ResilientHttpHandler(inner));

        using var response = await client.GetAsync("https://example.test/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryNonTransientResponse()
    {
        var inner = new QueueHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var client = new HttpClient(new ResilientHttpHandler(inner));

        using var response = await client.GetAsync("https://example.test/resource");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public async Task SendAsync_HonorsCallerCancellationWithoutRetry()
    {
        var inner = new CanceledHandler();
        using var client = new HttpClient(new ResilientHttpHandler(inner));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync("https://example.test/resource", cts.Token));

        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public async Task SendAsync_ConvertsFinalTimeoutToHttpRequestException()
    {
        var inner = new TimeoutHandler();
        using var client = new HttpClient(new ResilientHttpHandler(inner));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("https://example.test/resource"));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, inner.Attempts);
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int Attempts { get; private set; }
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return _responses.Dequeue();
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            throw new TaskCanceledException("synthetic timeout");
        }
    }

    private sealed class CanceledHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Expected cancellation before this point.");
        }
    }
}
