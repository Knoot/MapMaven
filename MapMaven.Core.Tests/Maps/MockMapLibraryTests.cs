using MapMaven.Core.Tests.TestData;

namespace MapMaven.Core.Tests.Maps;

public class MockMapLibraryTests
{
    [Fact]
    public void CreatingMultipleLibrariesPreservesSourcePathsAndSongCoreHashes()
    {
        var paths = TestData.TestData.TestMaps.Value.Select(m => m.DirectoryPath).ToArray();
        var first = MapMavenMockFileSystem.Get();
        var second = MapMavenMockFileSystem.Get();
        var hashFile = Path.Combine(MapMavenMockFileSystem.MockFilesBasePath, "UserData", "SongCore", "SongHashData.dat");

        Assert.Equal(paths, TestData.TestData.TestMaps.Value.Select(m => m.DirectoryPath));
        Assert.Equal(first.File.ReadAllText(hashFile), second.File.ReadAllText(hashFile));
        Assert.Equal(first.AllFiles.OrderBy(p => p), second.AllFiles.OrderBy(p => p));
    }
}
