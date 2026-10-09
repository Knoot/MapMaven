using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MapMaven.Core.Services;

public static class V4MapHasher
{
    // SongCore: original Info JSON, audio data, then each beatmap/lightshow pair in listed order.
    // https://github.com/Kylemc1413/SongCore/blob/master/source/SongCore/Utilities/Hashing.cs
    public static async Task<string> HashAsync(IFileSystem fileSystem, string directory, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var files = new[] { root.GetProperty("audio").GetProperty("audioDataFilename").GetString()! }
            .Concat(root.GetProperty("difficultyBeatmaps").EnumerateArray().SelectMany(d => new[]
            {
                d.GetProperty("beatmapDataFilename").GetString()!,
                d.GetProperty("lightshowDataFilename").GetString()!
            }));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.UTF8.GetBytes(json));
        foreach (var filename in files)
        {
            var path = fileSystem.Path.Combine(directory, filename);
            if (!fileSystem.File.Exists(path)) continue; // Matches SongCore's missing-file behavior.
            using var stream = fileSystem.File.OpenRead(path);
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer)) > 0) hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
