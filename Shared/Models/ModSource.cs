using RimSharp.Infrastructure.Configuration;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Source of a mod — determines its update/delete behavior and UI presentation.
    /// </summary>
    public enum ModSource
    {
        /// <summary>Core game content (Data/About.xml)</summary>
        Core,
        /// <summary>Official DLC (Data/About.xml with Ludeon prefix)</summary>
        Dlc,
        /// <summary>Downloaded via SteamCMD (local but has PublishedFileId)</summary>
        SteamCmd,
        /// <summary>Downloaded via Steam Workshop (workshop folder)</summary>
        Workshop,
        /// <summary>Local mod (user's game/Mods folder)</summary>
        Local,
        /// <summary>Git repository (local folder with .git)</summary>
        GitHub,
        /// <summary>Unknown or not yet classified</summary>
        Unknown
    }
}