using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RimSharp.MyApp.AppFiles;

namespace RimSharp.MyApp.MainPage
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