using System;
using System.Windows.Input;
using RimSharp.Core.Commands;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class NavigationCommands
    {
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }
        public ICommand NavigateToUrlCommand { get; }

        public NavigationCommands(
            Action goBack, 
            Func<bool> canGoBack, 
            Action goForward, 
            Func<bool> canGoForward,
            Action goHome,
            Action<string> navigateToUrl)
        {
            GoBackCommand = new RelayCommand(
                _ => goBack(),
                _ => canGoBack());
                
            GoForwardCommand = new RelayCommand(
                _ => goForward(),
                _ => canGoForward());
                
            GoHomeCommand = new RelayCommand(
                _ => goHome());
                
            NavigateToUrlCommand = new RelayCommand(
                param => navigateToUrl(param as string),
                param => param is string url && !string.IsNullOrEmpty(url));
        }
    }
}