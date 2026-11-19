using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Dialogs.RulesViewer;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.RulesViewer
{
    public class RulesViewerDialogViewModelTests
    {
        public RulesViewerDialogViewModelTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
        }

        private static RulesViewerDialogViewModel BuildVm(ModItem mod, IModDictionaryService? dictionary = null)
        {
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            return new RulesViewerDialogViewModel(mod, modService, registry, dictionary);
        }

        private static bool HasRow(RulesViewerDialogViewModel vm, string kind, string target) =>
            vm.Rows.Any(r => r.Kind == kind && r.Target == target);

        [Fact]
        public void Dependencies_ProduceRows_WithOfficialOrigin()
        {
            var mod = new ModItem
            {
                Name = "Dep Mod",
                PackageId = "dep.mod",
                ModDependencies =
                [
                    new() { PackageId = "dep.a", DisplayName = "Dependency A", SteamWorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=12345" },
                    new() { PackageId = "dep.b" } // no display name, no workshop url
                ]
            };

            var vm = BuildVm(mod);

            var rowA = vm.Rows.FirstOrDefault(r => r.Kind == "Dependency" && r.Target == "dep.a");
            rowA.Should().NotBeNull();
            rowA!.Name.Should().Be("Dependency A");
            rowA.Title.Should().Be("Dependency A");
            rowA.ShowTarget.Should().BeTrue();
            rowA.Comment.Should().BeNull();
            rowA.Origins.Should().Contain("Official");
            rowA.SourcesDisplay.Should().Be("Official");

            var rowB = vm.Rows.FirstOrDefault(r => r.Kind == "Dependency" && r.Target == "dep.b");
            rowB.Should().NotBeNull();
            rowB!.Name.Should().BeNull();
            rowB.Title.Should().Be("dep.b");
            rowB.ShowTarget.Should().BeFalse();
            rowB.Comment.Should().BeNull();
            rowB.Origins.Should().Contain("Official");
            rowB.SourcesDisplay.Should().Be("Official");
        }

        [Fact]
        public void ForceLoadBefore_ProduceRows_WithOfficialOrigin()
        {
            var mod = new ModItem
            {
                Name = "Force Before Mod",
                PackageId = "force.before.mod",
                ForceLoadBefore = ["force.a", "force.b"]
            };

            var vm = BuildVm(mod);

            HasRow(vm, "Force Load Before", "force.a").Should().BeTrue();
            HasRow(vm, "Force Load Before", "force.b").Should().BeTrue();

            var row = vm.Rows.First(r => r.Kind == "Force Load Before" && r.Target == "force.a");
            row.Name.Should().BeNull();
            row.Title.Should().Be("force.a");
            row.ShowTarget.Should().BeFalse();
            row.Comment.Should().BeNull();
            row.Origins.Should().Contain("Official");
            row.SourcesDisplay.Should().Be("Official");
        }

        [Fact]
        public void ForceLoadAfter_ProduceRows_WithOfficialOrigin()
        {
            var mod = new ModItem
            {
                Name = "Force After Mod",
                PackageId = "force.after.mod",
                ForceLoadAfter = ["force.x"]
            };

            var vm = BuildVm(mod);

            HasRow(vm, "Force Load After", "force.x").Should().BeTrue();

            var row = vm.Rows.First(r => r.Kind == "Force Load After" && r.Target == "force.x");
            row.Origins.Should().Contain("Official");
        }

        [Fact]
        public void ModWithNone_ProducesNoDependencyOrForceLoadRows()
        {
            var mod = new ModItem { Name = "Plain Mod", PackageId = "plain.mod" };

            var vm = BuildVm(mod);

            vm.Rows.Should().NotContain(r => r.Kind == "Dependency");
            vm.Rows.Should().NotContain(r => r.Kind == "Force Load Before");
            vm.Rows.Should().NotContain(r => r.Kind == "Force Load After");
        }

        [Fact]
        public void LoadBefore_ResolvesInstalledMod_ShowsDisplayName()
        {
            var target = new ModItem
            {
                Name = "Target Mod",
                PackageId = "target.mod",
                SteamName = "Target Mod",
                SteamUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=98765",
                SteamId = "98765"
            };
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem> { target });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem { Name = "Host Mod", PackageId = "host.mod", LoadBefore = ["target.mod"] },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "target.mod");
            row.Name.Should().Be("Target Mod");
            row.Title.Should().Be("Target Mod");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void LoadAfter_ResolvesInstalledMod_ShowsDisplayName()
        {
            var target = new ModItem
            {
                Name = "Target Mod",
                PackageId = "target.mod",
                SteamName = "Target Mod",
                SteamUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=98765",
                SteamId = "98765"
            };
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem> { target });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem { Name = "Host Mod", PackageId = "host.mod", LoadAfter = ["target.mod"] },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Load After" && r.Target == "target.mod");
            row.Name.Should().Be("Target Mod");
            row.Title.Should().Be("Target Mod");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void ForceLoadBefore_ResolvesInstalledMod_ShowsDisplayName()
        {
            var target = new ModItem
            {
                Name = "Target Mod",
                PackageId = "target.mod",
                SteamName = "Target Mod",
                SteamUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=98765",
                SteamId = "98765"
            };
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem> { target });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem { Name = "Host Mod", PackageId = "host.mod", ForceLoadBefore = ["target.mod"] },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Force Load Before" && r.Target == "target.mod");
            row.Name.Should().Be("Target Mod");
            row.Title.Should().Be("Target Mod");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void ForceLoadAfter_ResolvesInstalledMod_ShowsDisplayName()
        {
            var target = new ModItem
            {
                Name = "Target Mod",
                PackageId = "target.mod",
                SteamName = "Target Mod",
                SteamUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=98765",
                SteamId = "98765"
            };
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem> { target });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem { Name = "Host Mod", PackageId = "host.mod", ForceLoadAfter = ["target.mod"] },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Force Load After" && r.Target == "target.mod");
            row.Name.Should().Be("Target Mod");
            row.Title.Should().Be("Target Mod");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void Incompatibility_ResolvesInstalledMod_WhenRuleDeclaresNoNameOrComment()
        {
            var target = new ModItem
            {
                PackageId = "inc.mod",
                SteamName = "Incompatible Mod",
                SteamUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=55555",
                SteamId = "55555"
            };
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem> { target });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem
                {
                    Name = "Host Mod",
                    PackageId = "host.mod",
                    IncompatibleWith = { ["inc.mod"] = new ModIncompatibilityRule() }
                },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Incompatibility" && r.Target == "inc.mod");
            row.Name.Should().Be("Incompatible Mod");
            row.Title.Should().Be("Incompatible Mod");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().BeNull();
            row.Hard.Should().BeFalse();
        }

        [Fact]
        public void Incompatibility_KeepsRuleDeclaredName_AndComment()
        {
            var target = new ModItem
            {
                PackageId = "inc.mod",
                SteamName = "Resolver Name",
                SteamUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=11111",
                SteamId = "11111"
            };
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem> { target });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem
                {
                    Name = "Host Mod",
                    PackageId = "host.mod",
                    IncompatibleWith =
                    {
                        ["inc.mod"] = new ModIncompatibilityRule
                        {
                            Name = ["Rule Name"],
                            Comment = ["Rule comment"],
                            HardIncompatibility = true
                        }
                    }
                },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Incompatibility" && r.Target == "inc.mod");
            row.Name.Should().Be("Rule Name");
            row.Title.Should().Be("Rule Name");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().Be("Rule comment"); // the rule's declared reason is preserved
            row.Hard.Should().BeTrue();
        }

        [Fact]
        public void Dependency_FallsBackToResolver_WhenDeclaredFieldsEmpty()
        {
            var target = new ModItem
            {
                PackageId = "dep.mod",
                SteamName = "Dep Mod",
                SteamUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=22222",
                SteamId = "22222"
            };
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem> { target });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem
                {
                    Name = "Host Mod",
                    PackageId = "host.mod",
                    ModDependencies = { new() { PackageId = "dep.mod" } } // no DisplayName, no workshop URL
                },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Dependency" && r.Target == "dep.mod");
            row.Name.Should().Be("Dep Mod");
            row.Title.Should().Be("Dep Mod");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void NonInstalledTarget_ResolvedViaDictionary_ShowsName()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetEntryByPackageId("dict.mod").Returns(new ModDictionaryEntry
            {
                SteamId = "33333",
                SteamName = "Dictionary Mod",
                AboutName = "About Dictionary Mod"
            });

            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem>()); // not installed
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem { Name = "Host Mod", PackageId = "host.mod", LoadBefore = ["dict.mod"] },
                modService, registry, dictionary);

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "dict.mod");
            row.Name.Should().Be("Dictionary Mod");
            row.Title.Should().Be("Dictionary Mod");
            row.ShowTarget.Should().BeTrue();
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void UnresolvableTarget_LeavesNameAndCommentNull()
        {
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem>());
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var vm = new RulesViewerDialogViewModel(
                new ModItem { Name = "Host Mod", PackageId = "host.mod", LoadBefore = ["ghost.mod"] },
                modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "ghost.mod");
            row.Name.Should().BeNull();
            row.Title.Should().Be("ghost.mod"); // falls back to the packageId when nothing resolves
            row.ShowTarget.Should().BeFalse();
            row.Comment.Should().BeNull();
        }

        private static RulesViewerDialogViewModel BuildVmWithDictionary(
            ModItem mod,
            IModDictionaryService dictionary,
            string? currentGameVersion = null,
            ISteamApiClient? steamApi = null)
        {
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns((ModCustomInfo?)null);
            modService.GetLoadedMods().Returns(new List<ModItem>()); // not installed
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            return new RulesViewerDialogViewModel(mod, modService, registry, dictionary, currentGameVersion, steamApi);
        }

        private static ModDictionaryEntry Entry(string packageId, string steamId, string steamName, bool published, params string[] versions)
        {
            return new ModDictionaryEntry
            {
                PackageId = packageId,
                SteamId = steamId,
                SteamName = steamName,
                Published = published,
                Versions = new List<string>(versions)
            };
        }

        [Fact]
        public void Dictionary_PublishedBeatsUnpublishedEvenWithNewerVersion()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "99999", "Unpublished Fork", false, "1.8"),
                Entry("multi.mod", "11111", "Published Original", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7");

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Published Original");
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void Dictionary_AmongPublished_SupportingCurrentVersionWins()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "11111", "Does Not Support", true, "1.5"),
                Entry("multi.mod", "22222", "Supports 1.7", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7");

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Supports 1.7");
        }

        [Fact]
        public void Dictionary_TwoSupportingCurrentVersion_HighestMaxVersionWins()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "11111", "Lower Version", true, "1.7"),
                Entry("multi.mod", "22222", "Higher Version", true, "1.7", "1.8")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7");

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Higher Version");
        }

        [Fact]
        public void Dictionary_AllUnpublished_StillYieldsSingleChosenEntry()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "22222", "Unpublished B", false, "1.7"),
                Entry("multi.mod", "11111", "Unpublished A", false, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7");

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Unpublished A");
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void Dictionary_ExactTie_LowestNumericSteamIdWins()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "55555", "Higher Id", true, "1.7"),
                Entry("multi.mod", "11111", "Lower Id", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7");

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Lower Id");
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void Dictionary_NonNumericSteamId_SortsLast()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "abc", "Non Numeric", true, "1.7"),
                Entry("multi.mod", "11111", "Numeric", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7");

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Numeric");
            row.Comment.Should().BeNull();
        }

        [Fact]
        public void Dictionary_CurrentGameVersionNull_Step2SkippedButStep3MaxVersionStillApplied()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "22222", "Higher Max Version", true, "1.9"),
                Entry("multi.mod", "11111", "Lower Max Version", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, null);

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            // Step 2 (supports current version) is skipped, but step 3 (highest max version) still applies.
            // 1.9 > 1.7, so "Higher Max Version" wins despite not matching any current version.
            row.Name.Should().Be("Higher Max Version");
            row.Comment.Should().BeNull();
        }

        private static SteamApiResponse DetailResponse(string publishedFileId, string title, string creator, int result = 1)
        {
            return new SteamApiResponse
            {
                Response = new SteamApiResponseDetails
                {
                    Result = result,
                    PublishedFileDetails =
                    [
                        new SteamPublishedFileDetails
                        {
                            PublishedFileId = publishedFileId,
                            Result = result,
                            Title = title,
                            Creator = creator
                        }
                    ]
                }
            };
        }

        [Fact]
        public async Task Dictionary_LiveTitlePreferredOverDictionaryName()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "11111", "Dictionary Name", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var steamApi = Substitute.For<ISteamApiClient>();
            steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(DetailResponse("11111", "Live Workshop Title", "76561198000000001"));

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7", steamApi);

            await vm.InitializeAsync();

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Live Workshop Title");
            row.Comment.Should().BeNull();
        }

        [Fact]
        public async Task Dictionary_ApiFailureKeepsDictionaryName()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "11111", "Dictionary Name", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var steamApi = Substitute.For<ISteamApiClient>();
            steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns((SteamApiResponse?)null);

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7", steamApi);

            await vm.InitializeAsync();

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Dictionary Name");
        }

        [Fact]
        public async Task Dictionary_NonNumericSteamId_DoesNotCallSteamApi()
        {
            var dictionary = Substitute.For<IModDictionaryService>();
            dictionary.GetAllEntriesByPackageId("multi.mod").Returns(new List<ModDictionaryEntry>
            {
                Entry("multi.mod", "abc", "Non Numeric", true, "1.7")
            });
            dictionary.GetEntryByPackageId("multi.mod").Returns((ModDictionaryEntry?)null);

            var steamApi = Substitute.For<ISteamApiClient>();

            var vm = BuildVmWithDictionary(
                new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.mod"] },
                dictionary, "1.7", steamApi);

            await vm.InitializeAsync();

            _ = steamApi.DidNotReceive().GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.mod");
            row.Name.Should().Be("Non Numeric");
        }

        [Fact]
        public void SupportedVersions_CarriesVersionsList_AndMergedOrigins()
        {
            var supportedVersions = new List<VersionSupport>
            {
                new("1.7", VersionSource.Official),
                new("1.8", VersionSource.External, externalSourceName: "Some Source"),
                new("1.9", VersionSource.Custom, unofficial: true)
            };
            var mod = new ModItem
            {
                Name = "Versioned Mod",
                PackageId = "versioned.mod",
                SupportedVersions = supportedVersions
            };

            var vm = BuildVm(mod);

            var row = vm.Rows.First(r => r.Kind == "Supported Versions");
            row.Title.Should().Be("Supported Versions");
            row.Target.Should().Be("Supported Versions");
            row.ShowTarget.Should().BeFalse(); // no packageId line for this row
            row.Versions.Should().BeSameAs(supportedVersions);
            row.HasVersions.Should().BeTrue();
            row.SourcesDisplay.Should().Contain("Official");
            row.SourcesDisplay.Should().Contain("External (Some Source)");
            row.SourcesDisplay.Should().Contain("Custom");
        }

        [Fact]
        public void SourcesDisplay_JoinsAllOriginLabels()
        {
            var modService = Substitute.For<IModService>();
            modService.GetCustomModInfo(Arg.Any<string>()).Returns(new ModCustomInfo
            {
                LoadBefore = { ["multi.origin.mod"] = new ModDependencyRule() }
            });
            var configService = Substitute.For<IConfigService>();
            var registry = new ModRuleSourceRegistry(configService);
            var mod = new ModItem { Name = "Host", PackageId = "host.mod", LoadBefore = ["multi.origin.mod"] };
            mod.AboutLoadBefore.Add("multi.origin.mod");

            var vm = new RulesViewerDialogViewModel(mod, modService, registry);

            var row = vm.Rows.First(r => r.Kind == "Load Before" && r.Target == "multi.origin.mod");
            row.SourcesDisplay.Should().Be("Official, Custom");
        }
    }
}
