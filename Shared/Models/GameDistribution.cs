using System.IO;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Represents the game distribution type.
    /// </summary>
    public enum GameDistribution
    {
        /// <summary>
        /// Steam edition (uses Steam Workshop and Steam client integration).
        /// </summary>
        Steam,

        /// <summary>
        /// Non-Steam edition (GOG, DRM-Free standalone, Heroic, or custom folder).
        /// </summary>
        NonSteam
    }

    /// <summary>
    /// Information about a detected RimWorld installation.
    /// </summary>
    public class GameInstallationCandidate
    {
        public GameDistribution Distribution { get; set; }
        public string GameFolder { get; set; } = string.Empty;
        public string ConfigFolder { get; set; } = string.Empty;
        public string? WorkshopFolder { get; set; }
        public bool HasSteamClientIntegration { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string DetectionSource { get; set; } = string.Empty;

        public bool IsValid => !string.IsNullOrEmpty(GameFolder) && 
                              Directory.Exists(GameFolder) &&
                              !string.IsNullOrEmpty(ConfigFolder) && 
                              Directory.Exists(ConfigFolder);
    }
}