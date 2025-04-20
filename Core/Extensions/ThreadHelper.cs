// Core/Extensions/ThreadHelper.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading; // Required for Dispatcher

namespace RimSharp.Core.Extensions
{
    /// <summary>
    /// Helper class for thread-related operations and UI thread synchronization.
    /// </summary>
    public static class ThreadHelper
    {
        private static Dispatcher _uiDispatcher;

        /// <summary>
        /// Initializes the ThreadHelper with the UI thread's dispatcher.
        /// Should be called once on application startup (e.g., in App.xaml.cs OnStartup).
        /// </summary>
        public static void Initialize()
        {
            // Ensure this runs on the UI thread during initialization
            if (Application.Current != null)
            {
                 _uiDispatcher = Application.Current.Dispatcher;
            }
            else
            {
                 // Fallback or log error if Application.Current is null during init
                 // This might happen in unit tests or if called too early.
                 System.Diagnostics.Debug.WriteLine("[ThreadHelper] Warning: Application.Current was null during Initialize. UI thread operations might fail.");
                 // Attempt to get it from the current thread if it's STA
                 if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                 {
                     _uiDispatcher = Dispatcher.CurrentDispatcher;
                 }
            }
        }


        /// <summary>
        /// Gets the Dispatcher associated with the UI thread.
        /// </summary>
        public static Dispatcher UiDispatcher => _uiDispatcher ??
            throw new InvalidOperationException("ThreadHelper has not been initialized. Call ThreadHelper.Initialize() on startup.");


        /// <summary>
        /// Determines if the current thread is the UI thread by comparing dispatchers.
        /// </summary>
        public static bool IsUiThread => UiDispatcher.CheckAccess();


        /// <summary>
        /// Ensures that the specified action executes on the UI thread.
        /// If already on the UI thread, executes immediately; otherwise marshals synchronously to the UI thread.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        public static void EnsureUiThread(Action action)
        {
            if (action == null) return;

            if (IsUiThread)
            {
                action();
            }
            else
            {
                // Use Invoke for synchronous execution
                UiDispatcher.Invoke(action);
            }
        }


        /// <summary>
        /// Asynchronously executes an action on the UI thread using BeginInvoke (fire and forget).
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        /// <param name="priority">The priority at which to execute the action.</param>
        public static void BeginInvokeOnUiThread(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (action == null) return;

            if (IsUiThread)
            {
                action(); // Execute directly if already on UI thread
            }
            else
            {
                // Use BeginInvoke for asynchronous execution (fire and forget)
                UiDispatcher.BeginInvoke(priority, action);
            }
        }


        /// <summary>
        /// Asynchronously executes an action on the UI thread and returns a Task that completes when the action finishes.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        /// <param name="priority">The priority at which to execute the action.</param>
        /// <returns>A Task representing the asynchronous operation on the UI thread.</returns>
        public static Task RunOnUIThreadAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (action == null) return Task.CompletedTask;

            if (IsUiThread)
            {
                // Already on the UI thread, execute directly but wrap in a completed task
                // if the caller expects to await something (though execution is immediate).
                try
                {
                     action();
                     return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                     return Task.FromException(ex); // Propagate exception
                }
            }
            else
            {
                // Use InvokeAsync for awaitable asynchronous execution on the UI thread
                return UiDispatcher.InvokeAsync(action, priority).Task;
            }
        }


        /// <summary>
        /// Asynchronously executes a function on the UI thread and returns a Task<T> that completes with the function's result.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="func">The function to execute on the UI thread.</param>
        /// <param name="priority">The priority at which to execute the function.</param>
        /// <returns>A Task<T> representing the asynchronous operation on the UI thread.</returns>
        public static Task<T> RunOnUIThreadAsync<T>(Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal)
        {
             if (func == null) return Task.FromResult<T>(default);

             if (IsUiThread)
             {
                 // Already on the UI thread, execute directly and wrap result/exception
                 try
                 {
                      return Task.FromResult(func());
                 }
                 catch (Exception ex)
                 {
                     return Task.FromException<T>(ex);
                 }
             }
             else
             {
                 // Use InvokeAsync for awaitable asynchronous execution on the UI thread
                 return UiDispatcher.InvokeAsync(func, priority).Task;
             }
        }


        /// <summary>
        /// Executes a potentially long-running action on a background thread pool thread.
        /// Does nothing if already on a background thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        public static void RunOnBackgroundThread(Action action)
        {
            if (action == null) return;

            if (IsUiThread || !Thread.CurrentThread.IsThreadPoolThread) // Run if on UI or dedicated non-pool thread
            {
                // Use Task.Run to queue the action on the thread pool
                Task.Run(action);
            }
            else
            {
                 // Already on a background thread pool thread, consider executing directly
                 // Depending on desired behavior (e.g., prevent blocking current background task)
                 // For simplicity, we can still Task.Run it to ensure it doesn't block
                 // Task.Run(action);
                 // Or execute directly if acceptable:
                 action();
            }
        }


        /// <summary>
        /// Asynchronously executes a potentially long-running function on a background thread pool thread
        /// and returns its result.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="func">The function to execute.</param>
        /// <returns>A task representing the asynchronous operation, yielding the function's result.</returns>
        public static Task<T> RunOnBackgroundThreadAsync<T>(Func<T> func)
        {
            if (func == null)
                return Task.FromResult<T>(default);

             // Always use Task.Run to ensure execution on the thread pool
             // and return an awaitable Task<T>.
            return Task.Run(func);
        }

        /// <summary>
        /// Asynchronously executes a potentially long-running async function on a background thread pool thread.
        /// Useful for calling async methods that should not run on the UI thread.
        /// </summary>
        /// <param name="asyncFunc">The async function to execute.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static Task RunOnBackgroundThreadAsync(Func<Task> asyncFunc)
        {
             if (asyncFunc == null)
                 return Task.CompletedTask;

             // Task.Run can directly execute Func<Task>
             return Task.Run(asyncFunc);
        }


        /// <summary>
        /// Asynchronously executes a potentially long-running async function on a background thread pool thread
        /// and returns its result.
        /// </summary>
        /// <typeparam name="T">The return type of the async function.</typeparam>
        /// <param name="asyncFunc">The async function to execute.</param>
        /// <returns>A task representing the asynchronous operation, yielding the function's result.</returns>
        public static Task<T> RunOnBackgroundThreadAsync<T>(Func<Task<T>> asyncFunc)
        {
             if (asyncFunc == null)
                 return Task.FromResult<T>(default);

             // Task.Run can directly execute Func<Task<T>>
             return Task.Run(asyncFunc);
        }
    }
}