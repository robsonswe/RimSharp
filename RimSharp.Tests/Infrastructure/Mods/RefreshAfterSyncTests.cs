#nullable enable
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Infrastructure.Mods.Replacements;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Infrastructure.Mods.Versions;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods
{
    /// <summary>
    /// Post-sync cache refresh: each source manager's RefreshAfterSync() must
    /// invalidate only its own cache and must NOT persist the registry (no
    /// _registry.Save()). Re-application of the freshly synced data is handled
    /// centrally by the caller (IModService.ReapplySyncedDataAsync).
    /// </summary>
    public class RefreshAfterSyncTests
    {
        [Fact]
        public void ModInfoSourceManager_RefreshAfterSync_InvalidatesWithoutSaving()
        {
            var configService = Substitute.For<IConfigService>();
            var registry = new ModInfoSourceRegistry(configService);
            configService.ClearReceivedCalls();

            var dictionaryService = Substitute.For<IModDictionaryService>();
            var dataUpdateService = Substitute.For<IDataUpdateService>();
            var logger = Substitute.For<ILoggerService>();

            var manager = new ModInfoSourceManager(registry, dictionaryService, dataUpdateService, logger);

            manager.RefreshAfterSync();

            dictionaryService.Received(1).Invalidate();
            configService.DidNotReceive().SaveConfig();
        }

        [Fact]
        public void ModReplacementSourceManager_RefreshAfterSync_InvalidatesWithoutSaving()
        {
            var configService = Substitute.For<IConfigService>();
            var registry = new ModReplacementSourceRegistry(configService);
            configService.ClearReceivedCalls();

            var replacementService = Substitute.For<IModReplacementService>();
            var dataUpdateService = Substitute.For<IDataUpdateService>();
            var pathService = Substitute.For<IPathService>();
            var logger = Substitute.For<ILoggerService>();

            var manager = new ModReplacementSourceManager(
                registry, replacementService, dataUpdateService, pathService, logger);

            manager.RefreshAfterSync();

            replacementService.Received(1).Invalidate();
            configService.DidNotReceive().SaveConfig();
        }

        [Fact]
        public void RuleSourceManager_RefreshAfterSync_InvalidatesWithoutSavingOrReapplying()
        {
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            configService.ClearReceivedCalls();

            var repository = Substitute.For<IModRulesRepository>();
            var modService = Substitute.For<IModService>();
            var dataUpdateService = Substitute.For<IDataUpdateService>();
            var logger = Substitute.For<ILoggerService>();

            var manager = new RuleSourceManager(registry, repository, modService, dataUpdateService, logger);

            manager.RefreshAfterSync();

            repository.Received(1).Invalidate();
            configService.DidNotReceive().SaveConfig();
            // Re-application is centralized in the caller, not done here.
            modService.DidNotReceive().ReapplyRulesAsync();
        }

        [Fact]
        public void NoVersionWarningSourceManager_RefreshAfterSync_InvalidatesWithoutSavingOrReapplying()
        {
            var configService = Substitute.For<IConfigService>();
            var registry = new NoVersionWarningSourceRegistry(configService);
            configService.ClearReceivedCalls();

            var versionService = Substitute.For<IMlieVersionService>();
            var dataUpdateService = Substitute.For<IDataUpdateService>();
            var pathService = Substitute.For<IPathService>();
            var logger = Substitute.For<ILoggerService>();
            var modService = Substitute.For<IModService>();

            var manager = new NoVersionWarningSourceManager(
                registry, versionService, dataUpdateService, pathService, logger, modService);

            manager.RefreshAfterSync();

            versionService.Received(1).Invalidate();
            configService.DidNotReceive().SaveConfig();
            // Re-application is centralized in the caller, not done here.
            modService.DidNotReceive().ReapplyRulesAsync();
        }
    }
}
