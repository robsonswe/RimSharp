using System.Windows;
using System.Windows.Input;

namespace RimSharp.Infrastructure.Dialog
{
    public partial class DialogTemplates : ResourceDictionary
    {
        public DialogTemplates()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var window = (sender as FrameworkElement)?.TemplatedParent as Window;
            window?.Close();
        }

        private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is FrameworkElement element && 
                element.TemplatedParent is Window window)
            {
                window.DragMove();
                e.Handled = true;
            }
        }
    }
}