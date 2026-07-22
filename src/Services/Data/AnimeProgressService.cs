using System;
using System.Threading.Tasks;
using Kiriha.Models.Entities;
using Kiriha.Services.Data.Repositories;
using Serilog;
using Kiriha.Models;
using Kiriha.Services.Api;

namespace Kiriha.Services.Data;

public class AnimeProgressService
{
    private readonly AnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly SyncManager _syncManager;
    private readonly HistoryService _historyService;

    public AnimeProgressService(
        AnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        SyncManager syncManager,
        HistoryService historyService)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _syncManager = syncManager;
        _historyService = historyService;
    }

    public async Task RemoveAnimeAsync(int animeId)
    {
        // Remove locally first so the UI is responsive even when offline.
        await _animeRepository.RemoveAnimeLocalAsync(animeId);

        // Persist a Remove sync task so the deletion is replayed against trackers
        try
        {
            await _syncManager.EnqueueRemoveAsync(animeId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeProgressService: Failed to enqueue Remove sync task for {AnimeId}", animeId);
        }
    }

    public virtual async Task<bool> UpdateProgressAsync(AnimeItem item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if ((nextStatus == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Completed) && item.StatusDetailed == "Not yet aired")
        {
            Log.Warning("Cannot set {Title} to {Status} - it has not aired yet.", item.Title, nextStatus);
            return false;
        }

        await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
        await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress, nextStatus);

        item.Progress = nextProgress;
        if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None) 
            item.Status = nextStatus.Value;
        
        return true;
    }

    public virtual async Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeItem item, int nextProgress)
    {
        UserAnimeStatus? nextStatus = null;
        if (item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.Completed)
            nextStatus = UserAnimeStatus.Watching;
        
        bool isManga = item.MediaKind != MediaKind.Anime;
        
        // Manga completion
        if (isManga && item.Chapters > 0 && nextProgress >= item.Chapters && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;
        // Anime completion
        else if (!isManga && item.TotalEpisodes > 0 && nextProgress >= item.TotalEpisodes && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;

        if (isManga)
        {
            item.ChaptersRead = nextProgress;
            if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None) 
                item.Status = nextStatus.Value;

            await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
            await _syncManager.EnqueueFullUpdateAsync(item);
            
            _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Read");
            return nextStatus;
        }
        else
        {
            if (await UpdateProgressAsync(item, nextProgress, nextStatus))
            {
                _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Watched");
                return nextStatus;
            }
        }
        
        return null;
    }

    public async Task SmartDecrementProgressAsync(AnimeItem item)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;

        if (isManga)
        {
            if (item.ChaptersRead > 0)
            {
                int nextProgress = item.ChaptersRead - 1;
                item.ChaptersRead = nextProgress;

                await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, null);
                await _syncManager.EnqueueFullUpdateAsync(item);
                
                _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted");
            }
        }
        else
        {
            if (item.Progress > 0)
            {
                int nextProgress = item.Progress - 1;
                if (await UpdateProgressAsync(item, nextProgress))
                {
                    _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted");
                }
            }
        }
    }

    public async Task SetScoreAsync(AnimeItem item, int score)
    {
        item.Score = score.ToString();
        await _userAnimeRepo.UpdateScoreAsync(item, item.Score);
        await _syncManager.EnqueueUpdateAsync(item.Id, item.Progress, score: score);
        _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, item.Progress, "ScoreSet", score.ToString());
    }
}
