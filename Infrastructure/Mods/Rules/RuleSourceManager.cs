#nullable enable
using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
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

        public RuleSourceManager(
            ModRuleSourceRegistry registry,
            IModRulesRepository repository,
            IModService modService,
            IDataUpdateService dataUpdateService,
            ILoggerService logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _dataUpdateService = dataUpdateService ?? throw new ArgumentNullException(nameof(dataUpdateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            _repository.Invalidate();
            _logger.LogInfo("Rule source registry changed; re-merging the rules database.", nameof(RuleSourceManager));
            _ = ReapplyAsync(nameof(CommitRegistryChanges));
        }

        private async Task ReapplyAsync(string caller)
        {
            try
            {
                await _modService.ReapplyRulesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Rule re-merge after {caller} failed.", nameof(RuleSourceManager));
            }
        }
    }
}