using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RimSharp.AppDir.AppFiles;
using AppClass = RimSharp.AppDir.AppFiles.App;

namespace RimSharp.AppDir.MainPage
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Get the MainViewModel from the service provider
            DataContext = ((AppClass)Application.Current).ServiceProvider.GetRequiredService<MainViewModel>();
        }
    }
}