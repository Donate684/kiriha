using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Services.AppLifecycle;

public interface IBackgroundTaskSupervisor
{
    Task Run(string name, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken);
}
