using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Windows;
using System;
using RimSharp.Utility;

namespace RimSharp.ViewModels
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // Use SafeInvoke extension method for cleaner code
            Application.Current?.Dispatcher.SafeInvoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName); // OnPropertyChanged already handles threading
            return true;
        }

        // Helper method for dispatching actions to UI thread
        protected void RunOnUIThread(Action action)
        {
            // Use SafeInvoke extension method
            Application.Current?.Dispatcher.SafeInvoke(action);
        }
    }
}