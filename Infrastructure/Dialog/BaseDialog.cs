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

            if (this.Owner != null && this.Owner.IsVisible && this.Owner.WindowState == WindowState.Normal)
            {
                try
                {
                    Debug.WriteLine($"[BaseDialog OnClosing] Activating Owner ({this.Owner.Title}) before closing {this.Title}.");
                    this.Owner.Activate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BaseDialog OnClosing] Error activating owner for {this.Title}: {ex.Message}");
                }
            }
            else
            {
                 // This will now be the standard path for all dialogs.
                 Debug.WriteLine($"[BaseDialog OnClosing] Skipping owner activation for {this.Title}. Owner is null.");
            }
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