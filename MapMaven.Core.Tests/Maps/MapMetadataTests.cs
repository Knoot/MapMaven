using System.IO.Abstractions.TestingHelpers;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BeatSaber.SongHashing;
using MapMaven.Core.Models.Data;
using MapMaven.Core.Services;
using MapMaven.Core.Services.Interfaces;
using MapMaven.Infrastructure.Data;
using MapMaven.Models.Data;
using MapMaven.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MapMaven.Core.Tests.Maps;

public class MapMetadataTests
{
    private const string V4 = """
        {"version":"4.0.1","song":{"title":"Eye of the Storm","subTitle":"","author":"Battle Beast"},
         "audio":{"songFilename":"song.egg","songDuration":267.822,"audioDataFilename":"AudioData.dat","previewStartTime":76,"previewDuration":10},
         "coverImageFilename":"cover.jpg","difficultyBeatmaps":[
          {"beatmapAuthors":{"mappers":["Abe","Abe"],"lighters":["Light"]},"beatmapDataFilename":"Expert.dat","lightshowDataFilename":"Lights.dat"},
          {"beatmapAuthors":{"mappers":["Other"]},"beatmapDataFilename":"Expert.dat","lightshowDataFilename":"Lights.dat"}]}
        """;

    [Fact]
    public void ParsesNestedV4Metadata()
    {
        var info = MapMetadataParser.Parse(V4);
        Assert.Equal("Eye of the Storm", info.SongName);
        Assert.Equal("Battle Beast", info.SongAuthorName);
        Assert.Equal("Abe, Other", info.LevelAuthorName);
        Assert.Equal("song.egg", info.SongFileName);
        Assert.Equal("cover.jpg", info.CoverImageFilename);
        Assert.Equal(TimeSpan.FromSeconds(267.822), info.SongDuration);
        Assert.Equal(76, info.PreviewStartTimeInSeconds);
        Assert.Equal(10, info.PreviewDurationInSeconds);
    }

