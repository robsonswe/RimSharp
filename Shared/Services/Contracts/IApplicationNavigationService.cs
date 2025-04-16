using System;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Defines a service for handling application-level navigation requests, like switching tabs.
    /// </summary>
    public interface IApplicationNavigationService
    {
        /// <summary>
        /// Event raised when a request to switch to a specific tab is made.
        /// The event argument is the name/key of the target tab.
        /// </summary>
        event EventHandler<string> TabSwitchRequested;

        /// <summary>
        /// Requests that the main application view switch to the specified tab.
        /// </summary>
        /// <param name="tabName">The name/key of the target tab (e.g., "Mods", "Downloader").</param>
        void RequestTabSwitch(string tabName);
    }
}
