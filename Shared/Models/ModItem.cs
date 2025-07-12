using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions; // Added for Regex

namespace RimSharp.Shared.Models
{
    public enum ModType
    {
        Core = 0,
        Expansion = 1,
        Workshop = 2,
        WorkshopL = 3,
        Git = 4,
        Zipped = 5
    }

    public class ModItem
    {
        // Required fields from About.xml
        public string Name { get; set; }
        public string PackageId { get; set; }
        public string Authors { get; set; }
        public string Description { get; set; }
        public List<VersionSupport> SupportedVersions { get; set; } = new List<VersionSupport>();

        // Optional fields from About.xml
        public string ModVersion { get; set; }
        public string ModIconPath { get; set; }
        public string Url { get; set; }
        public List<ModDependency> ModDependencies { get; set; } = new List<ModDependency>();
        public List<ModDependency> ModDependenciesByVersion { get; set; } = new List<ModDependency>();
        public List<string> LoadBefore { get; set; } = new List<string>();
        public List<string> LoadBeforeByVersion { get; set; } = new List<string>();
        public List<string> ForceLoadBefore { get; set; } = new List<string>();
        public List<string> LoadAfter { get; set; } = new List<string>();
        public List<string> LoadAfterByVersion { get; set; } = new List<string>();
        public List<string> ForceLoadAfter { get; set; } = new List<string>();
        public Dictionary<string, ModIncompatibilityRule> IncompatibleWith { get; set; } = new Dictionary<string, ModIncompatibilityRule>(StringComparer.OrdinalIgnoreCase);
        public List<string> IncompatibleWithByVersion { get; set; } = new List<string>();

        // Additional fields
        public string SteamId { get; set; }
        public string SteamUrl { get; set; }
        public string ExternalUrl { get; set; }
        public string Path { get; set; }
        public bool IsActive { get; set; }
        public ModType ModType { get; set; }

        public bool LoadBottom { get; set; }
        public string PreviewImagePath { get; set; }

        public bool IsOutdatedRW { get; set; }

        public bool HasUrl => !string.IsNullOrEmpty(Url);
        public bool HasSteamUrl => !string.IsNullOrEmpty(SteamUrl);
        public bool HasExternalUrl => !string.IsNullOrEmpty(ExternalUrl);

        public string GitRepo { get; set; }

        public string DateStamp { get; set; }
        public string UpdateDate { get; set; }

        public string Tags { get; set; }

        // <<< NEW PROPERTY >>>
        /// <summary>
        /// Gets or sets a value indicating whether this mod contains C# assembly (.dll) files.
        /// Checked in `Assemblies` folders (root or versioned). Defaults to false.
        /// </summary>
        public bool Assemblies { get; set; } = false;

        public IEnumerable<string> SupportedVersionStrings => SupportedVersions.Select(v => v.Version);

        // New: Efficiently parsed Author list
        private List<string> _authorList;
        public List<string> AuthorList => _authorList ??= ParseCommaSeparatedString(Authors);

        // New: Efficiently parsed Tag list
        private List<string> _tagList;
        public List<string> TagList => _tagList ??= ParseCommaSeparatedString(Tags);
        /// <summary>
        /// Gets or sets a value indicating whether this mod instance has detected issues
        /// within the current active list context (e.g., missing dependencies, order violations).
        /// This property is managed externally (e.g., by ModListManager).
        /// </summary>
        public bool HasIssues { get; set; } // We'll update this from ModListManager

        /// <summary>
        /// Gets or sets the detailed tooltip text explaining the issues found for this mod
        /// in the current active list context.
        /// This property is managed externally (e.g., by ModListManager).
        /// </summary>
        public string IssueTooltipText { get; set; } // We'll update this from ModListManager

        /// <summary>
        /// Invalidates the cached tag list, forcing it to be reparsed on next access.
        /// Call this if the Tags string is modified externally.
        /// </summary>
        public void InvalidateTagListCache()
        {
            _tagList = null;
        }

        private static List<string> ParseCommaSeparatedString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }
            // Allow comma or semicolon, trim whitespace, remove empty results
            return input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase) // Ensure unique tags
                        .ToList();
        }
    }

    public class ModDependency
    {
        public string PackageId { get; set; }
        public string DisplayName { get; set; }
        public string SteamWorkshopUrl { get; set; }

        // --- ADDED CALCULATED PROPERTY ---
        private string _steamId;
        public string SteamId
        {
            get
            {
                if (_steamId == null) // Calculate only once
                {
                    _steamId = ExtractSteamIdFromUrl(SteamWorkshopUrl) ?? string.Empty; // Store empty string if not found
                }
                return _steamId;
            }
        }

        private static readonly Regex SteamIdRegex = new Regex(@"id=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string ExtractSteamIdFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var match = SteamIdRegex.Match(url);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }

            return null; // Return null if no ID found
        }
    }
}