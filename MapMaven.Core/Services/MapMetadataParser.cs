using System.Text.Json;
using MapMaven.Models.Data;

namespace MapMaven.Core.Services;

/// <summary>Converts Info.dat formats into the application's persisted metadata.</summary>
public static class MapMetadataParser
{
    public static bool IsV4(JsonElement root)
    {
        var version = Text(root, "version") ?? Text(root, "_version");
        if (version == null) return false; // Legacy files and existing serialized cache fixtures.
        if (!Version.TryParse(version, out var parsed) || parsed.Major < 2 || parsed.Major > 4)
            throw new JsonException($"Unsupported Info.dat version: {version}");
        return parsed.Major == 4;
    }

    public static MapInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        MapInfo info;
        if (IsV4(root))
        {
            var song = root.GetProperty("song");
            var audio = root.GetProperty("audio");
            var mappers = root.GetProperty("difficultyBeatmaps").EnumerateArray()
                .Where(d => d.TryGetProperty("beatmapAuthors", out _))
                .SelectMany(d => d.GetProperty("beatmapAuthors").TryGetProperty("mappers", out var authors)
                    ? authors.EnumerateArray().Select(a => a.GetString() ?? "") : Enumerable.Empty<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal);
            info = new MapInfo
            {
                SongName = Text(song, "title")!,
                SongAuthorName = Text(song, "author") ?? "",
                LevelAuthorName = string.Join(", ", mappers),
                SongFileName = Text(audio, "songFilename")!,
                CoverImageFilename = Text(root, "coverImageFilename") ?? "",
                SongDuration = TimeSpan.FromSeconds(Number(audio, "songDuration")),
                PreviewStartTimeInSeconds = (float)Number(audio, "previewStartTime"),
                PreviewDurationInSeconds = (float)Number(audio, "previewDuration")
            };
        }
        else
        {
            info = new MapInfo
            {
                SongName = LegacyText(root, "songName")!,
                SongAuthorName = LegacyText(root, "songAuthorName") ?? "",
                LevelAuthorName = LegacyText(root, "levelAuthorName") ?? "",
                SongFileName = LegacyText(root, "songFilename")!,
                CoverImageFilename = LegacyText(root, "coverImageFilename") ?? "",
                PreviewStartTimeInSeconds = (float)LegacyNumber(root, "previewStartTime"),
                PreviewDurationInSeconds = (float)LegacyNumber(root, "previewDuration")
            };
        }
        Validate(info);
        return info;
    }

    public static void Validate(MapInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.SongName)) throw new JsonException("Missing song title.");
        if (string.IsNullOrWhiteSpace(info.SongFileName)) throw new JsonException("Missing song filename.");
        if (info.SongAuthorName == null || info.LevelAuthorName == null || info.CoverImageFilename == null)
            throw new JsonException("Null author or cover metadata.");
        if (!float.IsFinite(info.PreviewStartTimeInSeconds) || !float.IsFinite(info.PreviewDurationInSeconds))
            throw new JsonException("Invalid preview timing.");
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static string? LegacyText(JsonElement root, string name) => Text(root, "_" + name) ?? Text(root, name);
    private static double Number(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetDouble() : 0;
    private static double LegacyNumber(JsonElement root, string name) => root.TryGetProperty("_" + name, out var value) ? value.GetDouble() : Number(root, name);
}
