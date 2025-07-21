#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;

namespace RimSharp.Features.VramAnalysis.ViewModels
{
    public class VramModItemWrapper : ViewModelBase
    {
        public ModItem Mod { get; }

        private long _estimatedVramUncompressed;
        public long EstimatedVramUncompressed
        {
            get => _estimatedVramUncompressed;
            set => SetProperty(ref _estimatedVramUncompressed, value);
        }

        private long _estimatedVramCompressed;
        public long EstimatedVramCompressed
        {
            get => _estimatedVramCompressed;
            set => SetProperty(ref _estimatedVramCompressed, value);
        }

        public VramModItemWrapper(ModItem mod)
        {
            Mod = mod;
        }
    }
}
