// Features/WorkshopDownloader/Components/Browser/BrowserView.axaml.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Projektanker.Icons.Avalonia;
using RimSharp.AppDir.AppFiles;
using Avalonia.Layout;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public partial class BrowserView : UserControl
    {
        private IBrowserControl? _wrappedControl;
        private ContentControl? _container;
        private bool _isInitialized;

        public BrowserView()
        {
            InitializeComponent();
            this.DataContextChanged += BrowserView_DataContextChanged;
            this.AttachedToVisualTree += BrowserView_AttachedToVisualTree;
            this.DetachedFromVisualTree += BrowserView_DetachedFromVisualTree;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _container = this.FindControl<ContentControl>("BrowserContainer");

            if (_container == null) return;

            var factory = (Application.Current as App)?.ServiceProvider?.GetService<IBrowserFactory>();
            bool isSupported = factory?.IsSupported ?? false;

            if (!isSupported)
            {
                _container.Content = CreatePlaceholderContent(
                    "Web Browser", 
                    "The embedded web browser is not available on this platform.", 
                    "Please use the Steam Workshop website directly to browse mods.", 
                    false);
            }
            else
            {
                _container.Content = CreatePlaceholderContent(
                    "Initializing Browser", 
                    "The Workshop browser is starting up...", 
                    "This may take a few moments on the first run.", 
                    true);
            }
        }

        private Control CreatePlaceholderContent(string title, string mainText, string subText, bool showProgress)
        {
            var beigeBrush = Application.Current?.FindResource("RimworldBeigeBrush") as IBrush ?? Brushes.Beige;
            var brownBrush = Application.Current?.FindResource("RimworldBrownBrush") as IBrush ?? Brushes.Brown;
            var darkBrownBrush = Application.Current?.FindResource("RimworldDarkBrownBrush") as IBrush ?? brownBrush;
            var lightBrownBrush = Application.Current?.FindResource("RimworldLightBrownBrush") as IBrush ?? brownBrush;

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 15,
                Children =
                {
                    new Icon { Value = "fa-globe", FontSize = 48, HorizontalAlignment = HorizontalAlignment.Center, Foreground = brownBrush },
                    new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.Bold,
                        Foreground = brownBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center },
                    new TextBlock { Text = mainText, FontSize = 14,
                        Foreground = darkBrownBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 400 },
                    new TextBlock { Text = subText, FontSize = 12,
                        Foreground = lightBrownBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 400 }
                }
            };

            if (showProgress)
            {
                var progress = new ProgressBar
                {
                    IsIndeterminate = true,
                    Width = 200,
                    Height = 4,
                    Margin = new Thickness(0, 10, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                progress.Classes.Add("RimworldProgressBarStyle");
                stack.Children.Add(progress);
            }

            return new Border
            {
                Background = beigeBrush,
                Padding = new Thickness(20),
                Child = stack
            };
        }

        private void BrowserView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Debug.WriteLine("[BrowserView] AttachedToVisualTree fired");
            if (_wrappedControl != null && DataContext is BrowserViewModel viewModel)
            {
                Debug.WriteLine("[BrowserView] Re-attaching browser control to ViewModel");
                viewModel.SetBrowserControl(_wrappedControl);
            }
        }

        private void BrowserView_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Debug.WriteLine("[BrowserView] DetachedFromVisualTree fired");
            if (DataContext is BrowserViewModel viewModel)
            {
                viewModel.SetBrowserControl(null);
            }
        }

        private async void BrowserView_DataContextChanged(object? sender, EventArgs e)
        {
            if (_isInitialized) return;

            var factory = (Application.Current as App)?.ServiceProvider?.GetService<IBrowserFactory>();
            if (factory == null || !factory.IsSupported) return;

            if (DataContext is BrowserViewModel viewModel)
            {
                Debug.WriteLine("[BrowserView] DataContext is BrowserViewModel, starting initialization...");
                _isInitialized = true;
                await InitializeBrowserAsync(viewModel, factory);
            }
        }

        private async Task InitializeBrowserAsync(BrowserViewModel viewModel, IBrowserFactory factory)
        {
            try
            {
                Debug.WriteLine("[BrowserView] Starting browser initialization via factory...");

                if (_container == null)
                {
                    Debug.WriteLine("[BrowserView] ERROR: BrowserContainer is null!");
                    return;
                }

                var (view, controller) = await factory.CreateBrowserAsync();
                _wrappedControl = controller;

                Debug.WriteLine("[BrowserView] Created browser control wrapper");
                
                var navigationStartedTcs = new TaskCompletionSource<bool>();
                void OnFirstNavigation(object? s, NavigationStartingEventArgs e)
                {
                    if (_wrappedControl != null)
                        _wrappedControl.NavigationStarting -= OnFirstNavigation;
                    navigationStartedTcs.TrySetResult(true);
                }
                _wrappedControl.NavigationStarting += OnFirstNavigation;

                await Task.WhenAny(navigationStartedTcs.Task, Task.Delay(5000));

                if (_container != null)
                {
                    _container.Content = view;
                    Debug.WriteLine("[BrowserView] Swapped placeholder for browser view");
                }

                if (this.IsAttachedToVisualTree())
                {
                    viewModel.SetBrowserControl(_wrappedControl);
                    Debug.WriteLine("[BrowserView] Set browser control in ViewModel");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserView] Initialization failed: {ex}");
                if (_container != null)
                {
                    _container.Content = CreatePlaceholderContent(
                        "Initialization Failed", 
                        "The Workshop browser failed to start.", 
                        ex.Message, 
                        false);
                }
            }
        }

        private void SearchTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter && DataContext is BrowserViewModel viewModel)
            {
                if (viewModel.SearchCommand.CanExecute(null))
                {
                    viewModel.SearchCommand.Execute(null);
                    e.Handled = true;
                    ClearAllFocus();
                }
            }
        }

        private void AddressTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter && DataContext is BrowserViewModel viewModel)
            {
                if (viewModel.NavigateToUrlCommand.CanExecute(viewModel.AddressBarUrl))
                {
                    viewModel.NavigateToUrlCommand.Execute(viewModel.AddressBarUrl);
                    e.Handled = true;
                    ClearAllFocus();
                }
            }
        }

        private void Root_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            ClearAllFocus();
        }

        private void SearchButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ClearAllFocus();
        }

        private void ClearAllFocus()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            topLevel?.FocusManager?.ClearFocus();
        }
    }
}
