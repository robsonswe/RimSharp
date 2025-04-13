using System;
using System.Threading.Tasks;
using System.Windows.Threading;

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
                dispatcher.Invoke(action);
        }

        public static Task SafeInvokeAsync(this Dispatcher dispatcher, Func<Task> action)
        {
            if (action is null)
                return Task.CompletedTask;

            if (dispatcher?.CheckAccess() != false)
                return action();

            return dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        public static void SafeBeginInvoke(this Dispatcher dispatcher, Action action)
        {
            if (action is null) return;

            if (dispatcher?.CheckAccess() != false)
                action();
            else
                dispatcher.BeginInvoke(action);
        }

        public static ValueTask<T> SafeInvokeAsync<T>(this Dispatcher dispatcher, Func<T> func)
        {
            if (func is null) return ValueTask.FromResult(default(T));

            if (dispatcher?.CheckAccess() != false)
                return ValueTask.FromResult(func());

            return new ValueTask<T>(dispatcher.InvokeAsync(func).Task);
        }
    }
}
