#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public enum AIIncompatibilityType { Soft, Hard }

    public class AIIncompatibilityCandidate
    {
        public ModItem SourceMod { get; set; } = new ModItem();
        public ModItem TargetMod { get; set; } = new ModItem();
        public AIIncompatibilityType Type { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool Confirmed { get; set; }
    }

    public interface IAIncompatibilityAgentService
    {
        /// <summary>True when the underlying provider has an API key configured.</summary>
        bool IsConfigured { get; }

        /// <summary>Diagnostic summary of the most recent run (keywords, suspects, verdicts).</summary>
        string LastRunSummary { get; }

        /// <summary>
        /// Analyzes ONE mod at a time: it generates keywords for the mod, does a
        /// search across ALL installed mods (via bytecode/text keyword search) to
        /// assemble a suspect list, and has the provider verify those suspects,
        /// returning candidates for the decision screen.
        /// </summary>
        Task<List<AIIncompatibilityCandidate>> AnalyzeSuspectsForModAsync(
            ModItem mod,
            List<ModItem> allMods,
            Action<string, double>? reportProgress,
            CancellationToken ct);
    }
}
