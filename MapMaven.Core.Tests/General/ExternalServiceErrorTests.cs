using MapMaven.Core.ApiClients.BeatLeader;
using MapMaven.Core.ApiClients.ScoreSaber;
using MapMaven.Core.Models;
using MapMaven.Core.Services.Leaderboards;
using MapMaven.Core.Services.Interfaces;
using Moq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit.Abstractions;

namespace MapMaven.Core.Tests.General
{
    [CollectionDefinition("ExternalServiceErrorTests")]
    public class ExternalServiceErrorTests : CoreIntegrationTestCollection
    {
        public ExternalServiceErrorTests(ITestOutputHelper testOutputHelper, MapMavenTestBedFixture fixture) : base(testOutputHelper, fixture)
        {
            var scoreSaberApiClientMock = fixture.GetService<Mock<ScoreSaberApiClient>>(testOutputHelper)!;

            scoreSaberApiClientMock
                .SetupSequence(x => x.ScoresAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<Sort?>(), It.IsAny<int?>(), It.IsAny<bool?>()))
                .ThrowsAsync(new Exception("Test exception")) // First call throws an exception
                .Returns(async () =>
                {
                    await Task.Delay(50); // Ensure recovery assertions handle asynchronous responses.
                    return TestData.TestData.TestScoreSaberPlayerScores;
                });

            var beatLeaderApiClientMock = fixture.GetService<Mock<BeatLeaderApiClient>>(testOutputHelper)!;

            beatLeaderApiClientMock
                .SetupSequence(x => x.ScoresAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Order?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Requirements?>(), It.IsAny<ScoreFilterStatus?>(), It.IsAny<LeaderboardContexts?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float?>(), It.IsAny<float?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()))
                .ThrowsAsync(new Exception("Test exception")) // First call throws an exception
                .Returns(async () =>
                {
                    await Task.Delay(50);
                    return TestData.TestData.TestBeatLeaderPlayerScores;
                });
        }

        private Task<ErrorEvent> WaitForScoreError(string provider) =>
            _fixture.GetService<IApplicationEventService>(_testOutputHelper)!.ErrorRaised
                .Where(e => e.Message == $"Failed to load player scores from {provider}." && e.Exception.Message == "Test exception")
                .Timeout(TimeSpan.FromSeconds(30)).FirstAsync().ToTask();

        private static bool HasTestMap(IEnumerable<MapMaven.Models.Map> maps) =>
            maps.Any(m => m.Hash == "051709ED4264F353EA329FB8803780E45D3BF8E5");

        [Fact]
        public async Task ScoreSaberScoreRetrieval_AfterError_RecoversAfterError()
        {
            var error = WaitForScoreError("ScoreSaber");
            await ApplicationSettingService.AddOrUpdateAsync(ScoreSaberService.PlayerIdSettingKey, "test123");
            await LeaderboardService.SetActiveLeaderboardProviderAsync(LeaderboardProvider.ScoreSaber);
            await error;
            var maps = await MapService.Maps.Where(HasTestMap).Timeout(TimeSpan.FromSeconds(30)).FirstAsync();

            Assert.NotEmpty(maps);

            var testMap = maps.First(m => m.Hash == "051709ED4264F353EA329FB8803780E45D3BF8E5");

            Assert.Empty(testMap.AllPlayerScores);

            var recoveredMaps = MapService.Maps
                .Where(m => m.Any(map => map.Hash == "051709ED4264F353EA329FB8803780E45D3BF8E5" && map.AllPlayerScores.Any()))
                .Timeout(TimeSpan.FromSeconds(30)).FirstAsync().ToTask();
            await MapService.RefreshDataAsync(reloadMapAndLeaderboardInfo: true, forceReloadCachedData: true);

            maps = await recoveredMaps;

            testMap = maps.First(m => m.Hash == "051709ED4264F353EA329FB8803780E45D3BF8E5");

            Assert.NotEmpty(testMap.AllPlayerScores);

            var score = testMap.AllPlayerScores.First();

            Assert.Equal(100, score.Score.BaseScore);
        }

        [Fact]
        public async Task BeatLeaderScoreRetrieval_AfterError_RecoversAfterError()
        {
            var error = WaitForScoreError("BeatLeader");
            await ApplicationSettingService.AddOrUpdateAsync(BeatLeaderService.PlayerIdSettingKey, "test456");

            await LeaderboardService.SetActiveLeaderboardProviderAsync(LeaderboardProvider.BeatLeader);

            await error;
            var maps = await MapService.Maps.Where(HasTestMap).Timeout(TimeSpan.FromSeconds(30)).FirstAsync();

            Assert.NotEmpty(maps);

            var testMap = maps.First(m => m.Hash == "051709ED4264F353EA329FB8803780E45D3BF8E5");

            Assert.Empty(testMap.AllPlayerScores);

            var recoveredMaps = MapService.Maps
                .Where(m => m.Any(map => map.Hash == "051709ED4264F353EA329FB8803780E45D3BF8E5" && map.AllPlayerScores.Any()))
                .Timeout(TimeSpan.FromSeconds(30)).FirstAsync().ToTask();
            await MapService.RefreshDataAsync(reloadMapAndLeaderboardInfo: true, forceReloadCachedData: true);

            maps = await recoveredMaps;

            testMap = maps.First(m => m.Hash == "051709ED4264F353EA329FB8803780E45D3BF8E5");

            Assert.NotEmpty(testMap.AllPlayerScores);

            var score = testMap.AllPlayerScores.First();

            Assert.Equal(200, score.Score.BaseScore);
        }
    }
}
