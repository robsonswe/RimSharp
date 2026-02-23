// Core/Extensions/ThreadHelper.cs
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RimSharp.Core.Extensions
{
    public static class ThreadHelper
    {
        private static Dispatcher? _uiDispatcher;

        public static void Initialize()
        {
            _uiDispatcher = Dispatcher.UIThread;
        }

        public static Dispatcher? UiDispatcher => _uiDispatcher;

        public static bool IsUiThread => Dispatcher.UIThread.CheckAccess();

        public static void EnsureUiThread(Action action)
        {
            if (action == null) return;
            if (IsUiThread) action();
            else _uiDispatcher?.Post(action);
        }

        public static void BeginInvokeOnUiThread(Action action) => BeginInvokeOnUiThread(action, DispatcherPriority.Normal);

        public static void BeginInvokeOnUiThread(Action action, DispatcherPriority priority)
        {
            if (action == null) return;
            _uiDispatcher?.Post(action, priority);
        }

        public static Task RunOnUIThreadAsync(Action action) => RunOnUIThreadAsync(action, DispatcherPriority.Normal);

        public static async Task RunOnUIThreadAsync(Action action, DispatcherPriority priority)
        {
            if (action == null) return;
            if (IsUiThread)
            {
                try { action(); }
                catch { throw; }
                return;
            }
            if (_uiDispatcher != null) await _uiDispatcher.InvokeAsync(action, priority);
        }

        public static Task RunOnUIThreadAsync(Func<Task> asyncFunc) => RunOnUIThreadAsync(asyncFunc, DispatcherPriority.Normal);

        public static async Task RunOnUIThreadAsync(Func<Task> asyncFunc, DispatcherPriority priority)
        {
            if (asyncFunc == null) return;
            if (IsUiThread)
            {
                try { await asyncFunc(); }
                catch { throw; }
                return;
            }
            if (_uiDispatcher != null) await _uiDispatcher.InvokeAsync(asyncFunc, priority);
        }

        public static Task<T> RunOnUIThreadAsync<T>(Func<T> func) => RunOnUIThreadAsync(func, DispatcherPriority.Normal);

        public static async Task<T> RunOnUIThreadAsync<T>(Func<T> func, DispatcherPriority priority)
        {
             if (func == null) return default!;
             if (IsUiThread)
             {
                 try { return func(); }
                 catch { throw; }
             }
             if (_uiDispatcher != null) return await _uiDispatcher.InvokeAsync(func, priority);
             return default!;
        }

        public static void RunOnBackgroundThread(Action action) { if (action != null) Task.Run(action); }
        public static Task<T> RunOnBackgroundThreadAsync<T>(Func<T> func) => func == null ? Task.FromResult<T>(default!) : Task.Run(func);
        public static Task RunOnBackgroundThreadAsync(Func<Task> asyncFunc) => asyncFunc == null ? Task.CompletedTask : Task.Run(asyncFunc);
        public static Task<T> RunOnBackgroundThreadAsync<T>(Func<Task<T>> asyncFunc) => asyncFunc == null ? Task.FromResult<T>(default!) : Task.Run(asyncFunc);
    }
}
