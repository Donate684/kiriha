using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Serilog;

namespace Kiriha.ViewModels;

public partial class SettingsViewModel
{
    [RelayCommand]
    public async Task MalLogin()
    {
        var tokens = await _authService.LoginAsync();
        if (tokens != null)
        {
            _settingsService.Update(settings => settings.Api.Mal = tokens, save: false);
            _settingsService.SaveImmediate();
            IsLoggedIn = true;
            ShikiLoginOneCommand.NotifyCanExecuteChanged();
            ShikiLoginNetCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public void MalLogout()
    {
        _settingsService.Update(settings =>
        {
            settings.Api.Mal = null;
            settings.Api.Shiki = null;
        }, save: false);
        _settingsService.SaveImmediate();
        IsLoggedIn = false;
        IsShikiLoggedIn = false;
        ShikiLoginOneCommand.NotifyCanExecuteChanged();
        ShikiLoginNetCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoginShikiOne))]
    public Task ShikiLoginOne() => LoginToMirrorAsync(ShikiMirror.One);

    [RelayCommand(CanExecute = nameof(CanLoginShikiNet))]
    public Task ShikiLoginNet() => LoginToMirrorAsync(ShikiMirror.Net);

    private async Task LoginToMirrorAsync(ShikiMirror mirror)
    {
        // Defensive: even if a stale UI somehow lets the user click the
        // disabled login button, refuse to start a second OAuth flow that
        // would overwrite the active mirror's tokens. Only one Shikimori
        // realm can be connected at a time.
        var current = _settingsService.Read(settings => settings.Api.Shiki);
        if (current != null && current.Mirror != mirror)
        {
            Log.Warning("Refused to log into Shikimori {Requested}: already connected to {Active}.",
                mirror, current.Mirror);
            return;
        }

        // Switch the active mirror first so ShikiAuthService picks up the right
        // OAuth endpoint + client_id when we kick off the browser flow.
        _settingsService.Update(settings => settings.Api.ShikiMirror = mirror, save: false);
        _settingsService.SaveImmediate();

        // Drop any host pin learned for the *previous* realm. Mixing a .net/.rip
        // pin into a .one OAuth flow (or vice-versa) would silently send the
        // login request to the wrong realm.
        _shikiHostResolver.Reset();

        var tokens = await _shikiAuthService.LoginAsync();
        if (tokens != null)
        {
            tokens.Mirror = mirror;
            _settingsService.Update(settings => settings.Api.Shiki = tokens, save: false);
            _settingsService.SaveImmediate();
            IsShikiLoggedIn = true;
        }
    }

    [RelayCommand]
    public void ShikiLogout()
    {
        _settingsService.Update(settings => settings.Api.Shiki = null, save: false);
        _settingsService.SaveImmediate();
        IsShikiLoggedIn = false;
    }
}
