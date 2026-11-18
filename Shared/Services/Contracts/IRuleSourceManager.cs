using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Mods.Rules;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Coordinates the rule database source registry: persists registry edits,
    /// triggers source refreshes and performs the live re-merge (cache
    /// invalidation + re-application onto the loaded mods) so registry changes
    /// take effect without an app restart.
    /// </summary>
    public interface IRuleSourceManager : IExternalSourceManager
    {
        /// <summary>The shared, priority-ordered source registry.</summary>
        ModRuleSourceRegistry Registry { get; }
    }
}