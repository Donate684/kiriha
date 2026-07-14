using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Kiriha.Models;

namespace Kiriha.Core.Dialogs;

/// <summary>
/// Abstraction over modal-window orchestration. Lets ViewModels and services
/// request user-facing dialogs without taking a hard dependency on Avalonia
/// types or on a static service locator.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the anime-details dialog for <paramref name="item"/>. Returns
    /// <c>true</c> if the user committed a change that the caller must reflect
    /// in its data, <c>false</c> otherwise (cancelled, closed, host hidden).
    /// </summary>
    /// <param name="sourceControl">Optional control whose top-level window is
    /// preferred as the dialog owner. When null, falls back to the main window.</param>
    Task<bool> ShowAnimeDetailsAsync(Control? sourceControl, AnimeItem item, CancellationToken ct = default);

    /// <summary>
    /// Surfaces the in-app update prompt. Driven by <c>UpdateService</c>; the
    /// concrete VM resolves whether the binary is already downloaded.
    /// </summary>
    Task ShowUpdateDialogAsync(bool isDownloaded = false, CancellationToken ct = default);
}
