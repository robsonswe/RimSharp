using RimSharp.ViewModels;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace RimSharp.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Get the MainViewModel from the service provider
            DataContext = ((App)Application.Current).ServiceProvider.GetRequiredService<MainViewModel>();
        }
    }
}