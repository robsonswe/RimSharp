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
                
                // Ensure handle is created
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
            
            // Set platform-specific content
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_container != null)
                {
                    _container.Content = new Border
                    {
                        Background = Avalonia.Application.Current?.FindResource("RimworldBeigeBrush") as Avalonia.Media.IBrush ?? Avalonia.Media.Brushes.Beige,
                        Padding = new Thickness(20),
                        Child = new StackPanel
                        {
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Spacing = 15,
                            Children =
                            {
                                new TextBlock { Text = "🌐", FontSize = 48, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                                new TextBlock { Text = "Web Browser", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold,
                                    Foreground = Avalonia.Application.Current?.FindResource("RimworldBrownBrush") as Avalonia.Media.IBrush ?? Avalonia.Media.Brushes.Brown,
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    TextAlignment = Avalonia.Media.TextAlignment.Center },
                                new TextBlock { Text = "The embedded web browser is only available on Windows.", FontSize = 14,
                                    Foreground = Avalonia.Application.Current?.FindResource("RimworldDarkBrownBrush") as Avalonia.Media.IBrush ?? new SolidColorBrush(Color.FromRgb(101, 67, 33)),
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                                    MaxWidth = 400 },
                                new TextBlock { Text = "Please use the Steam Workshop website directly or run on Windows to use the integrated browser.", FontSize = 12,
                                    Foreground = Avalonia.Application.Current?.FindResource("RimworldLightBrownBrush") as Avalonia.Media.IBrush ?? new SolidColorBrush(Color.FromRgb(139, 115, 85)),
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                                    MaxWidth = 400 }
                            }
                        }
                    };
                }
            }
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

                // Container should exist on Windows
                if (_container == null)
                {
                    Debug.WriteLine("[BrowserView] ERROR: BrowserContainer is null!");
                    return;
                }

                _webView = new WebView2
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    Visible = true,
                    Source = new Uri("about:blank")
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

                    // Navigate to default URL
                    string defaultUrl = "https://steamcommunity.com/app/294100/workshop/";
                    Debug.WriteLine($"[BrowserView] Navigating to: {defaultUrl}");
                    _webView.CoreWebView2.Navigate(defaultUrl);
                }
                else
                {
                    Debug.WriteLine("[BrowserView] ERROR: CoreWebView2 is null!");
                    return;
                }

                _wrappedControl = new WindowsBrowserControl(_webView);
                Debug.WriteLine("[BrowserView] Created WindowsBrowserControl wrapper");

                _container.Content = new WebView2Host(_webView);
                Debug.WriteLine("[BrowserView] Added WebView2Host to container");

                // Give the host time to create the native control
                await Task.Delay(100);

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
