#nullable enable
using System;

namespace RimSharp.Shared.Models
{
    public class VersionSupport
    {
        public string Version { get; set; }
        public bool Unofficial { get; set; }

        public VersionSupport(string version, bool unofficial = false)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Unofficial = unofficial;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not VersionSupport other)
                return false;

            return string.Equals(Version, other.Version, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return Version.ToLowerInvariant().GetHashCode();
        }

        public override string ToString()
        {
            return Version;
        }
    }
}