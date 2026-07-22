using System;
using System.Threading.Tasks;

namespace Kiriha.Core.Infrastructure;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> action);
    Task InvokeAsync(Func<Task> action);
    void Post(Action action);
}
