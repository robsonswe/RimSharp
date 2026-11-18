using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Infrastructure.Mods.Replacements;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Versions;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Data
{
    public class DataUpdateService : IDataUpdateService
    {
        private readonly ILoggerService _logger;
        private readonly HttpClient _httpClient;
        private readonly string _dataRootPath;
        private readonly string _bundledDataSource;
        private readonly string _remoteManifestUrl = "https://raw.githubusercontent.com/robsonswe/RimSharpDB/main/manifest.json";
        private readonly string _remoteBaseUrl = "https://raw.githubusercontent.com/robsonswe/RimSharpDB/main/";
        private readonly string _useThisInsteadUrl = "https://raw.githubusercontent.com/emipa606/UseThisInstead/main/replacements.json.gz";
        private readonly string _noVersionWarningBaseUrl = "https://raw.githubusercontent.com/emipa606/NoVersionWarning/main";
        private readonly GameInstance? _gameInstance;
        private readonly string _noVersionWarningTreeUrl = "https://api.github.com/repos/emipa606/NoVersionWarning/git/trees/main";

        /// <summary>
        /// Cheap staleness probe for the ~48 MB Steam Workshop DB: the latest
        /// commit touching steamDB.json. Its sha is stored as the file's
        /// validator so an unchanged upstream file skips the large download.
        /// </summary>
        private const string SteamWorkshopDbCommitsApiUrl =
            "https://api.github.com/repos/RimSort/Steam-Workshop-Database/commits?path=steamDB.json&per_page=1";
        private readonly ModRuleSourceRegistry? _ruleSourceRegistry;
        private readonly ModInfoSourceRegistry? _modInfoSourceRegistry;
        private readonly ModReplacementSourceRegistry? _replacementSourceRegistry;
        private readonly NoVersionWarningSourceRegistry? _noVersionWarningSourceRegistry;
        // Fallback version list used when the repository tree cannot be
        // discovered (offline, rate-limited). Mirrors the repo's current
        // layout; unknown versions are fetched dynamically instead.
        private static readonly string[] NoVersionWarningFallbackVersions = { "1.3", "1.4", "1.5", "1.6", "1.7" };

        /// <summary>
        /// Destination of each curated manifest entry (the key in the manifest's
        /// "files" object) within the runtime data layout: every curated data file
        /// lives at Data/<category>/RimSharp/<file>. The manifest
        /// describes repo-side locations only; the app owns where files land.
        /// </summary>
        private static readonly Dictionary<string, string> CuratedFileDestinationNames = new(StringComparer.Ordinal)
        {
            ["rules"] = Path.Combine("Rules", "RimSharp", "rules.json"),
            ["replacements"] = Path.Combine("Replacements", "RimSharp", "replacements.json"),
            ["dictionary"] = Path.Combine("ModInfo", "RimSharp", "db.json"),
        };

        /// <summary>
        /// Bundled placeholder files that ship next to the executable, paired
        /// with their runtime destination (both relative to their respective
        /// roots). The bundled layout mirrors the runtime layout so a fresh
        /// install starts with valid empty JSON at every curated location.
        /// </summary>
        private static readonly (string BundledRelative, string LocalRelative)[] BundledSeedFiles =
        {
            (Path.Combine("Data", "ModInfo", "RimSharp", "db.json"), Path.Combine("ModInfo", "RimSharp", "db.json")),
            (Path.Combine("Data", "Rules", "RimSharp", "rules.json"), Path.Combine("Rules", "RimSharp", "rules.json")),
            (Path.Combine("Data", "Replacements", "RimSharp", "replacements.json"), Path.Combine("Replacements", "RimSharp", "replacements.json")),
        };

        public DataUpdateService(
            ILoggerService logger,
            string appBasePath,
            HttpClient httpClient,
            GameInstance? gameInstance = null,
            ModRuleSourceRegistry? ruleSourceRegistry = null,
            ModInfoSourceRegistry? modInfoSourceRegistry = null,
            ModReplacementSourceRegistry? replacementSourceRegistry = null,
            NoVersionWarningSourceRegistry? noVersionWarningSourceRegistry = null)
            : this(logger, appBasePath, httpClient, AppDataPaths.BaseDirectory, gameInstance, ruleSourceRegistry, modInfoSourceRegistry, replacementSourceRegistry, noVersionWarningSourceRegistry)
        {
        }

        internal DataUpdateService(
            ILoggerService logger,
            string appBasePath,
            HttpClient httpClient,
            string bundledDataSource,
            GameInstance? gameInstance = null,
            ModRuleSourceRegistry? ruleSourceRegistry = null,
            ModInfoSourceRegistry? modInfoSourceRegistry = null,
            ModReplacementSourceRegistry? replacementSourceRegistry = null,
            NoVersionWarningSourceRegistry? noVersionWarningSourceRegistry = null)
        {
            _gameInstance = gameInstance;
            _logger = logger;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _bundledDataSource = bundledDataSource;
            _ruleSourceRegistry = ruleSourceRegistry;
            _modInfoSourceRegistry = modInfoSourceRegistry;
            _replacementSourceRegistry = replacementSourceRegistry;
            _noVersionWarningSourceRegistry = noVersionWarningSourceRegistry;

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "RimSharp-App");
            }

            // Store data in a reliable, user-specific location
            _dataRootPath = Path.Combine(appBasePath, "Data");

            SeedFromBundledPlaceholders();
        }

        /// <summary>
        /// First-run seeding: copy the bundled placeholder files that ship next
        /// to the executable ("&lt;BaseDirectory&gt;/Data/...") into the writable
        /// data location, mirroring the runtime layout. Seeding is per-file and
        /// copy-if-missing: an existing local file (synced or user-edited) is
        /// never overwritten, and a missing file is healed independently of the
        /// others. On Windows the writable location IS the exe-side directory,
        /// so this is a no-op there.
        /// </summary>
        private void SeedFromBundledPlaceholders()
        {
            try
            {
                int seeded = 0;

                foreach (var (bundledRelative, localRelative) in BundledSeedFiles)
                {
                    var bundledPath = Path.Combine(_bundledDataSource, bundledRelative);
                    if (!File.Exists(bundledPath))
                    {
                        continue;
                    }

                    var localPath = Path.Combine(_dataRootPath, localRelative);

                    // Never copy a file onto itself (Windows case, where the
                    // bundled root and the data root are the same directory).
                    if (string.Equals(
                            Path.GetFullPath(bundledPath),
                            Path.GetFullPath(localPath),
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (File.Exists(localPath))
                    {
                        continue; // Already present: never overwrite live data.
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    File.Copy(bundledPath, localPath, overwrite: false);
                    seeded++;
                }

                if (seeded > 0)
                {
                    _logger.LogInfo($"Seeded {seeded} bundled placeholder file(s) into the data layout: {_bundledDataSource} -> {_dataRootPath}", nameof(DataUpdateService));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to seed bundled placeholders: {ex.Message}", nameof(DataUpdateService));
            }
        }

        public string GetExternalFilePath(string category, string source, string fileName)
        {
            return Path.Combine(_dataRootPath, category, source, fileName);
        }

        public async Task CheckForAndApplyUpdatesAsync(IProgress<DataUpdateProgress> progress, CancellationToken cancellationToken)
        {
            _logger.LogInfo("Checking for data file updates...", nameof(DataUpdateService));
            progress.Report(new DataUpdateProgress { Message = "Contacting server...", Percentage = 0 });

            try
            {
                // 1. Fetch the remote manifest. It is never stored locally: the
                //    per-file SHA-256 comparison below is the only update gate,
                //    which also self-heals a partially synced data folder.
                var remoteManifestJson = await _httpClient.GetStringAsync(_remoteManifestUrl, cancellationToken)
                    .ConfigureAwait(false);

                var remoteManifest = JsonDocument.Parse(remoteManifestJson);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new DataUpdateProgress { Message = "Comparing versions...", Percentage = 20 });

                // 2. Verify every curated file against the manifest's SHA-256
                //    and download only the missing or mismatched ones.
                var curatedFiles = remoteManifest.RootElement.GetProperty("files").EnumerateObject().ToList();
                int fileCount = curatedFiles.Count;
                int filesDownloaded = 0;

                foreach (var fileProperty in curatedFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string key = fileProperty.Name;
                    if (!CuratedFileDestinationNames.TryGetValue(key, out var destinationRelative))
                    {
                        _logger.LogWarning($"Unknown curated manifest entry '{key}' - no destination mapped, skipping.", nameof(DataUpdateService));
                        continue;
                    }

                    string destination = Path.Combine(_dataRootPath, destinationRelative);
                    string? expectedSha = fileProperty.Value.TryGetProperty("sha", out var shaElement)
                        ? shaElement.GetString()
                        : null;

                    if (File.Exists(destination) &&
                        !string.IsNullOrEmpty(expectedSha) &&
                        await IsFileHashMatchingAsync(destination, expectedSha, cancellationToken).ConfigureAwait(false))
                    {
                        _logger.LogDebug($"Curated file up to date: '{destination}'", nameof(DataUpdateService));
                        continue;
                    }

                    string relativePath = fileProperty.Value.GetProperty("path").GetString() ?? throw new InvalidOperationException("Invalid manifest: file path missing.");
                    string fileName = Path.GetFileName(relativePath);

                    int currentProgress = 25 + (int)((double)filesDownloaded / Math.Max(fileCount, 1) * 60);
                    progress.Report(new DataUpdateProgress { Message = $"Downloading {fileName}...", Percentage = currentProgress });

                    string remoteFileUrl = _remoteBaseUrl + relativePath;
                    var fileContent = await _httpClient.GetStringAsync(remoteFileUrl, cancellationToken)
                        .ConfigureAwait(false);

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await File.WriteAllTextAsync(destination, fileContent, cancellationToken)
                        .ConfigureAwait(false);
                    filesDownloaded++;

                    _logger.LogInfo($"Curated file updated: '{remoteFileUrl}' -> '{destination}'", nameof(DataUpdateService));
                }

                if (filesDownloaded == 0)
                {
                    _logger.LogInfo("Curated data files are up to date.", nameof(DataUpdateService));
                }
                else
                {
                    _logger.LogInfo($"Data files updated successfully ({filesDownloaded} of {fileCount} curated files downloaded).", nameof(DataUpdateService));
                }

                progress.Report(new DataUpdateProgress { Message = "Update Complete!", Percentage = 100 });
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Data update operation was cancelled.", nameof(DataUpdateService));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Failed to update data files. Using cached version.", nameof(DataUpdateService));
                throw new InvalidOperationException("Failed to download rule updates. Please check your internet connection.", ex);
            }
            finally
            {
                // External community sources sync on every startup, regardless
                // of the curated outcome (unchanged manifest short-circuits the
                // try block; a curated failure must not skip them either).
                // SyncExternalSourcesAsync isolates each source's failures.
                await SyncExternalSourcesAsync(cancellationToken).ConfigureAwait(false);

                // User-configured rule database sources (community rules and
                // any repoFile source the user added). The curated RimSharpDB
                // entry is skipped here: the SHA-gated sync above owns its file.
                if (_ruleSourceRegistry != null)
                {
                    await SyncRuleSourcesAsync(null, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        /// <summary>
        /// Computes the SHA-256 of a local file and compares it (case-insensitive)
        /// with the manifest's expected hex hash.
        /// </summary>
        private static async Task<bool> IsFileHashMatchingAsync(string filePath, string expectedSha, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(filePath);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            return string.Equals(Convert.ToHexString(hash), expectedSha, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Syncs every enabled repoFile rule database source from the registry
        /// (the curated RimSharpDB entry is excluded: its file is owned by the
        /// SHA-gated curated sync above). Each source is isolated: one failing
        /// (offline, moved, 404) never blocks the others or the startup flow.
        /// localFile sources are never fetched — they are read from disk as-is.
        /// </summary>
        public async Task SyncRuleSourcesAsync(IProgress<DataUpdateProgress>? progress, CancellationToken cancellationToken)
        {
            if (_ruleSourceRegistry == null)
            {
                return;
            }

            foreach (var source in _ruleSourceRegistry.GetEnabledSources())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(source.Id, ModRuleSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
                {
                    // The curated file is synced by the SHA-gated flow; fetching
                    // it here would duplicate (and could fight) that sync.
                    continue;
                }

                if (source.Type != ModRuleSourceType.RepoFile)
                {
                    _logger.LogDebug($"Rule source '{source.Id}' is a local file; no network sync needed.", nameof(DataUpdateService));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(source.Url))
                {
                    _logger.LogWarning($"Rule source '{source.Id}' has no URL configured; skipping its sync.", nameof(DataUpdateService));
                    continue;
                }

                var localPath = GetRuleSourceFilePath(source);
                if (localPath == null)
                {
                    _logger.LogWarning($"Could not derive a cache file name for rule source '{source.Id}' ('{source.Url}'); skipping its sync.", nameof(DataUpdateService));
                    continue;
                }

                try
                {
                    progress?.Report(new DataUpdateProgress { Message = $"Updating {source.Name}..." });
                    _logger.LogInfo($"Syncing rule source '{source.Id}' from '{source.Url}'...", nameof(DataUpdateService));
                    await SyncTextFileAsync(
                        source.Url, localPath, cancellationToken,
                        SourceContentValidator.ValidateRules, source.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Isolated failure: a broken source must never block the
                    // other sources or the startup flow.
                    _logger.LogWarning($"Could not sync rule source '{source.Id}': {ex.Message}", nameof(DataUpdateService));
                }
            }
        }

        /// <summary>
        /// Fetches and validates a single new rule source before it is added to
        /// the registry. Uses the same conditional GET and cache path as the
        /// startup sync. Throws a friendly <see cref="InvalidOperationException"/>
        /// when the download fails or the content is not a valid rules file.
        /// </summary>
        public async Task SyncNewRuleSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default)
        {
            var localPath = GetRuleSourceFilePath(new ModRuleSourceRecord { Id = sourceId, Url = url });
            if (localPath == null)
            {
                throw new InvalidOperationException("Could not derive a cache file name from the URL. Please check the URL and try again.");
            }

            string? rejection = null;
            try
            {
                _logger.LogInfo($"Fetching new rule source '{sourceId}' from '{url}'...", nameof(DataUpdateService));
                var accepted = await SyncTextFileAsync(
                    url, localPath, cancellationToken,
                    content => rejection = SourceContentValidator.ValidateRules(content),
                    sourceId).ConfigureAwait(false);
                if (!accepted)
                {
                    throw new InvalidOperationException($"The downloaded rules file is not valid ({rejection ?? "unexpected format"}). Please check the URL.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fetch new rule source '{sourceId}': {ex.Message}", nameof(DataUpdateService));
                throw new InvalidOperationException($"Could not fetch '{sourceId}'. Please check the URL and your internet connection.", ex);
            }
        }

        /// <summary>
        /// Cache location of a rule source file: Data/Rules/<sourceId>/<file
        /// name from the URL>. Mirrors the path resolution in
        /// JsonModRulesRepository so sync and load always agree.
        /// </summary>
        private string? GetRuleSourceFilePath(ModRuleSourceRecord source)
        {
            if (string.IsNullOrWhiteSpace(source.Url) || !Uri.TryCreate(source.Url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var fileName = Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            return GetExternalFilePath("Rules", source.Id, Uri.UnescapeDataString(fileName));
        }

        /// <summary>
        /// Syncs every external community source. Each source is isolated:
        /// one failing (offline, moved, 404) never blocks the others or the
        /// startup flow.
        /// </summary>
        private async Task SyncExternalSourcesAsync(CancellationToken cancellationToken)
        {
            if (_replacementSourceRegistry != null)
            {
                await SyncReplacementSourcesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SyncUseThisInsteadAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_noVersionWarningSourceRegistry != null)
            {
                await SyncNoVersionWarningSourcesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SyncNoVersionWarningAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_modInfoSourceRegistry != null)
            {
                await SyncModInfoSourcesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        #region ModInfo Sources Sync

        public async Task SyncModInfoSourcesAsync(CancellationToken cancellationToken)
        {
            if (_modInfoSourceRegistry == null) return;

            foreach (var source in _modInfoSourceRegistry.GetEnabledSources())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(source.Id, ModInfoSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (source.Type != ModRuleSourceType.RepoFile || string.IsNullOrWhiteSpace(source.Url))
                {
                    continue;
                }

                var localPath = GetModInfoSourceFilePath(source.Id, source.Url);
                if (localPath == null) continue;

                try
                {
                    _logger.LogInfo($"Syncing ModInfo source '{source.Id}' from '{source.Url}'...", nameof(DataUpdateService));
                    if (string.Equals(source.Id, ModInfoSourceRegistry.SteamWorkshopDbSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        // The ~48 MB community DB needs a cheap freshness probe
                        // before any conditional GET (raw.githubusercontent does
                        // not reliably serve usable ETags for large files).
                        await SyncSteamWorkshopDbAsync(source.Url, localPath, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await SyncTextFileAsync(
                            source.Url, localPath, cancellationToken,
                            SourceContentValidator.ValidateModInfo, source.Id).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not sync ModInfo source '{source.Id}': {ex.Message}", nameof(DataUpdateService));
                }
            }
        }

        /// <summary>
        /// Syncs the community Steam Workshop DB with bandwidth protection. A
        /// cheap GitHub commits API probe yields the sha of the latest commit
        /// touching steamDB.json; when it matches the stored validator the large
        /// download is skipped entirely. Otherwise the normal conditional GET
        /// runs and the sha is persisted as the validator on success. A failed
        /// probe simply falls back to the conditional GET, so an offline or
        /// rate-limited API never blocks this (or any other) source.
        /// </summary>
        private async Task SyncSteamWorkshopDbAsync(string url, string localPath, CancellationToken cancellationToken)
        {
            var validatorPath = localPath + ".etag";
            string? sha = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, SteamWorkshopDbCommitsApiUrl);
                request.Headers.UserAgent.ParseAdd("RimSharp");
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var commit in doc.RootElement.EnumerateArray())
                    {
                        if (commit.ValueKind == JsonValueKind.Object &&
                            commit.TryGetProperty("sha", out var shaElement) &&
                            shaElement.ValueKind == JsonValueKind.String)
                        {
                            sha = shaElement.GetString();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not check Steam Workshop DB freshness; falling back to conditional GET: {ex.Message}", nameof(DataUpdateService));
            }

            if (!string.IsNullOrEmpty(sha) && File.Exists(localPath) && File.Exists(validatorPath))
            {
                var stored = await File.ReadAllTextAsync(validatorPath, cancellationToken).ConfigureAwait(false);
                if (string.Equals(stored.Trim(), sha, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo($"Steam Workshop DB is up to date (sha {sha}); skipping download.", nameof(DataUpdateService));
                    return;
                }
            }

            var accepted = await SyncTextFileAsync(
                url, localPath, cancellationToken,
                SourceContentValidator.ValidateSteamWorkshopDb,
                ModInfoSourceRegistry.SteamWorkshopDbSourceId).ConfigureAwait(false);

            // Store the commit sha (not the HTTP ETag) so the next startup's
            // pre-check can short-circuit the download. Only do this after a
            // successful write so a rejected payload never marks itself fresh.
            if (accepted && !string.IsNullOrEmpty(sha))
            {
                await File.WriteAllTextAsync(validatorPath, sha, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetches and validates a single new ModInfo source before it is added
        /// to the registry. Throws a friendly <see cref="InvalidOperationException"/>
        /// when the download fails or the content is not a valid db.json file.
        /// </summary>
        public async Task SyncNewModInfoSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default)
        {
            var localPath = GetModInfoSourceFilePath(sourceId, url);
            if (localPath == null)
            {
                throw new InvalidOperationException("Could not derive a cache file name from the URL. Please check the URL and try again.");
            }

            string? rejection = null;
            try
            {
                _logger.LogInfo($"Fetching new ModInfo source '{sourceId}' from '{url}'...", nameof(DataUpdateService));
                var accepted = await SyncTextFileAsync(
                    url, localPath, cancellationToken,
                    content => rejection = SourceContentValidator.ValidateModInfo(content),
                    sourceId).ConfigureAwait(false);
                if (!accepted)
                {
                    throw new InvalidOperationException($"The downloaded mod info file is not valid ({rejection ?? "unexpected format"}). Please check the URL.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fetch new ModInfo source '{sourceId}': {ex.Message}", nameof(DataUpdateService));
                throw new InvalidOperationException($"Could not fetch '{sourceId}'. Please check the URL and your internet connection.", ex);
            }
        }

        /// <summary>
        /// Cache location of a ModInfo source file: Data/ModInfo/&lt;sourceId&gt;/
        /// &lt;file name from the URL&gt; (defaulting to db.json). Shared with the
        /// loader so sync and load always resolve the same path.
        /// </summary>
        public string? GetModInfoSourceFilePath(string sourceId, string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var fileName = Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrEmpty(fileName)) fileName = "db.json";

            return GetExternalFilePath("ModInfo", sourceId, Uri.UnescapeDataString(fileName));
        }

        #endregion

        #region Replacements Sources Sync

        public async Task SyncReplacementSourcesAsync(CancellationToken cancellationToken)
        {
            if (_replacementSourceRegistry == null) return;

            foreach (var source in _replacementSourceRegistry.GetEnabledSources())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(source.Id, ModReplacementSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (source.Type != ModRuleSourceType.RepoFile || string.IsNullOrWhiteSpace(source.Url))
                {
                    continue;
                }

                var localPath = GetReplacementSourceFilePath(source);
                if (localPath == null) continue;

                try
                {
                    _logger.LogInfo($"Syncing replacement source '{source.Id}' from '{source.Url}'...", nameof(DataUpdateService));
                    if (source.Url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    {
                        await SyncGzippedJsonAsync(
                            source.Url, localPath, cancellationToken,
                            SourceContentValidator.ValidateReplacements, source.Id).ConfigureAwait(false);
                    }
                    else
                    {
                        await SyncTextFileAsync(
                            source.Url, localPath, cancellationToken,
                            SourceContentValidator.ValidateReplacements, source.Id).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not sync replacement source '{source.Id}': {ex.Message}", nameof(DataUpdateService));
                }
            }
        }

        /// <summary>
        /// Fetches and validates a single new replacement source before it is
        /// added to the registry. Honors the .gz decompression path. Throws a
        /// friendly <see cref="InvalidOperationException"/> when the download
        /// fails or the content is not a valid replacements file.
        /// </summary>
        public async Task SyncNewReplacementSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default)
        {
            var localPath = GetReplacementSourceFilePath(new ModRuleSourceRecord { Id = sourceId, Url = url });
            if (localPath == null)
            {
                throw new InvalidOperationException("Could not derive a cache file name from the URL. Please check the URL and try again.");
            }

            string? rejection = null;
            Func<string, string?> validate = content => rejection = SourceContentValidator.ValidateReplacements(content);
            try
            {
                _logger.LogInfo($"Fetching new replacement source '{sourceId}' from '{url}'...", nameof(DataUpdateService));
                bool accepted;
                if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    accepted = await SyncGzippedJsonAsync(url, localPath, cancellationToken, validate, sourceId).ConfigureAwait(false);
                }
                else
                {
                    accepted = await SyncTextFileAsync(url, localPath, cancellationToken, validate, sourceId).ConfigureAwait(false);
                }
                if (!accepted)
                {
                    throw new InvalidOperationException($"The downloaded replacements file is not valid ({rejection ?? "unexpected format"}). Please check the URL.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fetch new replacement source '{sourceId}': {ex.Message}", nameof(DataUpdateService));
                throw new InvalidOperationException($"Could not fetch '{sourceId}'. Please check the URL and your internet connection.", ex);
            }
        }

        private string? GetReplacementSourceFilePath(ModRuleSourceRecord source)
        {
            if (string.IsNullOrWhiteSpace(source.Url) || !Uri.TryCreate(source.Url, UriKind.Absolute, out _))
            {
                return null;
            }

            return GetExternalFilePath("Replacements", source.Id, "replacements.json");
        }

        #endregion

        #region NoVersionWarning Sources Sync

        public async Task SyncNoVersionWarningSourcesAsync(CancellationToken cancellationToken)
        {
            if (_noVersionWarningSourceRegistry == null) return;

            foreach (var source in _noVersionWarningSourceRegistry.GetEnabledSources())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (source.Type != ModRuleSourceType.RepoFile || string.IsNullOrWhiteSpace(source.Url))
                {
                    continue;
                }

                await SyncNoVersionWarningForSourceAsync(source.Id, source.Url, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetches and validates a single new NoVersionWarning source (its
        /// per-version ModIdsToFix.xml tree) before it is added to the registry.
        /// Throws a friendly <see cref="InvalidOperationException"/> when no
        /// version snapshot could be fetched or the content is invalid.
        /// </summary>
        public async Task SyncNewNoVersionWarningSourceAsync(string sourceId, string url, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInfo($"Fetching new NoVersionWarning source '{sourceId}' from '{url}'...", nameof(DataUpdateService));
                var accepted = await SyncNoVersionWarningForSourceAsync(sourceId, url, cancellationToken).ConfigureAwait(false);
                if (!accepted)
                {
                    throw new InvalidOperationException($"No valid 'No Version Warning' snapshots could be fetched from '{url}'. Please check the URL and try again.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fetch new NoVersionWarning source '{sourceId}': {ex.Message}", nameof(DataUpdateService));
                throw new InvalidOperationException($"Could not fetch '{sourceId}'. Please check the URL and your internet connection.", ex);
            }
        }

        /// <summary>
        /// Syncs the per-version ModIdsToFix.xml tree of one source. Returns
        /// true when at least one version snapshot was accepted, false when
        /// every fetch failed or was rejected by validation. Per-version
        /// failures are isolated so one missing version never blocks the rest.
        /// </summary>
        private async Task<bool> SyncNoVersionWarningForSourceAsync(string sourceId, string baseUrl, CancellationToken cancellationToken)
        {
            try
            {
                var versions = await DiscoverNoVersionWarningVersionsAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInfo($"Syncing 'No Version Warning' version snapshots for {sourceId}: {string.Join(", ", versions)}", nameof(DataUpdateService));

                bool anyAccepted = false;
                foreach (var version in versions)
                {
                    var localXmlPath = GetExternalFilePath(
                        "Versions", sourceId, Path.Combine(version, "ModIdsToFix.xml"));
                    var trimmedBase = baseUrl.TrimEnd('/');
                    var url = $"{trimmedBase}/{version}/ModIdsToFix.xml";

                    try
                    {
                        if (await SyncTextFileAsync(
                                url, localXmlPath, cancellationToken,
                                SourceContentValidator.ValidateNoVersionWarningXml,
                                sourceId).ConfigureAwait(false))
                        {
                            anyAccepted = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not sync 'No Version Warning' snapshot for {version} ({sourceId}): {ex.Message}", nameof(DataUpdateService));
                    }
                }

                return anyAccepted;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not sync 'No Version Warning' version snapshots for {sourceId}: {ex.Message}", nameof(DataUpdateService));
                return false;
            }
        }

        #endregion

        /// <summary>
        /// Syncs the community "Use This Instead" replacement snapshot from the
        /// emipa606/UseThisInstead GitHub repository into
        /// Data/Replacements/UseThisInstead/replacements.json. Uses conditional
        /// requests (ETag / Last-Modified) so unchanged files are not rewritten.
        /// The .gz is fetched because it is ~8x smaller than the plain JSON.
        /// </summary>
        private async Task SyncUseThisInsteadAsync(CancellationToken cancellationToken)
        {
            var localJsonPath = GetExternalFilePath("Replacements", "UseThisInstead", "replacements.json");

            try
            {
                _logger.LogInfo("Syncing 'Use This Instead' repository snapshot...", nameof(DataUpdateService));
                await SyncGzippedJsonAsync(
                    _useThisInsteadUrl, localJsonPath, cancellationToken,
                    SourceContentValidator.ValidateReplacements, "UseThisInstead").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Isolated failure: a broken external source must never block
                // the curated-data sync or startup.
                _logger.LogWarning($"Could not sync 'Use This Instead' repository snapshot: {ex.Message}", nameof(DataUpdateService));
            }
        }

        /// <summary>
        /// Conditional-downloads a gzipped JSON file and writes the decompressed
        /// JSON to <paramref name="localJsonPath"/>. A validator (ETag, else
        /// Last-Modified) is persisted next to the file as "<file>.etag";
        /// when the server answers 304 Not Modified the local file is left
        /// untouched. <paramref name="validate"/> checks the decompressed
        /// content before it is written; a rejection keeps the cached file and
        /// its etag and returns false.
        /// </summary>
        private async Task<bool> SyncGzippedJsonAsync(
            string url,
            string localJsonPath,
            CancellationToken cancellationToken,
            Func<string, string?>? validate = null,
            string? sourceName = null)
        {
            var localDir = Path.GetDirectoryName(localJsonPath)!;
            Directory.CreateDirectory(localDir);
            var validatorPath = localJsonPath + ".etag";

            using var request = await CreateConditionalGetRequestAsync(url, validatorPath, cancellationToken).ConfigureAwait(false);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.LogInfo($"External source up to date: '{url}'", nameof(DataUpdateService));
                return true;
            }

            response.EnsureSuccessStatusCode();

            var compressed = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            string json;
            await using (var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
            {
                json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            if (validate != null)
            {
                var reason = validate(json);
                if (reason != null)
                {
                    _logger.LogWarning($"Rejected invalid content from source '{sourceName ?? url}' ({url}): {reason}. Keeping the cached file.", nameof(DataUpdateService));
                    return false;
                }
            }

            await File.WriteAllTextAsync(localJsonPath, json, cancellationToken).ConfigureAwait(false);
            await StoreValidatorAsync(response, validatorPath, cancellationToken).ConfigureAwait(false);

            _logger.LogInfo($"External source updated: '{url}' -> '{localJsonPath}'", nameof(DataUpdateService));
            return true;
        }

        /// <summary>
        /// Syncs the "No Version Warning" per-version ModIdsToFix.xml files
        /// from the emipa606/NoVersionWarning GitHub repository into
        /// Data/Versions/NoVersionWarning/&lt;version&gt;/ModIdsToFix.xml.
        /// The version directories are discovered from the repository tree
        /// (so new game majors appear without code changes) and fall back to
        /// a hardcoded list when discovery fails. Each version is fetched
        /// independently: a failure (including a 404 for a directory that no
        /// longer exists upstream) is logged and skipped.
        /// </summary>
        private async Task SyncNoVersionWarningAsync(CancellationToken cancellationToken)
        {
            try
            {
                var versions = await DiscoverNoVersionWarningVersionsAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInfo($"Syncing 'No Version Warning' version snapshots: {string.Join(", ", versions)}", nameof(DataUpdateService));

                foreach (var version in versions)
                {
                    var localXmlPath = GetExternalFilePath(
                        "Versions", "NoVersionWarning", Path.Combine(version, "ModIdsToFix.xml"));
                    var url = $"{_noVersionWarningBaseUrl}/{version}/ModIdsToFix.xml";

                    try
                    {
                        await SyncTextFileAsync(
                            url, localXmlPath, cancellationToken,
                            SourceContentValidator.ValidateNoVersionWarningXml,
                            "NoVersionWarning").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not sync 'No Version Warning' snapshot for {version}: {ex.Message}", nameof(DataUpdateService));
                    }
                }
            }
            catch (Exception ex)
            {
                // Isolated failure: a broken external source must never block
                // the curated-data sync or startup.
                _logger.LogWarning($"Could not sync 'No Version Warning' version snapshots: {ex.Message}", nameof(DataUpdateService));
            }
        }

        /// <summary>
        /// Builds the offline candidate version list used when repository
        /// discovery fails. The hardcoded seed (the repo's current layout)
        /// is never truncated: a game version lower than the seed keeps the
        /// full seed (older files still exist upstream). It is extended by
        ///  - the running game's major version, so a new game major (e.g.
        ///    1.8) is still fetched when the tree API is unreachable but raw
        ///    downloads work, and
        ///  - the version directories of the locally installed NoVersionWarning
        ///    mod, which is an always-available offline source that may
        ///    already know a newer game major.
        /// Versions that do not exist upstream are logged and skipped per file.
        /// </summary>
        private List<string> BuildNoVersionWarningOfflineVersions()
        {
            var versions = new List<string>(NoVersionWarningFallbackVersions);

            var gameVersion = TryGetGameMajorVersion();
            if (gameVersion != null && gameVersion.Minor > 2)
            {
                // The repository starts at 1.3; generate every seed-consistent
                // version up to and including the running game's major.
                for (int minor = 3; minor <= gameVersion.Minor; minor++)
                {
                    var candidate = $"1.{minor}";
                    if (!versions.Contains(candidate))
                    {
                        versions.Add(candidate);
                    }
                }
            }

            foreach (var modVersion in CollectInstalledNoVersionWarningVersions())
            {
                if (!versions.Contains(modVersion))
                {
                    versions.Add(modVersion);
                }
            }

            return versions;
        }

        /// <summary>
        /// Walks the locally installed NoVersionWarning mod
        /// ("2599504692") in every configured mod search path and returns its
        /// version directory names. The mod folder is offline, so this works
        /// even when the repository is unreachable.
        /// </summary>
        private IEnumerable<string> CollectInstalledNoVersionWarningVersions()
        {
            var results = new List<string>();

            try
            {
                foreach (var searchPath in _gameInstance?.GetModSearchPaths() ?? Enumerable.Empty<string>())
                {
                    var modPath = Path.Combine(searchPath, "2599504692");
                    if (!Directory.Exists(modPath))
                    {
                        continue;
                    }

                    foreach (var dir in Directory.GetDirectories(modPath))
                    {
                        var name = Path.GetFileName(dir);
                        if (Regex.IsMatch(name, @"^\d+\.\d+$") && !results.Contains(name))
                        {
                            results.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not read version directories from the installed NoVersionWarning mod: {ex.Message}", nameof(DataUpdateService));
            }

            return results;
        }

        /// <summary>
        /// Reads the running game's version from Version.txt next to the game
        /// executable (e.g. "1.7.5005 rev1082"), returning only the
        /// major.minor part as a Version. Returns null when unknown.
        /// </summary>
        private Version? TryGetGameMajorVersion()
        {
            try
            {
                var gameFolder = _gameInstance?.GameFolder;
                if (string.IsNullOrEmpty(gameFolder))
                {
                    return null;
                }

                var versionFile = Path.Combine(gameFolder, "Version.txt");
                if (!File.Exists(versionFile))
                {
                    return null;
                }

                var match = Regex.Match(File.ReadAllText(versionFile), @"(\d+)\.(\d+)");
                return match.Success
                    ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value))
                    : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not read the game version for the fallback list: {ex.Message}", nameof(DataUpdateService));
                return null;
            }
        }

        /// <summary>
        /// Discovers the repository's version directories (names matching
        /// "&lt;major&gt;.&lt;minor&gt;") via the GitHub tree API, unioned with a
        /// hardcoded fallback list. If the discovery request or its parsing
        /// fails, only the fallback list is used - sync continues either way.
        /// </summary>
        private async Task<List<string>> DiscoverNoVersionWarningVersionsAsync(CancellationToken cancellationToken)
        {
            var versions = BuildNoVersionWarningOfflineVersions();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _noVersionWarningTreeUrl);
                // The GitHub API requires a User-Agent on every request.
                request.Headers.UserAgent.ParseAdd("RimSharp");

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("tree", out var tree))
                {
                    foreach (var entry in tree.EnumerateArray())
                    {
                        if (entry.TryGetProperty("type", out var type) &&
                            type.ValueEquals("tree") &&
                            entry.TryGetProperty("path", out var path))
                        {
                            var name = path.GetString();
                            if (!string.IsNullOrEmpty(name) &&
                                Regex.IsMatch(name, @"^\d+\.\d+$") &&
                                !versions.Contains(name))
                            {
                                versions.Add(name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not discover 'No Version Warning' version directories; using the fallback list: {ex.Message}", nameof(DataUpdateService));
            }

            return versions
                .OrderBy(v => Version.Parse(v))
                .ToList();
        }

        /// <summary>
        /// Conditional-downloads a plain-text file (no decompression) and
        /// writes it to <paramref name="localPath"/>. A validator (ETag, else
        /// Last-Modified) is persisted next to the file as "&lt;file&gt;.etag";
        /// when the server answers 304 Not Modified the local file is left
        /// untouched. <paramref name="validate"/> checks the content before it
        /// is written; a rejection keeps the cached file and its etag and
        /// returns false.
        /// </summary>
        private async Task<bool> SyncTextFileAsync(
            string url,
            string localPath,
            CancellationToken cancellationToken,
            Func<string, string?>? validate = null,
            string? sourceName = null)
        {
            var localDir = Path.GetDirectoryName(localPath)!;
            Directory.CreateDirectory(localDir);
            var validatorPath = localPath + ".etag";

            using var request = await CreateConditionalGetRequestAsync(url, validatorPath, cancellationToken).ConfigureAwait(false);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.LogInfo($"External source up to date: '{url}'", nameof(DataUpdateService));
                return true;
            }

            response.EnsureSuccessStatusCode();

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (validate != null)
            {
                var reason = validate(text);
                if (reason != null)
                {
                    _logger.LogWarning($"Rejected invalid content from source '{sourceName ?? url}' ({url}): {reason}. Keeping the cached file.", nameof(DataUpdateService));
                    return false;
                }
            }

            await File.WriteAllTextAsync(localPath, text, cancellationToken).ConfigureAwait(false);
            await StoreValidatorAsync(response, validatorPath, cancellationToken).ConfigureAwait(false);

            _logger.LogInfo($"External source updated: '{url}' -> '{localPath}'", nameof(DataUpdateService));
            return true;
        }

        /// <summary>
        /// Builds a GET request carrying a conditional validator (If-None-Match
        /// for an ETag, else If-Modified-Since for a Last-Modified date) read
        /// from <paramref name="validatorPath"/>, if one was stored by a
        /// previous sync.
        /// </summary>
        private async Task<HttpRequestMessage> CreateConditionalGetRequestAsync(
            string url, string validatorPath, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (File.Exists(validatorPath))
            {
                var validator = await File.ReadAllTextAsync(validatorPath, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(validator))
                {
                    if (validator.StartsWith("W/") || validator.StartsWith("\""))
                    {
                        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(validator));
                    }
                    else if (DateTimeOffset.TryParse(validator, out var lastModified))
                    {
                        request.Headers.IfModifiedSince = lastModified;
                    }
                }
            }

            return request;
        }

        /// <summary>
        /// Persists the response's validator (ETag, else Last-Modified) so the
        /// next sync can send a conditional request.
        /// </summary>
        private static async Task StoreValidatorAsync(
            HttpResponseMessage response, string validatorPath, CancellationToken cancellationToken)
        {
            var newValidator = response.Headers.ETag?.ToString()
                ?? response.Content.Headers.LastModified?.ToString("R");
            if (!string.IsNullOrEmpty(newValidator))
            {
                await File.WriteAllTextAsync(validatorPath, newValidator, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}


