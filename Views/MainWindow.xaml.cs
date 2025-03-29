using RimSharp.ViewModels;
using System.Windows;
using RimSharp.Services;
using RimSharp.Models;

namespace RimSharp.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            // This constructor shouldn't be used, but prevents XAML designer errors
            InitializeComponent();
            DataContext = new MainViewModel(
                new ModService(new PathService(new PathSettings())),
                new PathService(new PathSettings())
            );
        }
    }
}