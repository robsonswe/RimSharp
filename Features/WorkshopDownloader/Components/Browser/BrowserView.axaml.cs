using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using Projektanker.Icons.Avalonia;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class WebView2Host : NativeControlHost
    {
        private readonly WebView2 _webView;
        private IPlatformHandle? _currentHandle;

        public WebView2Host(WebView2 webView)
        {
            _webView = webView;
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_currentHandle != null)
                {
                    return _currentHandle;
                }
                if (_webView.Handle == IntPtr.Zero)
                {
                    _webView.CreateControl();
                }

                _currentHandle = new PlatformHandle(_webView.Handle, "HWND");
                return _currentHandle;
            }
            return base.CreateNativeControlCore(parent);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            _currentHandle = null;
            // Don't call base - avoids InvalidCastException
        }
    }

    public partial class BrowserView : UserControl
    {
        private WebView2? _webView;
        private WindowsBrowserControl? _wrappedControl;
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

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _container.Content = CreatePlaceholderContent(
                    "Web Browser", 
                    "The embedded web browser is only available on Windows.", 
                    "Please use the Steam Workshop website directly or run on Windows to use the integrated browser.", 
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
            Debug.WriteLine($"[BrowserView] DataContextChanged fired. IsInitialized={_isInitialized}, IsWindows={RuntimeInformation.IsOSPlatform(OSPlatform.Windows)}");
            if (_isInitialized || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            if (DataContext is BrowserViewModel viewModel)
            {
                Debug.WriteLine("[BrowserView] DataContext is BrowserViewModel, starting initialization...");
                _isInitialized = true;
                await InitializeWebViewAsync(viewModel);
            }
        }

        private async Task InitializeWebViewAsync(BrowserViewModel viewModel)
        {
            try
            {
                Debug.WriteLine("[BrowserView] Starting WebView2 initialization...");

if (_container == null)
                {
                    Debug.WriteLine("[BrowserView] ERROR: BrowserContainer is null!");
                    return;
                }

                string defaultUrl = "https://steamcommunity.com/app/294100/workshop/";

                _webView = new WebView2
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    Visible = true,
                    Source = new Uri(defaultUrl)
                };

                Debug.WriteLine("[BrowserView] Calling EnsureCoreWebView2Async...");
                await _webView.EnsureCoreWebView2Async();
                Debug.WriteLine("[BrowserView] EnsureCoreWebView2Async completed.");

                if (_webView.CoreWebView2 != null)
                {
                    Debug.WriteLine("[BrowserView] Configuring CoreWebView2 settings...");
                    _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                }
                else
                {
                    Debug.WriteLine("[BrowserView] ERROR: CoreWebView2 is null!");
                    return;
                }

                _wrappedControl = new WindowsBrowserControl(_webView);
                Debug.WriteLine("[BrowserView] Created WindowsBrowserControl wrapper");
                if (_container != null)
                {
                    _container.Content = CreatePlaceholderContent(
                        "Loading Workshop", 
                        "Connecting to Steam Workshop...", 
                        "The page will appear shortly.", 
                        true);
                }
                var navigationStartedTcs = new TaskCompletionSource<bool>();
                void OnFirstNavigation(object? s, string url)
                {
                    _wrappedControl.NavigationStarting -= OnFirstNavigation;
                    navigationStartedTcs.TrySetResult(true);
                }
                _wrappedControl.NavigationStarting += OnFirstNavigation;

                // Create the host but don't show it yet
                var host = new WebView2Host(_webView);

                await Task.WhenAny(navigationStartedTcs.Task, Task.Delay(5000));

                if (_container != null)
                {
                    _container.Content = host;
                    Debug.WriteLine("[BrowserView] Swapped placeholder for WebView2Host");
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


