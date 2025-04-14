using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Add these methods to enable window dragging
        
        private bool IsMouseInHeader(MouseEventArgs e)
        {
            // This method will be used if we want to determine 
            // if the mouse is in the header area
            // You can use this if you only want certain parts to be draggable
            return true; // By default, allow dragging from anywhere
        }

        #endregion
    }
}