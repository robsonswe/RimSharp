// Core/Services/ISystemInfoService.cs
#nullable enable

namespace RimSharp.Core.Services
{
    /// <summary>
    /// Defines a contract for a service that retrieves system hardware information.
    /// </summary>
    public interface ISystemInfoService
    {
        /// <summary>
        /// Gets the available video memory (VRAM) in bytes of the primary graphics card.
        /// </summary>
        /// <returns>The VRAM size in bytes, or 0 if it cannot be determined.</returns>
        long GetPrimaryGpuVram();
    }
}