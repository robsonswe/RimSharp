using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.ViewModels
{
    public interface IModsViewModel : INotifyPropertyChanged
    {
        bool IsLoading { get; }
        bool IsViewActive { get; set; }
        ICommand RequestRefreshCommand { get; }
        Task InitializeAsync(IProgress<(int current, int total, string message)> progress = null);
    }
}
