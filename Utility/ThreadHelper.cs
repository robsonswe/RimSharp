using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RimSharp.Utility
{
    /// <summary>
    /// Helper class for thread-related operations and UI thread synchronization.
    /// </summary>
    public static class ThreadHelper
    {
        /// <summary>
        /// Determines if the current thread is the UI thread.
        /// </summary>
        public static bool IsUiThread => 
            Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;

        /// <summary>
        /// Ensures that the specified action executes on the UI thread.
        /// If already on the UI thread, executes immediately; otherwise marshals to the UI thread.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        public static void EnsureUiThread(Action action)
        {
            if (action == null) return;

            if (IsUiThread || Application.Current == null)
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action);
            }
        }

        /// <summary>
        /// Asynchronously executes an action on the UI thread.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        public static void BeginInvokeOnUiThread(Action action)
        {
            if (action == null) return;
            
            if (IsUiThread || Application.Current == null)
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
            }
        }

        /// <summary>
        /// Executes a CPU-bound action on a background thread if not already on a background thread.
        /// </summary>
        /// <param name="action">The CPU-bound action to execute.</param>
        public static void RunOnBackgroundThread(Action action)
        {
            if (action == null) return;

            if (IsUiThread)
            {
                // We're on the UI thread, so use Task.Run to move to a background thread
                Task.Run(action);
            }
            else
            {
                // Already on a background thread, execute directly
                action();
            }
        }

        /// <summary>
        /// Asynchronously executes a CPU-bound function on a background thread.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="func">The CPU-bound function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static Task<T> RunOnBackgroundThreadAsync<T>(Func<T> func)
        {
            if (func == null) 
                return Task.FromResult<T>(default);

            if (IsUiThread)
            {
                // We're on the UI thread, so use Task.Run to move to a background thread
                return Task.Run(func);
            }
            else
            {
                // Already on a background thread, execute directly but wrap in Task
                try
                {
                    T result = func();
                    return Task.FromResult(result);
                }
                catch (Exception ex)
                {
                    // Preserve the exception in the task
                    var tcs = new TaskCompletionSource<T>();
                    tcs.SetException(ex);
                    return tcs.Task;
                }
            }
        }
    }
}