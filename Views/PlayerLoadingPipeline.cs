using System;
using Kiriha.Core.Mpv;
using Kiriha.ViewModels;
using Kiriha.Views.Controls;
using Serilog;

namespace Kiriha.Views;

internal sealed class PlayerLoadingPipeline
{
    private readonly PlayerViewModel _viewModel;
    private readonly MpvOpenGlVideoView _videoView;
    private bool _uiSubscribed;
    private bool _mediaLoadStarted;
    private bool _renderContextReady;
    private bool _initialFrameRequested;

    public PlayerLoadingPipeline(PlayerViewModel viewModel, MpvOpenGlVideoView videoView)
    {
        _viewModel = viewModel;
        _videoView = videoView;
    }

    public void AttachPlayer(MpvPlayer player)
    {
        if (_uiSubscribed)
            return;

        _viewModel.Initialize(player);
        _uiSubscribed = true;

        _videoView.Player = player;
        StartMediaLoad();
    }

    public void MarkRenderContextReady()
    {
        _renderContextReady = true;
        TryRequestInitialFrame();
    }

    private void StartMediaLoad()
    {
        if (!_uiSubscribed || _mediaLoadStarted || string.IsNullOrWhiteSpace(_viewModel.VideoUrl))
            return;

        _mediaLoadStarted = true;
        Log.Information("Loading video: {Url}", _viewModel.VideoUrl);
        _viewModel.LoadVideo(_viewModel.VideoUrl);
        TryRequestInitialFrame();
    }

    private void TryRequestInitialFrame()
    {
        if (_initialFrameRequested || !_mediaLoadStarted || !_renderContextReady)
            return;

        _initialFrameRequested = true;
        _videoView.RequestNextFrameRendering();
    }
}
