using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Api;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repositories;

/// <summary>
/// Persistence boundary for Shikimori metadata (the <c>metadata</c> table).
/// Stores the localised titles / synopses / studios fetched from the Shiki API
/// keyed by MAL id (Shiki and MAL share ids for matched titles).
/// </summary>
public interface IMetadataRepository
{
    /// <summary>Returns the cached entry, or null if we've never fetched it.</summary>
    Task<ShikiMetadata?> GetAsync(int id);

    /// <summary>
    /// Inserts or updates the entry. <see cref="ShikiMetadata.FetchedAt"/> is
    /// stamped to <see cref="DateTime.UtcNow"/> unconditionally so the TTL
    /// window is reset on every successful upsert.
    /// </summary>
    Task UpsertAsync(ShikiMetadata meta);

    /// <summary>Returns a set of all currently cached metadata IDs.</summary>
    Task<HashSet<int>> GetAllIdsAsync();
}

public sealed class MetadataRepository : IMetadataRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MetadataRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ShikiMetadata?> GetAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Metadata.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task UpsertAsync(ShikiMetadata meta)
    {
        meta.FetchedAt = DateTime.UtcNow;

        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.Metadata.FindAsync(meta.Id);
        if (existing == null)
            context.Metadata.Add(meta);
        else
            context.Entry(existing).CurrentValues.SetValues(meta);
        await context.SaveChangesAsync();
    }

    public async Task<HashSet<int>> GetAllIdsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var ids = await context.Metadata.Select(m => m.Id).ToListAsync();
        return new HashSet<int>(ids);
    }
}
