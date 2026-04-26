// Features/WorkshopDownloader/Components/Browser/GenericBrowserFactory.cs
#nullable enable
using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class GenericBrowserFactory : IBrowserFactory
    {
        public bool IsSupported => false;

        public Task<(Control View, IBrowserControl Controller)> CreateBrowserAsync()
        {
            throw new PlatformNotSupportedException("The embedded web browser is not supported on this platform.");
        }
    }
}
