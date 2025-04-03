using System;
using System.Windows.Threading; // Requires PresentationFramework

namespace RimSharp.Utility // Or a suitable namespace
{
    public static class DispatcherExtensions
    {
        /// <summary>
        /// Safely invokes an action on the Dispatcher's thread if required,
        /// otherwise executes it directly. Handles null dispatcher gracefully.
        /// </summary>
        /// <param name="dispatcher">The dispatcher instance (can be null).</param>
        /// <param name="action">The action to execute.</param>
        public static void SafeInvoke(this Dispatcher dispatcher, Action action)
        {
            if (action == null) return;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                // Use Invoke, not BeginInvoke, to match original ViewModelBase behavior
                // unless specific async behavior is desired elsewhere.
                dispatcher.Invoke(action);
            }
        }

        /// <summary>
        /// Safely begins invokes an action on the Dispatcher's thread if required,
        /// otherwise executes it directly. Handles null dispatcher gracefully.
        /// Use this for fire-and-forget UI updates.
        /// </summary>
        /// <param name="dispatcher">The dispatcher instance (can be null).</param>
        /// <param name="action">The action to execute.</param>
        public static void SafeBeginInvoke(this Dispatcher dispatcher, Action action)
        {
             if (action == null) return;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }
    }
}
