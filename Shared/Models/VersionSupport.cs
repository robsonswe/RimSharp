#nullable enable
using System;

namespace RimSharp.Shared.Models
{
    public class VersionSupport
    {
        public string Version { get; set; }
        public bool Unofficial { get; set; }
        public VersionSource Source { get; set; } // <<< ADDED

        // Modify constructor to accept source
        public VersionSupport(string version, VersionSource source, bool unofficial = false)
        {
            Version = version?.Trim() ?? throw new ArgumentNullException(nameof(version));
            Source = source;
            Unofficial = unofficial;
        }

        // Keep Equals and GetHashCode focused ONLY on the Version string for duplicate checking
        public override bool Equals(object? obj)
        {
            if (obj is not VersionSupport other)
                return false;

            // Compare only the version string, case-insensitively
            return string.Equals(Version, other.Version, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            // Hash code based only on the version string, case-insensitively
            return Version?.ToLowerInvariant().GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            // Optional: Add source/unofficial info here if needed for debugging displays
            // return $"{Version} (Source: {Source}, Unofficial: {Unofficial})";
            return Version;
        }
    }
}
