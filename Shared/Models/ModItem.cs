using System.Collections.Generic;

namespace RimSharp.Shared.Models
{
    public class ModItem
    {
        // Required fields from About.xml
        public string Name { get; set; }
        public string PackageId { get; set; }
        public string Authors { get; set; }
        public string Description { get; set; }
        public List<string> SupportedVersions { get; set; } = new List<string>();

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
        public bool IsCore { get; set; }
        public bool IsExpansion { get; set; }
        public bool IsActive { get; set; }
        public string PreviewImagePath { get; set; }

        public bool IsOutdatedRW { get; set; }

        public bool HasUrl => !string.IsNullOrEmpty(Url);
        public bool HasSteamUrl => !string.IsNullOrEmpty(SteamUrl);
        public bool HasExternalUrl => !string.IsNullOrEmpty(ExternalUrl);

    }

    public class ModDependency
    {
        public string PackageId { get; set; }
        public string DisplayName { get; set; }
        public string SteamWorkshopUrl { get; set; }
    }
}