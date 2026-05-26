using System;
using System.Threading.Tasks;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repositories;

/// <summary>
/// Persistence boundary for the conditional-GET HTTP cache (the
/// <c>http_response_cache</c> table). Stores <c>ETag</c> / <c>Last-Modified</c>
/// alongside the body keyed by URL hash so subsequent requests can validate
/// cheaply via <c>If-None-Match</c> / <c>If-Modified-Since</c>.
///
/// TTL: 30 days hard-stop for entries the server stopped revalidating. Lookups
/// re-validate against the origin on every call, so even "old" cache entries
/// remain correct as long as the server confirms them via 304.
/// </summary>
public interface IHttpCacheRepository
{
    Task<HttpCacheEntry?> GetAsync(string urlHash);

    Task UpsertAsync(string urlHash, string? etag, string? lastModified, byte[] body);
}

public sealed class HttpCacheRepository : IHttpCacheRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public HttpCacheRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<HttpCacheEntry?> GetAsync(string urlHash)
    {
        if (string.IsNullOrEmpty(urlHash)) return null;
        using var context = await _contextFactory.CreateDbContextAsync();
        var entry = await context.HttpResponseCache.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UrlHash == urlHash);
        if (entry == null) return null;
        if (DateTime.UtcNow - entry.CreatedAt > Ttl) return null;
        return entry;
    }

    public async Task UpsertAsync(string urlHash, string? etag, string? lastModified, byte[] body)
    {
        if (string.IsNullOrEmpty(urlHash) || body == null) return;
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.HttpResponseCache
            .FirstOrDefaultAsync(e => e.UrlHash == urlHash);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            context.HttpResponseCache.Add(new HttpCacheEntry
            {
                UrlHash = urlHash,
                ETag = etag,
                LastModified = lastModified,
                Body = body,
                CreatedAt = now
            });
        }
        else
        {
            existing.ETag = etag;
            existing.LastModified = lastModified;
            existing.Body = body;
            existing.CreatedAt = now;
        }
        await context.SaveChangesAsync();
    }
}
