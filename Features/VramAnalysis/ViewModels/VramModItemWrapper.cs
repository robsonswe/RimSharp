// Features/VramAnalysis/ViewModels/VramModItemWrapper.cs
#nullable enable
using System.Collections.Generic;
using RimSharp.AppDir.AppFiles;
using RimSharp.Features.VramAnalysis.Tools;
using RimSharp.Shared.Models;

namespace RimSharp.Features.VramAnalysis.ViewModels
{
    public class VramModItemWrapper : ViewModelBase
    {
        public ModItem Mod { get; }
        public long EstimatedVramCompressed { get => _estimatedVramCompressed; set { if (SetProperty(ref _estimatedVramCompressed, value)) { OnPropertyChanged(nameof(HasConditionalContent)); } } }
        private long _estimatedVramCompressed;
        public long MaxEstimatedVramCompressed { get => _maxEstimatedVramCompressed; set { if (SetProperty(ref _maxEstimatedVramCompressed, value)) { OnPropertyChanged(nameof(HasConditionalContent)); } } }
        private long _maxEstimatedVramCompressed;
        public long EstimatedVramUncompressed { get => _estimatedVramUncompressed; set => SetProperty(ref _estimatedVramUncompressed, value); }
        private long _estimatedVramUncompressed;
        public long MaxEstimatedVramUncompressed { get => _maxEstimatedVramUncompressed; set => SetProperty(ref _maxEstimatedVramUncompressed, value); }
        private long _maxEstimatedVramUncompressed;
        public int TextureCount { get => _textureCount; set => SetProperty(ref _textureCount, value); }
        private int _textureCount;
        public int MaxTextureCount { get => _maxTextureCount; set => SetProperty(ref _maxTextureCount, value); }
        private int _maxTextureCount;
        
        // --- NEW: Atlas Count Properties ---
        public int InAtlasCount { get => _inAtlasCount; set => SetProperty(ref _inAtlasCount, value); }
        private int _inAtlasCount;
        public int MaxInAtlasCount { get => _maxInAtlasCount; set => SetProperty(ref _maxInAtlasCount, value); }
        private int _maxInAtlasCount;

        public bool HasConditionalContent => EstimatedVramCompressed != MaxEstimatedVramCompressed;
        public List<ConditionalDependency> ConditionalDependencies { get; set; } = new();

        public VramModItemWrapper(ModItem mod) { Mod = mod; }
    }
}