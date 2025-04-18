using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Diagnostics;
using System; // For EventArgs
using System.Windows.Threading; // For Dispatcher
using RimSharp.AppDir.Dialogs; // Need this for DialogViewModelBase

namespace RimSharp.Infrastructure.Dialog
{
    public class BaseDialog : Window
    {
        private bool _isClosing = false; // Flag to prevent re-entrancy in handler

        static BaseDialog()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BaseDialog),
                new FrameworkPropertyMetadata(typeof(BaseDialog)));
        }

        public BaseDialog()
        {
            Style = (Style)Application.Current.Resources["RimworldDialogStyle"];
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Subscribe to DataContextChanged to manage event subscription
            this.DataContextChanged += BaseDialog_DataContextChanged;
        }

        #region Dependency Properties
        // ... (Dependency Properties remain the same) ...
        public static readonly DependencyProperty CloseableProperty =
                DependencyProperty.Register("Closeable", typeof(bool), typeof(BaseDialog), new PropertyMetadata(true));

        public bool Closeable
        {
            get => (bool)GetValue(CloseableProperty);
            set => SetValue(CloseableProperty, value);
        }

        public static readonly DependencyProperty HeaderContentProperty =
            DependencyProperty.Register("HeaderContent", typeof(object), typeof(BaseDialog));

        public object HeaderContent
        {
            get => GetValue(HeaderContentProperty);
            set => SetValue(HeaderContentProperty, value);
        }

        public static readonly DependencyProperty MainContentProperty =
            DependencyProperty.Register("MainContent", typeof(object), typeof(BaseDialog));

        public object MainContent
        {
            get => GetValue(MainContentProperty);
            set => SetValue(MainContentProperty, value);
        }

        public static readonly DependencyProperty ButtonContentProperty =
            DependencyProperty.Register("ButtonContent", typeof(object), typeof(BaseDialog));

        public object ButtonContent
        {
            get => GetValue(ButtonContentProperty);
            set => SetValue(ButtonContentProperty, value);
        }
        #endregion

        // OnClosing override remains the same (handles owner activation)
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) { /* ... */ return; }
            // Activate Owner logic...
            if (this.Owner != null && this.Owner.IsVisible && this.Owner.WindowState == WindowState.Normal)
            {
                try { this.Owner.Activate(); /* ... logging ... */ } catch (Exception ex) { /* ... logging ... */ }
            } else { /* ... logging ... */ }
        }

        // CloseButton_Click remains the same (triggers standard close)
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        // IsMouseInHeader remains the same
        private bool IsMouseInHeader(MouseEventArgs e) { return true; }


        // --- Centralized Closing Logic ---

        private void BaseDialog_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from the old ViewModel
            if (e.OldValue is DialogViewModelBase oldVm)
            {
                 Debug.WriteLine($"[BaseDialog DataContextChanged] Unsubscribing from RequestCloseDialog for {oldVm.GetType().Name} in {this.Title}");
                 oldVm.RequestCloseDialog -= Centralized_ViewModel_RequestCloseDialog;
            }

            // Subscribe to the new ViewModel
            if (e.NewValue is DialogViewModelBase newVm)
            {
                 Debug.WriteLine($"[BaseDialog DataContextChanged] Subscribing to RequestCloseDialog for {newVm.GetType().Name} in {this.Title}");
                 newVm.RequestCloseDialog += Centralized_ViewModel_RequestCloseDialog;
            }
        }

        /// <summary>
        /// Centralized handler for the ViewModel's RequestCloseDialog event.
        /// Sets the window's DialogResult and schedules the Close() call.
        /// </summary>
        private void Centralized_ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            // Prevent re-entrancy
            if (_isClosing)
            {
                Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Skipped: Already closing {this.Title}.");
                return;
            }

            // Ensure DataContext is still the expected ViewModel
            if (!(this.DataContext is DialogViewModelBase viewModel) || viewModel != sender)
            {
                 Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Skipped: DataContext mismatch or null for {this.Title}.");
                 return;
            }

            // Set flag immediately
             _isClosing = true;
             Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Initiated for {this.Title}.");

            try
            {
                // *** MODIFY THIS PART ***
                // Attempt to set DialogResult, but catch exception for non-modal windows
                try
                {
                    // Only attempt to set if the ViewModel provided a value
                    if (viewModel.DialogResultForWindow.HasValue)
                    {
                        this.DialogResult = viewModel.DialogResultForWindow;
                        Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Set Window.DialogResult to {this.DialogResult} for {this.Title}.");
                    }
                    else
                    {
                         Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Skipping Window.DialogResult set (ViewModel value is null) for {this.Title}.");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // This likely means the window was shown with .Show() (non-modal)
                    Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Info: Cannot set DialogResult (likely non-modal window '{this.Title}'). Message: {ex.Message}");
                    // Continue with closing anyway
                }
                // *** END MODIFICATION ***

                // Schedule Close via Dispatcher (Keep this)
                Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Scheduling Close() for {this.Title} via Dispatcher.");
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (this.IsLoaded)
                    {
                         Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Executing dispatched Close() for {this.Title}.");
                         try { Close(); } catch(Exception closeEx) { Debug.WriteLine($"[BaseDialog] EXCEPTION during dispatched Close(): {closeEx}"); }
                    }
                    else { Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Dispatched Close() skipped for {this.Title} as IsLoaded is false."); }
                }));
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"[BaseDialog Centralized_RequestClose] Exception during scheduling: {ex}");
                 _isClosing = false; // Reset flag if scheduling fails
            }
        }

        // Override OnClosed for final cleanup if necessary (like unsubscribing DataContextChanged)
        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from DataContext changes for this instance
            this.DataContextChanged -= BaseDialog_DataContextChanged;

             // Ensure handler is unsubscribed if DataContext still exists (belt and braces)
            if (this.DataContext is DialogViewModelBase vm)
            {
                 vm.RequestCloseDialog -= Centralized_ViewModel_RequestCloseDialog;
            }

            Debug.WriteLine($"[BaseDialog OnClosed] Final cleanup for {this.Title}.");
            base.OnClosed(e);
        }
    }
}
