using System;

namespace RimSharp.Shared.Models
{
    public enum ModIssueType
    {
        Sorting,
        Dependency,
        Incompatibility,
        VersionMismatch,
        Duplicate
    }

    public class ModIssue
    {
        public ModItem Mod { get; set; }
        public ModIssueType Type { get; set; }
        public string Description { get; set; }

        public ModIssue(ModItem mod, ModIssueType type, string description)
        {
            Mod = mod ?? throw new ArgumentNullException(nameof(mod));
            Type = type;
            Description = description ?? string.Empty;
        }
    }
}
