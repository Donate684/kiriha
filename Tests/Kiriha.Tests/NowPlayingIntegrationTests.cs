using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Kiriha.Services;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Kiriha.Services.AppLifecycle;
using Kiriha.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Tests;

public sealed class NowPlayingIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public NowPlayingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [MemberData(nameof(GetTestFiles))]
    public async Task TestLocalLibraryRecognition(string mkvPath)
    {
        // Use the real DI container to get the actual MappingService used by TrackingService/NowPlaying
        var provider = AppStartupCoordinator.BuildServiceProvider(false);
        var mappingService = provider.GetRequiredService<MappingService>();
        var userRepo = provider.GetRequiredService<IUserAnimeRepository>();

        var userList = await userRepo.GetAllAsync();
        
        // TrackingService operates on the OriginalTitle (which for local players is typically the filename)
        string originalTitle = Path.GetFileNameWithoutExtension(mkvPath);

        // Mimic TrackingService's MatchMediaAsync logic
        int? malId = await mappingService.GetIdFromTitleAsync(originalTitle, userList);
        
        if (!malId.HasValue)
        {
            malId = await mappingService.SearchOnMalAsync(originalTitle);
        }

        var matched = malId.HasValue ? userList.FirstOrDefault(x => x.Id == malId.Value) : null;

        _output.WriteLine($"File:    {Path.GetFileName(mkvPath)}");
        _output.WriteLine($"AnimeId: {malId?.ToString() ?? "null"}");
        if (matched != null)
        {
            _output.WriteLine($"Matched: {matched.Title} (RU: {matched.RussianTitle})");
        }
    }
    public static TheoryData<string> GetTestFiles()
    {
        var data = new TheoryData<string>
        {
            @"D:\29-sai Nichijou\[Erai-raws] 29_sai Dokushin Chuuken Boukensha no Nichijou - 11 [1080p CR WEB-DL AVC AAC][MultiSub][A9D026C2].mkv",
            @"D:\Alma-chan wa Kazoku ni Naritai\[Erai-raws] Alma-chan wa Kazoku ni Naritai - 08 [1080p CR WEB-DL AVC AAC][MultiSub][EEA49705].mkv",
            @"D:\Chainsaw Man Reze Arc\Chainsaw Man Reze Arc 2160p H.265 HDR.mkv",
            @"D:\Champignon no Majo\Champignon.Witch.S01E07.The.Magic.Diary.1080p.CR.WEB-DL.JPN.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"D:\ChaO\[SweetSub&LoliHouse] ChaO [WebRip 1080p HEVC-10bit AAC ASSx2].mkv",
            @"D:\CITY the Animation\[SweetSub&VCB-Studio] CITY THE ANIMATION [09][Ma10p_1080p][x265_flac_aac].mkv",
            @"D:\Fate Strange Fake\Fate-strange.Fake.S01E10.Episode.10.Gold.and.Lion.1080p.AMZN.WEB-DL.JPN.DDP2.0.H.264.ESub-ToonsHub.mkv",
            @"D:\Gnosia\[Erai-raws] Gnosia - 20 [1080p CR WEB-DL AVC AAC][MultiSub][F2B87CDB].mkv",
            @"D:\Hibi wa Sugiredo Meshi Umashi\[ReinForce] Hibi wa Sugiredo Meshi Umashi 10 (BDRip 1920x1080 x264 FLAC).mkv",
            @"D:\Isekai Nonbiri Nouka 2\Farming.Life.In.Another.World.S02E03.Winter.1080p.AMZN.WEB-DL.AAC2.0.H.264-VARYG.mkv",
            @"D:\Kamiina Botan\[Erai-raws] Kamiina Botan - 10 [1080p CR WEB-DL AVC AAC][MultiSub][791CADE0].mkv",
            @"D:\Kobayashi-san Samishigariya no Ryuu\Kobayashi-san Chi no Maid Dragon Samishigariya no Ryuu.mkv",
            @"D:\Medalist\[Nekomoe kissaten&VCB-Studio] Medalist [07][Ma10p_1080p][x265_flac].mkv",
            @"D:\Medalist Season 2\Medalist.S02E02.1080p.DSNP.WEB-DL.DUAL.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"D:\Monster Strike Deadverse Reloaded\Monster.Strike.Deadverse.Reloaded.S01E06.1080p.AMZN.WEB-DL.JPN.DDP2.0.H.264-ToonsHub.mkv",
            @"D:\New PANTY & STOCKING with GARTERBELT\New PANTY & STOCKING with GARTERBELT - 10.mkv",
            @"D:\SHIBOYUGI\SHIBOYUGI.Playing.Death.Games.to.Put.Food.on.the.Table.S01E06.Whos.----ing.You.1080p.NF.WEB-DL.DUAL.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"D:\SHIBOYUGI 44 Cloudy Beach\SHIBOYUGI.Playing.Death.Games.to.Put.Food.on.the.Table.44.Cloudy.Beach.2026.1080p.NF.WEB-DL.DUAL.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"D:\Sutetsuyo\[SubsPlease] Ansatsusha de Aru Ore no Status ga Yuusha yori mo Akiraka ni Tsuyoi no da ga - 02v2 (1080p) [1DC09513].mkv",
            @"D:\Tasokare Hotel\[Erai-raws] Tasokare Hotel - 01 [1080p][Multiple Subtitle][96D8709A].mkv",
            @"D:\Tensei Akujo no Kuro Rekishi\[Erai-raws] Tensei Akujo no Kuro Rekishi - 03 [1080p CR WEB-DL AVC AAC][MultiSub][A67CE6DE].mkv",
            @"D:\Touhou Musou Kakyou\Touhou Musou Kakyou - 02.5 [DVD 848x480p AVC AC3][UCCUSS].mkv",
            @"D:\Towa no Yugure\[Erai-raws] Towa no Yugure - 12 [1080p HIDIVE WEB-DL AVC AAC][55C86DAD].mkv",
            @"D:\Virgin Punk Clockwork Girl\Virgin Punk Clockwork Girl [BD HEVC 1080p].mkv"
        };
        return data;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly string _dbPath;

        public TestDbContextFactory(string dbPath)
        {
            _dbPath = dbPath;
        }

        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options;
            
            return new AppDbContext(options);
        }
    }
}
