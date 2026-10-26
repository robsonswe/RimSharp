using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;

namespace RimSharp.AppDir.Dialogs;
public class DownloadProgressItem : INotifyPropertyChanged
{
    private string _status = "Waiting";
    private int _progress;
    public string SteamId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressString)); } }
    public int Progress { get => _progress; set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressString)); } }
    public string ProgressString => $"{Progress}%";
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public class DownloadProgressWindowViewModel : INotifyPropertyChanged
{
    public ObservableCollection<DownloadProgressItem> Items { get; } = new();
    public ICommand CloseCommand { get; }
    public DownloadProgressWindowViewModel()
    {
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }
    public event EventHandler? CloseRequested;
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}