using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Kiriha.Tests;

public sealed class NowPlayingIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public NowPlayingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [Trait("Category", "Local")]
    [MemberData(nameof(GetTestFiles))]
    public async Task TestLocalLibraryRecognition(string mkvPath)
    {
        // Use the real DI container to get the actual MappingService used by TrackingService/NowPlaying
        var provider = AppStartupCoordinator.BuildServiceProvider(false);
        var dbInit = provider.GetRequiredService<DatabaseInitializer>();
        await dbInit.InitializeAsync();

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
            @"[Erai-raws] 29_sai Dokushin Chuuken Boukensha no Nichijou - 11 [1080p CR WEB-DL AVC AAC][MultiSub][A9D026C2].mkv",
            @"[Erai-raws] Alma-chan wa Kazoku ni Naritai - 08 [1080p CR WEB-DL AVC AAC][MultiSub][EEA49705].mkv",
            @"Chainsaw Man Reze Arc 2160p H.265 HDR.mkv",
            @"Champignon.Witch.S01E07.The.Magic.Diary.1080p.CR.WEB-DL.JPN.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"[SweetSub&LoliHouse] ChaO [WebRip 1080p HEVC-10bit AAC ASSx2].mkv",
            @"[SweetSub&VCB-Studio] CITY THE ANIMATION [09][Ma10p_1080p][x265_flac_aac].mkv",
            @"Fate-strange.Fake.S01E10.Episode.10.Gold.and.Lion.1080p.AMZN.WEB-DL.JPN.DDP2.0.H.264.ESub-ToonsHub.mkv",
            @"[Erai-raws] Gnosia - 20 [1080p CR WEB-DL AVC AAC][MultiSub][F2B87CDB].mkv",
            @"[ReinForce] Hibi wa Sugiredo Meshi Umashi 10 (BDRip 1920x1080 x264 FLAC).mkv",
            @"Farming.Life.In.Another.World.S02E03.Winter.1080p.AMZN.WEB-DL.AAC2.0.H.264-VARYG.mkv",
            @"[Erai-raws] Kamiina Botan - 10 [1080p CR WEB-DL AVC AAC][MultiSub][791CADE0].mkv",
            @"Kobayashi-san Chi no Maid Dragon Samishigariya no Ryuu.mkv",
            @"[Nekomoe kissaten&VCB-Studio] Medalist [07][Ma10p_1080p][x265_flac].mkv",
            @"Medalist.S02E02.1080p.DSNP.WEB-DL.DUAL.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"Monster.Strike.Deadverse.Reloaded.S01E06.1080p.AMZN.WEB-DL.JPN.DDP2.0.H.264-ToonsHub.mkv",
            @"New PANTY & STOCKING with GARTERBELT - 10.mkv",
            @"SHIBOYUGI.Playing.Death.Games.to.Put.Food.on.the.Table.S01E06.Whos.----ing.You.1080p.NF.WEB-DL.DUAL.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"SHIBOYUGI.Playing.Death.Games.to.Put.Food.on.the.Table.44.Cloudy.Beach.2026.1080p.NF.WEB-DL.DUAL.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            @"[SubsPlease] Ansatsusha de Aru Ore no Status ga Yuusha yori mo Akiraka ni Tsuyoi no da ga - 02v2 (1080p) [1DC09513].mkv",
            @"[Erai-raws] Tasokare Hotel - 01 [1080p][Multiple Subtitle][96D8709A].mkv",
            @"[Erai-raws] Tensei Akujo no Kuro Rekishi - 03 [1080p CR WEB-DL AVC AAC][MultiSub][A67CE6DE].mkv",
            @"Touhou Musou Kakyou - 02.5 [DVD 848x480p AVC AC3][UCCUSS].mkv",
            @"[Erai-raws] Towa no Yugure - 12 [1080p HIDIVE WEB-DL AVC AAC][55C86DAD].mkv",
            @"Virgin Punk Clockwork Girl [BD HEVC 1080p].mkv"
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
