using Avalonia.Controls;
using RimSharp.AppDir.Dialogs;

namespace RimSharp.AppDir.Dialogs;
public partial class DownloadProgressWindow : Window
{
    public DownloadProgressWindow() => InitializeComponent();
    public DownloadProgressWindow(DownloadProgressWindowViewModel vm) : this() => DataContext = vm;
}