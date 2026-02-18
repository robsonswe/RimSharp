using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Diagnostics;
using System;

namespace RimSharp.Infrastructure.Dialog
{
    public class BaseDialog : Window
    {
        static BaseDialog()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BaseDialog),
                new FrameworkPropertyMetadata(typeof(BaseDialog)));
        }

        public BaseDialog()
        {
            Style = (Style)Application.Current.Resources["RimworldDialogStyle"];
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Loaded += (s, e) =>
            {
                if (Owner == null && Application.Current != null && Application.Current.MainWindow != this)
                {
                    Owner = Application.Current.MainWindow;
                    Debug.WriteLine($"[BaseDialog] Owner automatically set to MainWindow ({Owner?.Title}) on Loaded.");
                }
            };
        }

        #region Dependency Properties
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

        // OnClosing override remains the same - this is correct
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (e.Cancel)
            {
                Debug.WriteLine($"[BaseDialog OnClosing] Close cancelled by another handler for {this.Title}.");
                return;
            }

            // WORKAROUND for WPF focus/minimization bug:
            // For modeless windows, we can detach the owner to prevent it from minimizing the main window.
            // For modal windows (ShowDialog), we cannot set Owner to null here (InvalidOperationException),
            // but we can ensure the owner doesn't lose its 'Normal' state.
            if (this.Owner != null)
            {
                // ComponentDispatcher.IsThreadModal is a way to check if we are in a ShowDialog() call
                bool isModal = System.Windows.Interop.ComponentDispatcher.IsThreadModal;
                
                if (!isModal)
                {
                    Debug.WriteLine($"[BaseDialog OnClosing] Modeless dialog {this.Title} detaching from Owner to prevent minimization bug.");
                    this.Owner = null;
                }
                else
                {
                    Debug.WriteLine($"[BaseDialog OnClosing] Modal dialog {this.Title} closing. WPF will handle focus.");
                }
            }

            Debug.WriteLine($"[BaseDialog OnClosing] Closing dialog {this.Title}.");
        }

        // CloseButton_Click and IsMouseInHeader remain the same
         private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private bool IsMouseInHeader(MouseEventArgs e)
        {
             return true;
        }
    }
}