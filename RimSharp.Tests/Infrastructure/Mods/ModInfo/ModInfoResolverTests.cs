using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Infrastructure.Mods.ModInfo;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.ModInfo
{
    public class ModInfoResolverTests
    {
        private readonly IModDictionaryService _dictionary = Substitute.For<IModDictionaryService>();
        private readonly ISteamApiClient _steamApi = Substitute.For<ISteamApiClient>();
        private readonly ModInfoResolver _resolver;

        public ModInfoResolverTests()
        {
            _resolver = new ModInfoResolver(_dictionary, _steamApi);
        }

        private static SteamApiResponse ApiResponse(params SteamPublishedFileDetails[] details) => new()
        {
            Response = new SteamApiResponseDetails
            {
                Result = 1,
                ResultCount = details.Length,
                PublishedFileDetails = details.ToList()
            }
        };

        [Fact]
        public async Task ResolveAsync_SingleCandidate_IsNamedFromApi()
        {
            var entry = new ModDictionaryEntry
            {
                PackageId = "test.mod",
                SteamId = "111",
                SteamName = "Dictionary Name",
                Published = true
            };
            _dictionary.GetAllEntriesByPackageId("test.mod").Returns(new List<ModDictionaryEntry> { entry });
            _dictionary.GetEntryByPackageId("test.mod").Returns(entry);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails { PublishedFileId = "111", Title = "Live API Name", Result = 1 }));

            var result = await _resolver.ResolveAsync(new[] { "test.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            var candidates = result["test.mod"];
            candidates.Should().ContainSingle();
            candidates[0].SteamId.Should().Be("111");
            candidates[0].DisplayName.Should().Be("Live API Name");
            candidates[0].Published.Should().BeTrue();
            candidates[0].ApiDetails.Should().NotBeNull();
            candidates[0].DictionaryEntry.Should().BeSameAs(entry);
        }

        [Fact]
        public async Task ResolveAsync_BatchOfPfids_MakesOneApiCallWithAllIds()
        {
            var entries = new[]
            {
                new ModDictionaryEntry { PackageId = "a.mod", SteamId = "111", SteamName = "A" },
                new ModDictionaryEntry { PackageId = "b.mod", SteamId = "222", SteamName = "B" },
                new ModDictionaryEntry { PackageId = "c.mod", SteamId = "333", SteamName = "C" }
            };
            foreach (var entry in entries)
            {
                _dictionary.GetAllEntriesByPackageId(entry.PackageId).Returns(new List<ModDictionaryEntry> { entry });
                _dictionary.GetEntryByPackageId(entry.PackageId).Returns(entry);
            }
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(
                    new SteamPublishedFileDetails { PublishedFileId = "111", Title = "A", Result = 1 },
                    new SteamPublishedFileDetails { PublishedFileId = "222", Title = "B", Result = 1 },
                    new SteamPublishedFileDetails { PublishedFileId = "333", Title = "C", Result = 1 }));

            await _resolver.ResolveAsync(new[] { "a.mod", "b.mod", "c.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            await _steamApi.Received(1).GetPublishedFileDetailsBatchAsync(
                Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "111", "222", "333" })),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ResolveAsync_MultipleCandidates_PutsPrioritizedEntryFirst()
        {
            var low = new ModDictionaryEntry { PackageId = "shared.pkg", SteamId = "111", SteamName = "Old" };
            var high = new ModDictionaryEntry { PackageId = "shared.pkg", SteamId = "222", SteamName = "New" };
            _dictionary.GetAllEntriesByPackageId("shared.pkg").Returns(new List<ModDictionaryEntry> { low, high });
            // GetEntryByPackageId carries the version → published → source policy.
            _dictionary.GetEntryByPackageId("shared.pkg").Returns(high);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(
                    new SteamPublishedFileDetails { PublishedFileId = "111", Title = "Old", Result = 1 },
                    new SteamPublishedFileDetails { PublishedFileId = "222", Title = "New", Result = 1 }));

            var result = await _resolver.ResolveAsync(new[] { "shared.pkg" }, cancellationToken: TestContext.Current.CancellationToken);

            var candidates = result["shared.pkg"];
            candidates.Should().HaveCount(2);
            candidates[0].SteamId.Should().Be("222");
            candidates[1].SteamId.Should().Be("111");
        }

        [Fact]
        public async Task ResolveAsync_ZeroCandidates_ReturnsEmptyListAndSkipsApi()
        {
            _dictionary.GetAllEntriesByPackageId("unknown.mod").Returns(new List<ModDictionaryEntry>());
            _dictionary.GetEntryByPackageId("unknown.mod").Returns((ModDictionaryEntry?)null);

            var result = await _resolver.ResolveAsync(new[] { "unknown.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result.Should().ContainKey("unknown.mod");
            result["unknown.mod"].Should().BeEmpty();
            await _steamApi.DidNotReceiveWithAnyArgs().GetPublishedFileDetailsBatchAsync(default!, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ResolveAsync_UnpublishedCandidate_PreservesPublishedFlag()
        {
            var entry = new ModDictionaryEntry
            {
                PackageId = "old.mod",
                SteamId = "444",
                SteamName = "Unpublished Mod",
                Published = false
            };
            _dictionary.GetAllEntriesByPackageId("old.mod").Returns(new List<ModDictionaryEntry> { entry });
            _dictionary.GetEntryByPackageId("old.mod").Returns(entry);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails { PublishedFileId = "444", Title = "Unpublished Mod", Result = 1 }));

            var result = await _resolver.ResolveAsync(new[] { "old.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result["old.mod"].Should().ContainSingle();
            result["old.mod"][0].Published.Should().BeFalse();
            result["old.mod"][0].SteamId.Should().Be("444");
        }

        [Fact]
        public async Task ResolveAsync_ExcludedPackageIds_AreOmitted()
        {
            var entry = new ModDictionaryEntry { PackageId = "installed.mod", SteamId = "555", SteamName = "Installed" };
            _dictionary.GetAllEntriesByPackageId("installed.mod").Returns(new List<ModDictionaryEntry> { entry });

            var result = await _resolver.ResolveAsync(
                new[] { "installed.mod", "missing.mod" },
                excludePackageIds: new[] { "INSTALLED.MOD" },
                cancellationToken: TestContext.Current.CancellationToken);

            result.Should().NotContainKey("installed.mod");
            result.Should().ContainKey("missing.mod");
            result["missing.mod"].Should().BeEmpty();
            await _steamApi.DidNotReceiveWithAnyArgs().GetPublishedFileDetailsBatchAsync(default!, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ResolveAsync_DedupsSteamIdsCaseInsensitively()
        {
            var a = new ModDictionaryEntry { PackageId = "a.mod", SteamId = "777", SteamName = "A" };
            var b = new ModDictionaryEntry { PackageId = "b.mod", SteamId = "777", SteamName = "B" };
            _dictionary.GetAllEntriesByPackageId("a.mod").Returns(new List<ModDictionaryEntry> { a });
            _dictionary.GetAllEntriesByPackageId("b.mod").Returns(new List<ModDictionaryEntry> { b });
            _dictionary.GetEntryByPackageId(Arg.Any<string>()).Returns(call => call.Arg<string>() == "a.mod" ? a : b);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails { PublishedFileId = "777", Title = "Shared", Result = 1 }));

            await _resolver.ResolveAsync(new[] { "a.mod", "b.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            await _steamApi.Received(1).GetPublishedFileDetailsBatchAsync(
                Arg.Is<IEnumerable<string>>(ids => ids.Count() == 1 && ids.First() == "777"),
                Arg.Any<CancellationToken>());
        }
    }
}
