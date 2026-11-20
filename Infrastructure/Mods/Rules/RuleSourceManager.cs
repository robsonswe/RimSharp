#nullable enable
using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Core.Extensions;
using RimSharp.Infrastructure.Data;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Rules
{
    /// <summary>
    /// Default <see cref="IRuleSourceManager"/>: owns the registry commit path
    /// (persist -> invalidate the merged rules cache -> re-apply onto the
    /// loaded mods). Remote sources sync automatically at startup.
    /// </summary>
    public class RuleSourceManager : IRuleSourceManager
    {
        private readonly ModRuleSourceRegistry _registry;
        private readonly IModRulesRepository _repository;
        private readonly IModService _modService;
        private readonly IDataUpdateService _dataUpdateService;
        private readonly ILoggerService _logger;
        private readonly IModListManager? _modListManager;

        public RuleSourceManager(
            ModRuleSourceRegistry registry,
            IModRulesRepository repository,
            IModService modService,
            IDataUpdateService dataUpdateService,
            ILoggerService logger,
            IModListManager? modListManager = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modListManager = modListManager;
        }

        public ModRuleSourceRegistry Registry => _registry;

        public long? EffectiveTimestamp => _repository.EffectiveTimestamp;

        public IReadOnlyDictionary<string, long> SourceTimestamps => _repository.SourceTimestamps;

        /// <summary>
        /// Persists the registry and re-merges. The re-application runs on the
        /// thread pool (fire-and-forget from the synchronous settings Save) so
        /// the UI thread is never blocked by the re-merge.
        /// </summary>
        public void CommitRegistryChanges()
        {
            _registry.Save();
            InvalidateCaches();
            _logger.LogInfo("Rule source registry changed; re-merging the rules database.", nameof(RuleSourceManager));
            _ = ReapplyAsync(nameof(CommitRegistryChanges));
        }

        public void RefreshAfterSync()
        {
            InvalidateCaches();
            _logger.LogInfo("Rule data synced; invalidated rules cache.", nameof(RuleSourceManager));
        }

        private void InvalidateCaches()
        {
            _repository.Invalidate();
        }

        private async Task ReapplyAsync(string caller)
        {
            try
            {
                await _modService.ReapplyRulesAsync().ConfigureAwait(false);

                // ReapplyRulesAsync marshals its ModItem mutations onto the UI
                // thread and awaits them. Now refresh the issue badges on that
                // same UI thread so HasIssues/tooltips/counts are not stale
                // after a rule source was added/removed/reordered.
                if (_modListManager != null)
                {
                    await ThreadHelper.RunOnUIThreadAsync(
                        () => _modListManager.RefreshActiveModIssues()).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Rule re-merge after {caller} failed.", nameof(RuleSourceManager));
            }
        }
    }
}