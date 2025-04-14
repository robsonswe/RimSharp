using System;
using System.Collections.Generic;
using System.Linq;

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
        public List<string> IncompatibleWith { get; set; } = new List<string>();
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

        public IEnumerable<string> SupportedVersionStrings => SupportedVersions.Select(v => v.Version);

                // New: Efficiently parsed Author list
        private List<string> _authorList;
        public List<string> AuthorList => _authorList ??= ParseCommaSeparatedString(Authors);

        // New: Efficiently parsed Tag list
        private List<string> _tagList;
        public List<string> TagList => _tagList ??= ParseCommaSeparatedString(Tags);

        private static List<string> ParseCommaSeparatedString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }
            return input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) // Allow comma or semicolon
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
        }

    }

    public class ModDependency
    {
        public string PackageId { get; set; }
        public string DisplayName { get; set; }
        public string SteamWorkshopUrl { get; set; }
    }
}