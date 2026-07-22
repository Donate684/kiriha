using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Services.Maintenance;

public interface IMaintenanceTask
{
    TimeSpan InitialDelay { get; }
    TimeSpan Interval { get; }
    Task ExecuteAsync(CancellationToken ct);
}
