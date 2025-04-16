using System;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    /// <summary>
    /// Implements the service for handling application-level navigation requests.
    /// </summary>
    public class ApplicationNavigationService : IApplicationNavigationService
    {
        public event EventHandler<string> TabSwitchRequested;

        public void RequestTabSwitch(string tabName)
        {
            // Raise the event to notify listeners (like MainViewModel)
            TabSwitchRequested?.Invoke(this, tabName);
            System.Diagnostics.Debug.WriteLine($"[ApplicationNavigationService] Tab switch requested for: {tabName}");
        }
    }
}
