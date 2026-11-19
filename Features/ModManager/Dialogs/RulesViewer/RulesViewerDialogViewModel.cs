#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using RimSharp.AppDir.Dialogs;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.ModManager.Dialogs.RulesViewer
{
    /// <summary>
    /// A small tag on a rule row. <see cref="Category"/> (Official / External /
    /// Custom / Unknown) is the strongest origin behind the tag and drives its
    /// styling: outlined = declared by the mod, filled = community rules source,
    /// amber = the user's own settings.
    /// </summary>
    public class RuleTagViewModel
    {
        public string Text { get; }
        public string Category { get; }
        public string Tooltip { get; }

        public RuleTagViewModel(string text, string category, string tooltip)
        {
            Text = text;
            Category = category;
            Tooltip = tooltip;
        }

        public static string CategoryOf(string originLabel) => originLabel switch
        {
            "Official" => "Official",
            "Custom" => "Custom",
            "Unknown" => "Unknown",
            _ => "External"
        };

        /// <summary>The most notable category of several origins: Custom > External > Official.</summary>
        public static string StrongestCategory(IEnumerable<string> originLabels)
        {
            var cats = originLabels.Select(CategoryOf).ToList();
            if (cats.Contains("Custom")) return "Custom";
            if (cats.Contains("External")) return "External";
            if (cats.Contains("Official")) return "Official";
            return "Unknown";
        }

        public static string Describe(string originLabel) => CategoryOf(originLabel) switch
        {
            "Official" => "Declared in the mod's own About.xml",
            "Custom" => "Added by your own custom settings",
            "External" => $"Contributed by a community rules source ({originLabel})",
            _ => "Origin could not be determined"
        };
    }

    /// <summary>
    /// One rule row shown in the read-only Rules viewer dialog, labelled with
    /// every origin that claimed it (About.xml, Custom, and/or a rule source).
    /// </summary>
    public class RuleRowViewModel : ViewModelBase
    {
        private string? _name;
        private string _title = string.Empty;
        private bool _showTarget;

        public string Kind { get; } // "Load Before", "Load After", "Incompatibility", "Load Top", "Load Bottom", "Supported Versions"
        public string Target { get; } = string.Empty;

        /// <summary>
        /// Resolved/declared mod name. Setting it (e.g. after live Workshop
        /// enrichment) refreshes <see cref="Title"/> and <see cref="ShowTarget"/>.
        /// Must be set on the UI thread once the row is displayed.
        /// </summary>
        public string? Name
        {
            get => _name;
            internal set
            {
                if (_name == value) return;
                _name = value;
                RefreshHeadline();
            }
        }

        public string? Comment { get; }
        public bool Hard { get; }
        public ObservableCollection<string> Origins { get; } = new();

        /// <summary>
        /// Tags shown on the right of the row. Set while the groups are built
        /// (before the view binds): kind tags for related-mod rows, non-Official
        /// origin tags for the other rows.
        /// </summary>
        public List<RuleTagViewModel> Tags { get; internal set; } = new();

        /// <summary>False for the first row of a section (no divider above it).</summary>
        public bool ShowDivider { get; internal set; }

        /// <summary>
        /// Primary headline: the resolved/declared mod name, falling back to the
        /// packageId when no name could be resolved.
        /// </summary>
        public string Title
        {
            get => _title;
            private set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        /// <summary>
        /// True when the packageId differs from the headline and should be shown
        /// as a secondary line.
        /// </summary>
        public bool ShowTarget
        {
            get => _showTarget;
            private set => this.RaiseAndSetIfChanged(ref _showTarget, value);
        }

        /// <summary>
        /// Supported game versions for the "Supported Versions" row; null for
        /// every other row.
        /// </summary>
        public List<VersionSupport>? Versions { get; }

        public bool HasVersions => Versions != null && Versions.Count > 0;

        /// <summary>All origin labels joined.</summary>
        public string SourcesDisplay { get; }

        public RuleRowViewModel(
            string kind,
            string target,
            string? name = null,
            string? comment = null,
            bool hard = false,
            List<VersionSupport>? versions = null,
            IEnumerable<string>? origins = null)
        {
            Kind = kind;
            Target = target;
            _name = name;
            Comment = comment;
            Hard = hard;
            Versions = versions;
            if (origins != null)
                foreach (var o in origins) Origins.Add(o);

            SourcesDisplay = string.Join(", ", Origins);
            RefreshHeadline();
        }

        private void RefreshHeadline()
        {
            Title = !string.IsNullOrWhiteSpace(_name) ? _name! : Target;
            ShowTarget = !string.IsNullOrWhiteSpace(Target) && !string.Equals(Title, Target, StringComparison.Ordinal);
        }
    }

    /// <summary>A titled section of related rule rows.</summary>
    public class RuleGroupViewModel
    {
        public string Header { get; }
        public string Description { get; }
        public bool ShowCount { get; }
        public List<RuleRowViewModel> Rows { get; }
        public int Count => Rows.Count;

        public RuleGroupViewModel(string header, string description, bool showCount, List<RuleRowViewModel> rows)
        {
            Header = header;
            Description = description;
            ShowCount = showCount;
            Rows = rows;
        }
    }

    public class RulesViewerDialogViewModel : DialogViewModelBase<bool>
    {
        private readonly IModService _modService;
        private readonly ModRuleSourceRegistry _registry;
        private readonly IModDictionaryService? _dictionary;
        private readonly string? _currentGameVersion;
        private readonly ISteamApiClient? _steamApi;
        private ModCustomInfo? _customInfo;
        private readonly Dictionary<string, string?> _targetCache = new(StringComparer.OrdinalIgnoreCase);

        // packageId -> numeric SteamId, populated only when a row's name was
        // resolved from a dictionary entry that carries a numeric SteamId.
        private readonly Dictionary<string, string> _dictionarySteamIdByTarget = new(StringComparer.OrdinalIgnoreCase);

        // row -> numeric SteamId, so InitializeAsync knows exactly which rows
        // are eligible for live Workshop enrichment.
        private readonly Dictionary<RuleRowViewModel, string> _dictionarySteamIds = new();

        public RulesViewerDialogViewModel(
            ModItem mod,
            IModService modService,
            ModRuleSourceRegistry registry,
            IModDictionaryService? dictionary = null,
            string? currentGameVersion = null,
            ISteamApiClient? steamApi = null)
            : base($"Rules — {mod.DisplayName}")
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _dictionary = dictionary;
            _currentGameVersion = currentGameVersion;
            _steamApi = steamApi;
            Mod = mod ?? throw new ArgumentNullException(nameof(mod));

            BuildRows();
            BuildGroups();
        }

        public ModItem Mod { get; }

        public ObservableCollection<RuleRowViewModel> Rows { get; } = new();

        public bool HasRules => Rows.Count > 0;

        /// <summary>Rows organised into titled sections; the view binds to this.</summary>
        public ObservableCollection<RuleGroupViewModel> Groups { get; } = new();

        /// <summary>One-line digest under the mod name.</summary>
        public string SummaryText { get; private set; } = string.Empty;

        /// <summary>
        /// Best-effort enrichment of dictionary-derived rows with live Workshop
        /// data: the published-file title replaces the dictionary name.
        /// Only rows whose name came from a dictionary entry with a numeric
        /// SteamId are touched. Every failure is swallowed so the dictionary
        /// values remain as the fallback and the dialog can always be shown.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_steamApi == null || _dictionarySteamIds.Count == 0)
                return;

            var distinctIds = _dictionarySteamIds.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctIds.Count == 0)
                return;

            var detailsById = new Dictionary<string, SteamPublishedFileDetails>(StringComparer.OrdinalIgnoreCase);
            var responseOk = false;
            try
            {
                var response = await _steamApi
                    .GetPublishedFileDetailsBatchAsync(distinctIds)
                    .ConfigureAwait(false);

                responseOk = response?.Response?.Result == 1;
                foreach (var detail in response?.Response?.PublishedFileDetails
                         ?? Enumerable.Empty<SteamPublishedFileDetails>())
                {
                    if (detail == null || string.IsNullOrWhiteSpace(detail.PublishedFileId))
                        continue;
                    if (!detailsById.ContainsKey(detail.PublishedFileId))
                        detailsById[detail.PublishedFileId] = detail;
                }
            }
            catch (Exception)
            {
                // Network/parse failures leave dictionary values untouched.
                return;
            }

            if (!responseOk || detailsById.Count == 0)
                return;

            foreach (var pair in _dictionarySteamIds)
            {
                var row = pair.Key;
                if (!detailsById.TryGetValue(pair.Value, out var detail))
                    continue;
                if (detail.Result != 1)
                    continue;

                try
                {
                    var title = detail.Title;
                    if (!string.IsNullOrWhiteSpace(title))
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => row.Name = title);
                }
                catch (Exception)
                {
                    // Never let a single row's enrichment break the others.
                }
            }
        }

        private void BuildRows()
        {
            Rows.Clear();

            // --- Load Before ---
            foreach (var target in Mod.LoadBefore)
            {
                var lbName = DescribeTarget(target);
                AddRow("Load Before", target, OriginsForLoadBefore(target), name: lbName);
            }

            // --- Force Load Before (About.xml only; no DB/custom provenance) ---
            foreach (var target in Mod.ForceLoadBefore)
            {
                var flbName = DescribeTarget(target);
                AddRow("Force Load Before", target, new List<string> { "Official" }, name: flbName);
            }

            // --- Load After ---
            foreach (var target in Mod.LoadAfter)
            {
                var laName = DescribeTarget(target);
                AddRow("Load After", target, OriginsForLoadAfter(target), name: laName);
            }

            // --- Force Load After (About.xml only; no DB/custom provenance) ---
            foreach (var target in Mod.ForceLoadAfter)
            {
                var flaName = DescribeTarget(target);
                AddRow("Force Load After", target, new List<string> { "Official" }, name: flaName);
            }

            // --- Dependencies (About.xml <modDependencies>; Official origin only) ---
            foreach (var dep in Mod.ModDependencies)
            {
                var depName = string.IsNullOrWhiteSpace(dep.DisplayName) ? null : dep.DisplayName;
                if (depName == null)
                    depName = DescribeTarget(dep.PackageId);
                AddRow("Dependency", dep.PackageId, new List<string> { "Official" }, name: depName);
            }

            // --- Incompatibilities ---
            foreach (var kvp in Mod.IncompatibleWith)
            {
                var target = kvp.Key;
                var rule = kvp.Value;
                var incName = rule.Name?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(incName))
                    incName = DescribeTarget(target);
                AddRow("Incompatibility", target, OriginsForIncompatibility(target), incName,
                    comment: CommentText(rule.Comment), hard: rule.HardIncompatibility);
            }

            // --- Load Top ---
            if (Mod.LoadTop)
            {
                var origins = new List<string>();
                if (CustomInfo.LoadTop != null) origins.Add("Custom");
                if (!string.IsNullOrEmpty(Mod.DatabaseLoadTopSource)) origins.Add(SourceLabel(Mod.DatabaseLoadTopSource));
                AddRow("Load Top", string.Empty, origins, name: "Pinned to the top of the load order");
            }

            // --- Load Bottom ---
            if (Mod.LoadBottom)
            {
                var origins = new List<string>();
                if (CustomInfo.LoadBottom != null) origins.Add("Custom");
                if (!string.IsNullOrEmpty(Mod.DatabaseLoadBottomSource)) origins.Add(SourceLabel(Mod.DatabaseLoadBottomSource));
                AddRow("Load Bottom", string.Empty, origins, name: "Pinned to the bottom of the load order");
            }

            // --- Supported Versions (legacy) ---
            if (Mod.SupportedVersions != null && Mod.SupportedVersions.Any())
            {
                var versionOrigins = new List<string>();
                foreach (var versionSupport in Mod.SupportedVersions)
                {
                    switch (versionSupport.Source)
                    {
                        case VersionSource.Official: versionOrigins.Add("Official"); break;
                        case VersionSource.External:
                            versionOrigins.Add(string.IsNullOrWhiteSpace(versionSupport.ExternalSourceName)
                                ? "External"
                                : $"External ({versionSupport.ExternalSourceName})");
                            break;
                        case VersionSource.Custom: versionOrigins.Add("Custom"); break;
                    }
                }
                versionOrigins = versionOrigins.Distinct().ToList();
                if (versionOrigins.Count == 0) versionOrigins.Add("Unknown");
                AddRow("Supported Versions", "Supported Versions", versionOrigins, versions: Mod.SupportedVersions);
            }
        }

        private static string Plural(int n, string one, string many) => n == 1 ? $"{n} {one}" : $"{n} {many}";

        /// <summary>
        /// Origin tags for a row. "Official" (declared in the mod's own About.xml)
        /// is the default and gets no tag; community-source and custom origins do.
        /// </summary>
        private static List<RuleTagViewModel> OriginTags(RuleRowViewModel row) =>
            row.Origins
                .Where(o => o != "Official")
                .Select(o => new RuleTagViewModel(o, RuleTagViewModel.CategoryOf(o), RuleTagViewModel.Describe(o)))
                .ToList();

        private void BuildGroups()
        {
            Groups.Clear();

            // One section per kind of rule, in reading order.
            var defs = new (string Kind, string Header, string Description, bool ShowCount)[]
            {
                ("Dependency",         "Dependencies",        "Mods this mod requires",                      true),
                ("Load After",         "Load After",          "This mod loads after these mods",             true),
                ("Force Load After",   "Force Load After",    "Forced: this mod loads after these mods",     true),
                ("Load Before",        "Load Before",         "This mod loads before these mods",            true),
                ("Force Load Before",  "Force Load Before",   "Forced: this mod loads before these mods",    true),
                ("Incompatibility",    "Incompatible with",   "Mods that conflict with this one",            true),
                ("Load Top",           "Load Top",            "Pinned to the top of the load order",         false),
                ("Load Bottom",        "Load Bottom",         "Pinned to the bottom of the load order",      false),
                ("Supported Versions", "Game versions",       "Versions this mod supports",                  false),
            };

            foreach (var def in defs)
            {
                var rows = Rows.Where(r => r.Kind == def.Kind).ToList();
                if (rows.Count == 0) continue;

                foreach (var r in rows) r.Tags = OriginTags(r);
                for (int i = 0; i < rows.Count; i++) rows[i].ShowDivider = i > 0;
                Groups.Add(new RuleGroupViewModel(def.Header, def.Description, def.ShowCount, rows));
            }

            // --- Summary line ---
            var deps = Rows.Count(r => r.Kind == "Dependency");
            var order = Rows.Count(r => r.Kind != "Dependency" && r.Kind != "Incompatibility" && r.Kind != "Supported Versions");
            var inc = Rows.Count(r => r.Kind == "Incompatibility");
            var ver = Rows.Where(r => r.Kind == "Supported Versions").Sum(r => r.Versions?.Count ?? 0);
            var parts = new List<string>();
            if (deps > 0) parts.Add(Plural(deps, "dependency", "dependencies"));
            if (order > 0) parts.Add(Plural(order, "load-order rule", "load-order rules"));
            if (inc > 0) parts.Add(Plural(inc, "incompatibility", "incompatibilities"));
            if (ver > 0) parts.Add(Plural(ver, "game version", "game versions"));
            SummaryText = string.Join("  ·  ", parts);
        }

        private void AddRow(string kind, string target, List<string> origins, string? name = null, string? comment = null, bool hard = false, List<VersionSupport>? versions = null)
        {
            if (origins.Count == 0) origins.Add("Unknown");
            var row = new RuleRowViewModel(kind, target, name, comment, hard, versions, origins);
            Rows.Add(row);

            // Remember rows whose name came from a dictionary entry with a
            // numeric SteamId; these are the only rows InitializeAsync enriches.
            if (_dictionarySteamIdByTarget.TryGetValue(target, out var steamId))
                _dictionarySteamIds[row] = steamId;
        }

        private List<string> OriginsForLoadBefore(string target)
        {
            var origins = new List<string>();
            if (Mod.AboutLoadBefore.Contains(target)) origins.Add("Official");
            if (Mod.DatabaseLoadBefore.TryGetValue(target, out var dbSrc)) origins.Add(SourceLabel(dbSrc));
            if (CustomInfo.LoadBefore.ContainsKey(target)) origins.Add("Custom");
            return origins;
        }

        private List<string> OriginsForLoadAfter(string target)
        {
            var origins = new List<string>();
            if (Mod.AboutLoadAfter.Contains(target)) origins.Add("Official");
            if (Mod.DatabaseLoadAfter.TryGetValue(target, out var dbSrc)) origins.Add(SourceLabel(dbSrc));
            if (CustomInfo.LoadAfter.ContainsKey(target)) origins.Add("Custom");
            return origins;
        }

        private List<string> OriginsForIncompatibility(string target)
        {
            var origins = new List<string>();
            if (Mod.AboutIncompatibleWith.Contains(target)) origins.Add("Official");
            if (Mod.DatabaseIncompatibleWith.TryGetValue(target, out var dbSrc)) origins.Add(SourceLabel(dbSrc));
            if (CustomInfo.IncompatibleWith.ContainsKey(target)) origins.Add("Custom");
            return origins;
        }

        private string SourceLabel(string sourceId) =>
            RuleProvenanceLabels.GetSourceDisplayName(sourceId, _registry);

        private ModCustomInfo CustomInfo => _customInfo ??= _modService.GetCustomModInfo(Mod.PackageId) ?? new ModCustomInfo();

        private static string? CommentText(List<string>? comments) =>
            comments != null && comments.Count > 0 ? string.Join(" ", comments) : null;

        /// <summary>
        /// Resolves a rule target (packageId) to a display name.
        /// Precedence: installed/loaded mod → mod dictionary service → unresolved.
        /// Results are cached so repeated targets don't re-scan.
        /// </summary>
        private string? DescribeTarget(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return null;

            if (_targetCache.TryGetValue(packageId, out var cached))
                return cached;

            var result = ResolveTarget(packageId);
            _targetCache[packageId] = result;
            return result;
        }

        private string? ResolveTarget(string packageId)
        {
            // 1. Installed / loaded mod.
            var mod = _modService.GetLoadedMods()
                .FirstOrDefault(m => string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (mod != null)
                return mod.DisplayName;

            // 2. Mod dictionary service.
            if (_dictionary != null)
            {
                var entry = SelectDictionaryEntry(packageId);
                if (entry != null)
                {
                    var name = !string.IsNullOrWhiteSpace(entry.SteamName) ? entry.SteamName
                        : !string.IsNullOrWhiteSpace(entry.AboutName) ? entry.AboutName
                        : !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name
                        : null;
                    if (!string.IsNullOrWhiteSpace(entry.SteamId) && entry.SteamId.All(char.IsDigit))
                        _dictionarySteamIdByTarget[packageId] = entry.SteamId;
                    return name;
                }
            }

            return null;
        }

        /// <summary>
        /// Picks a single dictionary entry deterministically when a packageId
        /// has multiple candidates (original + forks/ports).
        /// Priority: published first → supports current game version → highest
        /// max version → lowest numeric SteamId.
        /// </summary>
        private ModDictionaryEntry? SelectDictionaryEntry(string packageId)
        {
            var all = _dictionary!.GetAllEntriesByPackageId(packageId) ?? new List<ModDictionaryEntry>();
            var single = _dictionary!.GetEntryByPackageId(packageId);
            if (single != null && !all.Contains(single))
                all = all.Append(single).ToList();

            if (all.Count == 0) return null;
            if (all.Count == 1) return all[0];

            var published = all.Where(e => e.Published).ToList();
            var pool = published.Count > 0 ? published : all;

            // Supports the current game version.
            if (!string.IsNullOrEmpty(_currentGameVersion))
            {
                var supporting = pool.Where(e => e.Versions
                    .Any(v => string.Equals(v, _currentGameVersion, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (supporting.Count > 0)
                    pool = supporting;
            }

            // Most recent version (max parseable version per entry, then max across entries).
            var best = pool
                .Select(e => new
                {
                    Entry = e,
                    MaxVersion = MaxParseableVersion(e.Versions),
                    SteamIdLong = ParseSteamId(e.SteamId)
                })
                .OrderByDescending(x => x.MaxVersion)
                .ThenBy(x => x.SteamIdLong)
                .First();

            return best.Entry;
        }

        private static Version? MaxParseableVersion(IEnumerable<string> versions)
        {
            Version? max = null;
            foreach (var v in versions)
            {
                if (Version.TryParse(v, out var parsed))
                {
                    if (max == null || parsed > max)
                        max = parsed;
                }
            }
            return max;
        }

        /// <summary>
        /// Parses a SteamId as a long. Entries with a non-numeric/empty SteamId
        /// sort last (return long.MaxValue).
        /// </summary>
        private static long ParseSteamId(string? steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out var id))
                return long.MaxValue;
            return id;
        }
    }
}
