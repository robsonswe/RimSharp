using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RimSharp.Core.Extensions
{
    public static class DispatcherExtensions
    {
        public static void SafeInvoke(this Dispatcher dispatcher, Action action)
        {
            if (action is null) return;

            if (dispatcher?.CheckAccess() != false)
                action();
            else
                dispatcher.Post(action);
        }

        public static async Task SafeInvokeAsync(this Dispatcher dispatcher, Func<Task> action)
        {
            if (action is null)
                return;

            if (dispatcher?.CheckAccess() != false)
            {
                await action();
                return;
            }

            if (dispatcher != null) await dispatcher.InvokeAsync(action);
        }

        public static void SafeBeginInvoke(this Dispatcher dispatcher, Action action)
        {
            if (action is null) return;

            if (dispatcher?.CheckAccess() != false)
                action();
            else
                dispatcher.Post(action);
        }

        public static async ValueTask<T> SafeInvokeAsync<T>(this Dispatcher dispatcher, Func<T> func)
        {
            if (func is null) return default(T)!;

            if (dispatcher?.CheckAccess() != false)
                return func();

            if (dispatcher != null) return await dispatcher.InvokeAsync(func);
            return default(T)!;
        }
    }
}
