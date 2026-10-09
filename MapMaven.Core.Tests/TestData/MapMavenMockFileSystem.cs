using MapMaven.Models.Data;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace MapMaven.Core.Tests.TestData
{
    public static class MapMavenMockFileSystem
    {
        private const string ReplacePath = "f:\\bslegacylauncher\\installed versions\\beat saber 1.29.1\\";

        public static string MockFilesBasePath => @"C:\TestData";

        public static MockFileSystem Get()
        {
            var songHashData = GetMockSongHashData(TestData.TestMaps.Value);

            var mockFiles = new Dictionary<string, MockFileData>
            {
                { $"{MockFilesBasePath}/UserData/SongCore/SongHashData.dat", new MockFileData(songHashData) },
            };

            AddMockMapInfo(mockFiles, TestData.TestMaps.Value);

            // Score recovery also refreshes estimates. These shipped assets must exist in
            // the mocked filesystem just as they do in the application output directory.
            foreach (var model in new[] { "ScoreSaberScoreEstimateMLModel.mlnet", "BeatLeaderScoreEstimateMLModel.mlnet" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "ScoreEstimation", model);
                mockFiles.Add(path, new MockFileData(File.ReadAllBytes(path)));
            }

            return new MockFileSystem(mockFiles);
        }

        private static string GetMockSongHashData(IEnumerable<MapInfo> maps)
        {
            var mapsJson = maps.Select(m =>
            {
                var path = m.DirectoryPath
                    .Replace(ReplacePath, string.Empty)
                    .Replace(@"\", @"\\");

                return $$"""
                    ".\\{{path}}": {
                        "directoryHash": 1,
                        "songHash": "{{m.Hash ?? Guid.NewGuid().ToString()}}"
                    }
                """;
            });

            return $$"""
            {
                {{string.Join(",", mapsJson)}}
            }
            """;
        }

        private static void AddMockMapInfo(Dictionary<string, MockFileData> mockFiles, IEnumerable<MapInfo> maps)
        {
            foreach (var map in maps)
            {
                AddMockMapInfo(mockFiles, map);
            }
        }

        private static void AddMockMapInfo(Dictionary<string, MockFileData> mapInfoDictionary, MapInfo mapInfo)
        {
            var mapInfoJson = JsonSerializer.Serialize(mapInfo);

            var directoryPath = mapInfo.DirectoryPath
                .Replace(ReplacePath, $"{MockFilesBasePath}/");

            mapInfoDictionary.Add($"{directoryPath}/", new MockDirectoryData());
            mapInfoDictionary.Add($"{directoryPath}/Info.dat", new MockFileData(mapInfoJson));
        }
    }
}
