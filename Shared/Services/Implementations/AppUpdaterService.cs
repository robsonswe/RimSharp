#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class AppUpdaterService : IAppUpdaterService
    {
        private readonly HttpClient _httpClient;
        private readonly ILoggerService _logger;
        private readonly IConfigService _configService;

        private const string GitHubReleasesApiUrl = "https://api.github.com/repos/robsonswe/RimSharp/releases?per_page=20";
        private const string UpdateChannelKey = "update_channel";

        public AppUpdaterService(IHttpClientFactory httpClientFactory, ILoggerService logger, IConfigService configService)
        {
            _httpClient = httpClientFactory.CreateClient("GitHub_Updater");
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "RimSharp-Updater");
            }
            _logger = logger;
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public async Task<(bool IsUpdateAvailable, string? LatestVersion, string? ReleaseUrl)> CheckForAppUpdateAsync()
        {
            try
            {
                _logger.LogInfo("Checking for RimSharp updates from GitHub API.", "AppUpdaterService");

                string channel = _configService.GetConfigValue(UpdateChannelKey);
                string channelLower = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.ToLowerInvariant().Trim();

                using var response = await _httpClient.GetAsync(GitHubReleasesApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"GitHub API returned status code: {response.StatusCode}", "AppUpdaterService");
                    return (false, null, null);
                }

                string json = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubReleaseInfo>>(json);

                if (releases == null || releases.Count == 0)
                {
                    _logger.LogWarning("No releases found on GitHub.", "AppUpdaterService");
                    return (false, null, null);
                }

                var currentSemVer = GetCurrentAppVersion();
                if (currentSemVer == null)
                {
                    _logger.LogWarning("Could not determine current application version.", "AppUpdaterService");
                    return (false, null, null);
                }

                // 1. Filtra os releases válidos para o canal
                // 2. Ordena usando o ReleaseCandidateComparer (Base Version primeiro, desempate por Data de Publicação)
                var latestEligible = releases
                    .Where(r => !r.Draft && !string.IsNullOrWhiteSpace(r.TagName))
                    .Where(r => IsReleaseEligibleForChannel(r, channelLower))
                    .Select(r => new ReleaseCandidate(r, SemanticVersion.Parse(r.TagName!)!))
                    .Where(x => x.Version != null)
                    .OrderByDescending(x => x, new ReleaseCandidateComparer())
                    .FirstOrDefault();

                if (latestEligible == null)
                {
                    _logger.LogInfo($"No releases found matching channel '{channelLower}'.", "AppUpdaterService");
                    return (false, null, null);
                }

                // Verifica se o candidato selecionado representa um update em relação ao app instalado
                if (IsCandidateAnUpdate(latestEligible, currentSemVer))
                {
                    string? zipUrl = latestEligible.Release.Assets?
                        .FirstOrDefault(a => a.BrowserDownloadUrl != null && 
                                             a.BrowserDownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?
                        .BrowserDownloadUrl;

                    string releaseUrl = zipUrl ?? latestEligible.Release.HtmlUrl ?? string.Empty;

                    _logger.LogInfo($"App update available in '{channelLower}' channel. Current: {currentSemVer}, Candidate: {latestEligible.Version} ({latestEligible.ReleaseDate:yyyy-MM-dd})", "AppUpdaterService");
                    return (true, latestEligible.Release.TagName, releaseUrl);
                }

                _logger.LogInfo($"App is up to date for '{channelLower}' channel. Current: {currentSemVer}, Latest: {latestEligible.Version}", "AppUpdaterService");
                return (false, latestEligible.Release.TagName, latestEligible.Release.HtmlUrl);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Failed to check for app updates from GitHub.", "AppUpdaterService");
                return (false, null, null);
            }
        }

        private static bool IsCandidateAnUpdate(ReleaseCandidate candidate, SemanticVersion current)
        {
            // Se a versão base (Major.Minor.Patch) for maior, é update
            if (candidate.Version.Major != current.Major) return candidate.Version.Major > current.Major;
            if (candidate.Version.Minor != current.Minor) return candidate.Version.Minor > current.Minor;
            if (candidate.Version.Patch != current.Patch) return candidate.Version.Patch > current.Patch;

            // Se for exatamente a mesma tag/versão, não precisa atualizar
            if (string.Equals(candidate.Version.ToString(), current.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;

            // Mesma versão base, mas com tags diferentes (ex: 2.0.0 vs 2.0.0-beta):
            // Aplica a ordem de progressão padrão (alpha < beta < estável)
            return candidate.Version > current;
        }

        private enum ChannelTier
        {
            Stable = 0,
            Beta = 1,
            Alpha = 2
        }

        private static ChannelTier GetReleaseTier(GitHubReleaseInfo release)
        {
            string tag = release.TagName?.ToLowerInvariant() ?? string.Empty;

            if (tag.Contains("alpha"))
                return ChannelTier.Alpha;

            if (tag.Contains("beta") || tag.Contains("-rc"))
                return ChannelTier.Beta;

            if (release.Prerelease)
                return ChannelTier.Alpha;

            return ChannelTier.Stable;
        }

        private static bool IsReleaseEligibleForChannel(GitHubReleaseInfo release, string userChannel)
        {
            var releaseTier = GetReleaseTier(release);

            return userChannel switch
            {
                "beta"   => releaseTier == ChannelTier.Stable || releaseTier == ChannelTier.Beta,
                "alpha"  => true, // Aceita qualquer um
                "stable" => releaseTier == ChannelTier.Stable,
                _        => releaseTier == ChannelTier.Stable
            };
        }

        private static SemanticVersion? GetCurrentAppVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIndex = infoVersion.IndexOf('+');
                if (plusIndex >= 0)
                    infoVersion = infoVersion.Substring(0, plusIndex);

                var parsed = SemanticVersion.Parse(infoVersion);
                if (parsed != null) return parsed;
            }

            var ver = assembly.GetName().Version;
            return ver != null ? new SemanticVersion(ver.Major, ver.Minor, Math.Max(ver.Build, 0)) : null;
        }

        private class ReleaseCandidate
        {
            public GitHubReleaseInfo Release { get; }
            public SemanticVersion Version { get; }
            public DateTimeOffset ReleaseDate => Release.PublishedAt ?? Release.CreatedAt ?? DateTimeOffset.MinValue;

            public ReleaseCandidate(GitHubReleaseInfo release, SemanticVersion version)
            {
                Release = release;
                Version = version;
            }
        }

        /// <summary>
        /// Ordenador de lançamentos:
        /// 1. Maior versão base (Major.Minor.Patch) vence.
        /// 2. Se a versão base for idêntica (ex: 2.0.0 vs 2.0.0-beta), DESEMPATA PELA DATA DE PUBLICAÇÃO (PublishedAt).
        /// </summary>
        private class ReleaseCandidateComparer : IComparer<ReleaseCandidate>
        {
            public int Compare(ReleaseCandidate? x, ReleaseCandidate? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                // 1. Compara Base Version (Major.Minor.Patch)
                if (x.Version.Major != y.Version.Major) return x.Version.Major.CompareTo(y.Version.Major);
                if (x.Version.Minor != y.Version.Minor) return x.Version.Minor.CompareTo(y.Version.Minor);
                if (x.Version.Patch != y.Version.Patch) return x.Version.Patch.CompareTo(y.Version.Patch);

                // 2. Desempate para mesma versão base: DATA MAIS RECENTE VENCE
                int dateCmp = x.ReleaseDate.CompareTo(y.ReleaseDate);
                if (dateCmp != 0) return dateCmp;

                // 3. Fallback se as datas forem exatamente iguais no segundo
                return x.Version.CompareTo(y.Version);
            }
        }

        private class GitHubReleaseInfo
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
            [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
            [JsonPropertyName("draft")] public bool Draft { get; set; }
            [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
            [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
            [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
        }

        private class GitHubAsset
        {
            [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        }

        public class SemanticVersion : IComparable<SemanticVersion>
        {
            public int Major { get; }
            public int Minor { get; }
            public int Patch { get; }
            public string PreRelease { get; }

            public SemanticVersion(int major, int minor, int patch, string preRelease = "")
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                PreRelease = preRelease;
            }

            public static SemanticVersion? Parse(string versionString)
            {
                if (string.IsNullOrWhiteSpace(versionString)) return null;

                string clean = versionString.Trim().TrimStart('v', 'V');
                string preRelease = string.Empty;

                int dashIndex = clean.IndexOf('-');
                if (dashIndex >= 0)
                {
                    preRelease = clean.Substring(dashIndex + 1);
                    clean = clean.Substring(0, dashIndex);
                }

                var parts = clean.Split('.');
                if (parts.Length < 1) return null;

                int major = int.TryParse(parts[0], out var maj) ? maj : 0;
                int minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0;
                int patch = parts.Length > 2 && int.TryParse(parts[2], out var pat) ? pat : 0;

                return new SemanticVersion(major, minor, patch, preRelease);
            }

            public int CompareTo(SemanticVersion? other)
            {
                if (other is null) return 1;

                if (Major != other.Major) return Major.CompareTo(other.Major);
                if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
                if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

                bool hasPre = !string.IsNullOrEmpty(PreRelease);
                bool otherHasPre = !string.IsNullOrEmpty(other.PreRelease);

                if (!hasPre && otherHasPre) return 1;
                if (hasPre && !otherHasPre) return -1;
                if (!hasPre && !otherHasPre) return 0;

                return ComparePreReleaseIdentifiers(PreRelease, other.PreRelease);
            }

            private static int ComparePreReleaseIdentifiers(string a, string b)
            {
                var partsA = a.Split('.', '-');
                var partsB = b.Split('.', '-');

                int minLen = Math.Min(partsA.Length, partsB.Length);
                for (int i = 0; i < minLen; i++)
                {
                    string pA = partsA[i];
                    string pB = partsB[i];

                    bool isNumA = int.TryParse(pA, out int numA);
                    bool isNumB = int.TryParse(pB, out int numB);

                    if (isNumA && isNumB)
                    {
                        int cmp = numA.CompareTo(numB);
                        if (cmp != 0) return cmp;
                    }
                    else if (isNumA != isNumB)
                    {
                        return isNumA ? -1 : 1;
                    }
                    else
                    {
                        int rankA = GetPreRank(pA);
                        int rankB = GetPreRank(pB);
                        if (rankA != rankB) return rankA.CompareTo(rankB);

                        int cmp = string.Compare(pA, pB, StringComparison.OrdinalIgnoreCase);
                        if (cmp != 0) return cmp;
                    }
                }

                return partsA.Length.CompareTo(partsB.Length);
            }

            private static int GetPreRank(string s)
            {
                string lower = s.ToLowerInvariant();
                if (lower.StartsWith("alpha")) return 1;
                if (lower.StartsWith("beta")) return 2;
                if (lower.StartsWith("rc")) return 3;
                return 0;
            }

            public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
            public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
            public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
            public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;

            public override string ToString() =>
                string.IsNullOrEmpty(PreRelease) ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
        }
    }
}