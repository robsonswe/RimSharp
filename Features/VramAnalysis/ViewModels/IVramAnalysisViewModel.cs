using System.ComponentModel;

namespace RimSharp.Features.VramAnalysis.ViewModels
{
    public interface IVramAnalysisViewModel : INotifyPropertyChanged
    {
        bool IsBusy { get; }
    }
}
