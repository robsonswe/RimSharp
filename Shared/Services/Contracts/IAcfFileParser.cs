using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Parses Valve KeyValues ("ACF") files — specifically appworkshop_294100.acf manifest
    /// files that record locally installed Workshop item timestamps. The ACF lives at:
    ///   - Steam client: &lt;steamapps&gt;\workshop\appworkshop_294100.acf
    ///   - SteamCMD:      &lt;steamcmd-prefix&gt;\steam\steamapps\workshop\appworkshop_294100.acf
    /// </summary>
    public interface IAcfFileParser
    {
        /// <summary>
        /// Parses the workshop ACF manifest and extracts per-item timestamp records.
        /// </summary>
        /// <param name="acfFilePath">Full path to appworkshop_294100.acf.</param>
        /// <param name="data">Parsed data; Empty when parsing fails.</param>
        /// <param name="error">Human-readable failure reason; null on success.</param>
        /// <returns>True when the file was parsed into data.</returns>
        bool TryParse(string acfFilePath, out WorkshopAcfData data, out string? error);
    }
}