    [Theory]
    [InlineData("2.0.0", "_")]
    [InlineData("3.0.0", "_")]
    [InlineData("3.0.0", "")]
    public void ParsesLegacyMetadata(string version, string prefix)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [prefix + "version"] = version, [prefix + "songName"] = "Old song",
            [prefix + "songAuthorName"] = "Artist", [prefix + "levelAuthorName"] = "Mapper",
            [prefix + "songFilename"] = "old.egg", [prefix + "previewDuration"] = 12
        });
        var info = MapMetadataParser.Parse(json);
        Assert.Equal("Old song", info.SongName);
        Assert.Equal("Artist", info.SongAuthorName);
        Assert.Equal("Mapper", info.LevelAuthorName);
        Assert.Equal(12, info.PreviewDurationInSeconds);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"_songName\":null,\"_songFilename\":\"song.egg\"}")]
    [InlineData("{\"version\":\"5.0.0\"}")]
    [InlineData("{")]
    public void RejectsInvalidMetadata(string json) => Assert.ThrowsAny<Exception>(() => MapMetadataParser.Parse(json));

    [Fact]
    public async Task V4HashMatchesSongCoreByteOrderIncludingRepeatedFiles()
    {
        var fs = new MockFileSystem();
        var directory = Path.GetFullPath("hash-fixture");
        fs.AddFile(Path.Combine(directory, "AudioData.dat"), new MockFileData("audio"));
        fs.AddFile(Path.Combine(directory, "Expert.dat"), new MockFileData("notes"));
        fs.AddFile(Path.Combine(directory, "Lights.dat"), new MockFileData("lights"));
        var expected = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(V4 + "audio" + "notes" + "lights" + "notes" + "lights")));
        Assert.Equal(expected, await V4MapHasher.HashAsync(fs, directory, V4));
        Assert.Equal("notes", fs.File.ReadAllText(Path.Combine(directory, "Expert.dat")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedLibraryPersistsAndResolvesPlaylistAfterRestart(bool rejectOneCacheWrite)
    {
        var root = Path.Combine(Path.GetTempPath(), "MapMaven-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var fs = new MockFileSystem();
            var maps = Path.Combine(root, "Beat Saber_Data", "CustomLevels");
            fs.AddFile(Path.Combine(maps, "abc valid", "Info.dat"), new MockFileData(V4));
            fs.AddFile(Path.Combine(maps, "abd malformed", "Info.dat"), new MockFileData("{"));
            fs.AddFile(Path.Combine(maps, "abe missing title", "Info.dat"), new MockFileData(V4.Replace("\"title\":\"Eye of the Storm\"", "\"title\":null")));
            fs.AddDirectory(Path.Combine(maps, "abf missing info"));
            if (rejectOneCacheWrite)
                fs.AddFile(Path.Combine(maps, "ac0 cache rejected", "Info.dat"), new MockFileData(V4.Replace("Eye of the Storm", "Rejected")));
            var settings = new Mock<IApplicationSettingService>();
            settings.SetupGet(s => s.ApplicationSettings).Returns(Observable.Return(new Dictionary<string, ApplicationSetting>
            {
                ["BeatSaberInstallLocation"] = new() { StringValue = root }
            }));
            var services = new ServiceCollection();
            services.AddDbContext<MapMavenContext>(o => o.UseSqlite("Data Source=" + Path.Combine(root, "cache.db") + ";Pooling=False"));
            services.AddScoped<IDataStore>(p => p.GetRequiredService<MapMavenContext>());
            using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MapMavenContext>();
                await db.Database.EnsureCreatedAsync();
                if (rejectOneCacheWrite)
                    await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectMap BEFORE INSERT ON MapInfos WHEN NEW.SongName = 'Rejected' BEGIN SELECT RAISE(ABORT, 'test record failure'); END;");
            }

            BeatSaberDataService CreateService() => new(new Mock<IBeatmapHasher>(MockBehavior.Strict).Object,
                new BeatSaberFileService(settings.Object), provider, NullLogger<BeatSaberDataService>.Instance, fs);
            var first = CreateService();
            await first.MapInfoByHash.Where(m => m.Count > 0).FirstAsync();
            await first.LoadingMapInfo.Where(loading => !loading).FirstAsync();
            var loaded = await first.MapInfoByHash.FirstAsync();
            Assert.Equal(rejectOneCacheWrite ? 2 : 1, loaded.Count);
            var map = Assert.Single(loaded.Values.Where(m => m.SongName == "Eye of the Storm"));
            Assert.Equal("Eye of the Storm", map.SongName);
            using (var scope = provider.CreateScope())
            {
                var persisted = Assert.Single(await scope.ServiceProvider.GetRequiredService<MapMavenContext>().MapInfos.ToListAsync());
                Assert.Equal(map.Hash, persisted.Hash);
                Assert.Equal(map.SongDuration, persisted.SongDuration);
            }
            // Real playlist files, separate from the mocked map assets.
            File.WriteAllText(Path.Combine(root, "Playlists", "test.bplist"), JsonSerializer.Serialize(new
            {
                playlistTitle = "Regression", playlistAuthor = "Test", songs = new[] { new { hash = map.Hash, songName = map.SongName } }
            }));
            var restarted = CreateService();
            await restarted.MapInfoByHash.Where(m => m.Count > 0).FirstAsync();
            await restarted.LoadingMapInfo.Where(loading => !loading).FirstAsync();
            var reloaded = await restarted.MapInfoByHash.FirstAsync();
            var playlist = Assert.Single(await restarted.GetAllPlaylists());
            Assert.True(reloaded.ContainsKey(Assert.Single(playlist).Hash!));
            Assert.Equal(rejectOneCacheWrite ? 2 : 1, reloaded.Count);
            Assert.Equal(map.Hash, Assert.Single(reloaded.Values.Where(m => m.SongName == "Eye of the Storm")).Hash);
        }
        finally { Directory.Delete(root, true); }
    }
}
