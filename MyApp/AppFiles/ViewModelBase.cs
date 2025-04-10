using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RimSharp.Core.Extensions;

namespace RimSharp.MyApp.AppFiles
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // Use ThreadHelper for UI thread safety
            ThreadHelper.EnsureUiThread(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // Helper method for dispatching actions to UI thread
        protected void RunOnUIThread(Action action)
        {
            ThreadHelper.EnsureUiThread(action);
        }

        protected async Task RunOnUIThreadAsync(Action action)
        {
            await Task.Run(() => RunOnUIThread(action));
        }

        // Helper for safely running background operations that update the UI when complete
        protected async Task RunAsync(Func<Task> asyncOperation)
        {
            try
            {
                await asyncOperation();
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"Error in async operation: {ex}");
                // Optionally re-throw or handle it depending on your error handling strategy
                throw;
            }
        }

        // Helper for running CPU-intensive operations on a background thread
        protected async Task<T> RunWithResultAsync<T>(Func<T> operation)
        {
            return await ThreadHelper.RunOnBackgroundThreadAsync(operation);
        }
    }
}