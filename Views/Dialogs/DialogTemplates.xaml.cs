using System.Windows;

namespace RimSharp.Views.Dialogs
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
    }
}
