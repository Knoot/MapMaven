# Windows V4 test build

Push the patch to a GitHub repository/fork. Open **Actions → Windows V4 test build → Run workflow** and select the branch containing the patch. After the test and publish steps succeed, download the **MapMaven-V4-win-x64** artifact. Extract the artifact ZIP and the application ZIP inside it into a writable folder. Keep every file in the application folder together.

Close the original MapMaven application, then double-click **Launch-MapMaven.cmd**. This launcher keeps settings, database and logs in a separate `data` folder next to the patched executable and skips creating the normal startup shortcut. Set the Beat Saber installation to `X:\BSManager\BSInstances\1.40.8` and select your leaderboard/player settings again. Existing playlists are read from that installation's `Playlists` directory. Updating a live playlist or saving a playlist still writes to that directory as usual.

Do not launch `MapMaven.exe` directly if you want isolated application data: direct launch uses the existing `%ProgramData%\MapMaven` settings/database. No maps or metadata are rewritten by the parser. The existing CustomLevels junction is enumerated normally; no junction relocation is needed.

The ZIP bundles .NET and the Windows App SDK. No Visual Studio or .NET SDK is required. Windows x64 (Windows 10 version 1809 or later) and the **Microsoft Edge WebView2 Evergreen Runtime** are required for the Blazor interface. WebView2 is usually already installed; if it is missing, install the runtime from https://developer.microsoft.com/microsoft-edge/webview2/ (this is a browser runtime, not development tooling). The workflow uses the Windows runner's build tools and installs the MAUI workload in CI.

Publishing follows Microsoft's unpackaged MAUI guidance: https://learn.microsoft.com/dotnet/maui/windows/deployment/publish-unpackaged-cli . The entire publish folder is retained, including native SQLite/ML dependencies, assets and model files. This workflow creates a downloadable test artifact, not a Squirrel installer or an upstream release, and does not replace the original installation.

## Metadata and indexing changes

The legacy `MapInfo` JSON attributes only bind `_songName`, `_songFilename`, etc. V4 nests these under `song` and `audio`, leaving required SQLite columns null. The old loader cached the entire collection before notifying the UI, so a single failed insertion hid the library.

`MapMetadataParser` converts V4 and legacy V2/V3 metadata explicitly, maps distinct mappers into `LevelAuthorName`, and rejects missing titles/song filenames or unsupported versions. The model has no subtitle field, so subtitles remain unsupported. V4 song duration is retained rather than overwritten by audio probing. Legacy underscored Info metadata remains supported even when difficulty data uses a newer format; unprefixed legacy metadata is also accepted.

`BeatSaberDataService` logs/skips invalid folders, validates current metadata even with a cached hash, publishes valid maps before cache writes, and writes each record in a separate scope/transaction. Stale cache rows are removed separately. No database migration is needed. SongCore cached identifiers are preserved. Without a cached identifier, V4 hashing follows SongCore's original JSON + audio data + ordered beatmap/lightshow bytes (including repeated references); older maps retain the existing hasher. Source: https://github.com/Kylemc1413/SongCore/blob/master/source/SongCore/Utilities/Hashing.cs .

Regression tests cover V4 fields, V2/V3, missing/malformed metadata, mixed libraries, SQLite persistence/reload, hash ordering, and real `.bplist` hash resolution after service restart. Existing recommendation/live-playlist tests run in CI as well. Actual UI behavior, a large junction-backed library, and launching the ZIP on a clean Windows machine still need manual verification.

## Verification on 2026-10-09

All **33 Release tests passed** locally, including a real SQLite trigger that rejects one map while other maps remain visible and persist. The self-contained **Windows x64 publish succeeded** using the existing .NET SDK 10.0.103 with the application's unchanged .NET 8 target. No SDK, Visual Studio or MAUI workload was installed on the user's PC. The published output contains `MapMaven.exe`, `coreclr.dll`, `Microsoft.ui.xaml.dll`, `e_sqlite3.dll`, web assets and both prediction model files. Existing compiler/analyzer warnings remain.

The GitHub Actions workflow uses .NET 8 and has not been run remotely in this session. The local ZIP is in `artifacts/MapMaven-V4-win-x64.zip`; use the same extraction/launcher instructions above. WebView2 is an external runtime prerequisite. No clean-machine launch or test against the user's actual Beat Saber maps was performed.
