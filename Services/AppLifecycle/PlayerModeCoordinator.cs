using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.ViewModels;
using Kiriha.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Services.AppLifecycle;

public sealed class PlayerModeCoordinator
{
    private readonly Application _app;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrayService _trayService;
    private PlayerCommandServer? _playerCommandServer;

    public PlayerModeCoordinator(Application app, IServiceProvider serviceProvider, TrayService trayService)
    {
        _app = app;
        _serviceProvider = serviceProvider;
        _trayService = trayService;
    }

    public static bool IsPlayerMode(string[] args) =>
        args.Any(arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));

    public void Initialize(string[] args)
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        _trayService.DisableTrayIcons();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartPlayerCommandServer();

        if (!PlayerProcessBridge.IsResident(args))
            desktop.MainWindow = CreatePlayerWindow(args);
    }

    private void StartPlayerCommandServer()
    {
        _playerCommandServer ??= new PlayerCommandServer(args =>
        {
            Dispatcher.UIThread.Post(() => HandlePlayerCommand(args));
        });
        _playerCommandServer.Start();
    }

    private void HandlePlayerCommand(string[] args)
    {
        if (args.Any(arg => arg.Equals(PlayerProcessBridge.ShutdownArg, StringComparison.OrdinalIgnoreCase)))
        {
            if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var playerWindow in desktop.Windows.OfType<PlayerWindow>().ToArray())
                    playerWindow.Close();

                desktop.Shutdown();
            }

            return;
        }

        if (args.Any(arg => arg.Equals(PlayerProcessBridge.UpdateMetadataArg, StringComparison.OrdinalIgnoreCase)))
        {
            ApplyPlayerMetadataCommand(args);
            return;
        }

        if (!IsPlayerMode(args) || PlayerProcessBridge.IsResident(args))
            return;

        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        if (settingsService.Current.Player.SingleWindow && TryReplacePlayerWindow(args))
            return;

        var window = CreatePlayerWindow(args);
        window.Show();
        window.Activate();
    }

    private bool TryReplacePlayerWindow(string[] args)
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        var window = desktop.Windows.OfType<PlayerWindow>().LastOrDefault();
        if (window?.DataContext is not PlayerViewModel vm)
            return false;

        var videoUrl = GetPlayerVideoUrl(args);
        if (string.IsNullOrWhiteSpace(videoUrl))
            return false;

        vm.LoadVideo(videoUrl);
        window.Show();
        window.Activate();
        return true;
    }

    private void ApplyPlayerMetadataCommand(string[] args)
    {
        var originalTitle = GetArgValue(args, "--original-title") ?? string.Empty;
        var titleRu = GetArgValue(args, "--title-ru") ?? string.Empty;
        var titleEn = GetArgValue(args, "--title-en") ?? string.Empty;
        var episodeText = GetArgValue(args, "--episode") ?? string.Empty;
        int? animeId = int.TryParse(GetArgValue(args, "--anime-id"), out var parsedAnimeId) ? parsedAnimeId : null;

        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var metadata = new PlayerMediaMetadata(titleRu, titleEn, episodeText, animeId);
        var playerWindows = desktop.Windows.OfType<PlayerWindow>().ToArray();
        var updated = false;

        foreach (var playerWindow in playerWindows)
        {
            if (playerWindow.DataContext is not PlayerViewModel vm)
                continue;

            if (!vm.MatchesOriginalTitle(originalTitle))
                continue;

            vm.ApplyExternalMetadata(metadata);
            updated = true;
        }

        if (!updated && playerWindows.LastOrDefault()?.DataContext is PlayerViewModel fallbackVm)
            fallbackVm.ApplyExternalMetadata(metadata);
    }

    private PlayerWindow CreatePlayerWindow(string[] args)
    {
        var videoUrl = GetPlayerVideoUrl(args);

        var metadataResolver = _serviceProvider.GetRequiredService<IPlayerMediaMetadataResolver>();
        var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
        var playerVm = new PlayerViewModel(videoUrl, metadataResolver.Resolve(videoUrl), metadataResolver, settingsService);
        return new PlayerWindow { DataContext = playerVm };
    }

    private static string? GetArgValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
            return null;

        var value = args[index + 1];
        return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
    }

    private static string GetPlayerVideoUrl(string[] args)
    {
        var playerArgIndex = Array.FindIndex(args, arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));
        if (playerArgIndex >= 0 && playerArgIndex + 1 < args.Length && !args[playerArgIndex + 1].StartsWith("--"))
            return args[playerArgIndex + 1];

        return string.Empty;
    }
}
