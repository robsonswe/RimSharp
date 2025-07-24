// Features/VramAnalysis/ViewModels/VramModItemWrapper.cs
#nullable enable
using System.Collections.Generic;
using RimSharp.AppDir.AppFiles;
using RimSharp.Features.VramAnalysis.Tools; // Required for ConditionalDependency
using RimSharp.Shared.Models;

namespace RimSharp.Features.VramAnalysis.ViewModels
{
    public class VramModItemWrapper : ViewModelBase
    {
        public ModItem Mod { get; }

        private long _estimatedVramCompressed;
        public long EstimatedVramCompressed
        {
            get => _estimatedVramCompressed;
            set
            {
                if (SetProperty(ref _estimatedVramCompressed, value))
                {
                    OnPropertyChanged(nameof(HasConditionalContent));
                    OnPropertyChanged(nameof(VramDisplayText));
                }
            }
        }

        private long _maxEstimatedVramCompressed;
        public long MaxEstimatedVramCompressed
        {
            get => _maxEstimatedVramCompressed;
            set
            {
                if (SetProperty(ref _maxEstimatedVramCompressed, value))
                {
                    OnPropertyChanged(nameof(HasConditionalContent));
                    OnPropertyChanged(nameof(VramDisplayText));
                }
            }
        }

        private long _estimatedVramUncompressed;
        public long EstimatedVramUncompressed
        {
            get => _estimatedVramUncompressed;
            set => SetProperty(ref _estimatedVramUncompressed, value);
        }

        private long _maxEstimatedVramUncompressed;
        public long MaxEstimatedVramUncompressed
        {
            get => _maxEstimatedVramUncompressed;
            set => SetProperty(ref _maxEstimatedVramUncompressed, value);
        }
        // --- END MODIFICATION ---

        public bool HasConditionalContent => EstimatedVramCompressed != MaxEstimatedVramCompressed;
        public List<ConditionalDependency> ConditionalDependencies { get; set; } = new();

        public string VramDisplayText => FormatBytes(EstimatedVramCompressed);

        public VramModItemWrapper(ModItem mod) { Mod = mod; }

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "Not Calculated"; 

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < suffixes.Length - 1)
            {
                dblSByte /= 1024.0;
                i++;
            }
            return $"{dblSByte:F1} {suffixes[i]}";
        }
    }
}