using Kiriha.Core.Mpv;
using Kiriha.ViewModels.Player;
using Kiriha.Views.Controls;
using Serilog;

namespace Kiriha.Views.Player;

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
        TryStartMediaLoad();
    }

    public void MarkRenderContextReady()
    {
        _renderContextReady = true;
        TryStartMediaLoad();
    }

    private void TryStartMediaLoad()
    {
        if (!_uiSubscribed ||
            !_renderContextReady ||
            _mediaLoadStarted ||
            string.IsNullOrWhiteSpace(_viewModel.VideoUrl))
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
