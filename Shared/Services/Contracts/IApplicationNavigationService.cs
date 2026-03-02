using System;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface IApplicationNavigationService
    {
        /// <summary>

/// </summary>
        event EventHandler<string> TabSwitchRequested;

        /// <summary>

        /// </summary>

        void RequestTabSwitch(string tabName);
    }
}

