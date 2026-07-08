using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data;

/// <summary>
/// Owns the schema lifecycle of the SQLite database. Strategy:
///   * <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/> applies
///     EF migrations from <c>Services/Data/Migrations</c>. The model is the
///     single source of truth (<see cref="AppDbContext.OnModelCreating"/>);
///     migrations are auto-generated via <c>dotnet ef migrations add</c>.
///   * One-shot adoption for legacy DBs created by <c>EnsureCreated</c>
///     before migrations existed: we detect the missing
///     <c>__EFMigrationsHistory</c> table and stamp it with the initial
///     migration row, so <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/>
///     treats the existing schema as already-applied instead of trying to
///     re-create tables and failing.
///   * WAL pragmas applied in a single batched statement (one round-trip on
///     cold start). <c>synchronous=NORMAL</c> is the safe-default for WAL —
///     durable across process kill — and <c>wal_autocheckpoint=200</c> caps
///     how much the WAL can outgrow the main file before being folded back.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted; // 0 = not started, 1 = started (Interlocked guard)

    public Task InitializationTask => _initTcs.Task;

    public DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InitializeAsync()
    {
        // Single-shot guard: every concurrent caller awaits the same TCS.
        if (Interlocked.CompareExchange(ref _initStarted, 1, 0) != 0)
        {
            await _initTcs.Task;
            return;
        }

        try
        {
            var total = Stopwatch.StartNew();
            await Task.Run(async () =>
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var stage = Stopwatch.StartNew();
                await context.Database.MigrateAsync();
                Log.Information("StartupTiming: database migrations elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

                // WAL + sane defaults in a single batch (one round-trip on cold start).
                stage.Restart();
                await context.Database.ExecuteSqlRawAsync(
                    "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA wal_autocheckpoint=200;");
                Log.Information("StartupTiming: database pragmas elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

                Log.Information("Database initialized elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);
            });
            _initTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize EF Core database");
            _initTcs.TrySetException(ex);
        }
    }


    /// <summary>
    /// Forces all pending WAL frames into the main database file. Call before
    /// app exit and on system session-end to guarantee durability against a
    /// forced <c>TerminateProcess</c>.
    /// </summary>
    public async Task FlushAsync()
    {
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
            Log.Information("DatabaseInitializer: WAL checkpoint(TRUNCATE) completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseInitializer: FlushAsync failed");
        }
    }
}
