using System;
using Kiriha.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Core.Navigation;

/// <summary>
/// Resolves transient ViewModels for navigation. Replaces the previous
/// <c>Func&lt;TViewModel&gt;</c> service-locator factories registered in
/// <c>App.ConfigureServices</c> — those bypassed the type system and forced
/// every "I need a fresh VM" site to add a parameter to the consumer's ctor.
///
/// For singletons, keep using direct constructor injection. This factory is
/// strictly for transient ViewModels (e.g. <see cref="WelcomeViewModel"/>,
/// <see cref="SearchViewModel"/>) that must be re-created on every navigation
/// to clear per-page state.
/// </summary>
public interface IViewModelFactory
{
    TViewModel Create<TViewModel>() where TViewModel : ViewModelBase;
}

/// <summary>
/// Default implementation backed by <see cref="IServiceProvider"/>. The
/// transient/singleton lifetime decision lives in DI registrations, so this
/// class is intentionally a thin pass-through.
/// </summary>
public sealed class ViewModelFactory : IViewModelFactory
{
    private readonly IServiceProvider _services;

    public ViewModelFactory(IServiceProvider services) => _services = services;

    public TViewModel Create<TViewModel>() where TViewModel : ViewModelBase =>
        _services.GetRequiredService<TViewModel>();
}
