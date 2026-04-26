// Features/WorkshopDownloader/Components/Browser/IBrowserFactory.cs
#nullable enable
using System.Threading.Tasks;
using Avalonia.Controls;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public interface IBrowserFactory
    {
        bool IsSupported { get; }
        Task<(Control View, IBrowserControl Controller)> CreateBrowserAsync();
    }
}
