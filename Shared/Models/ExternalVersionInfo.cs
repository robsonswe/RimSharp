#nullable enable
namespace RimSharp.Shared.Models
{
    /// <summary>
    /// One compatibility version contributed by a community external source,
    /// together with the display name of the source that declared it.
    /// </summary>
    /// <param name="Version">The supported game version string (e.g. "1.5").</param>
    /// <param name="SourceName">
    /// Display name of the contributing source. Null/empty when no name is
    /// available (the UI then falls back to a generic label).
    /// </param>
    public sealed record ExternalVersionInfo(string Version, string? SourceName);
}
