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

        [Fact]
        public async Task ResolveAsync_AuthorExtractedFromCreator()
        {
            var entry = new ModDictionaryEntry { PackageId = "auth.mod", SteamId = "111", SteamName = "Auth Mod" };
            _dictionary.GetAllEntriesByPackageId("auth.mod").Returns(new List<ModDictionaryEntry> { entry });
            _dictionary.GetEntryByPackageId("auth.mod").Returns(entry);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "111", Title = "Auth Mod", Result = 1, Creator = "76561198000000001"
                }));
            _steamApi.GetPersonaNameAsync("76561198000000001", Arg.Any<CancellationToken>()).Returns("Jane Doe");

            var result = await _resolver.ResolveAsync(new[] { "auth.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result["auth.mod"].Should().ContainSingle();
            result["auth.mod"][0].Author.Should().Be("Jane Doe");
            await _steamApi.Received(1).GetPersonaNameAsync("76561198000000001", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ResolveAsync_SharedCreator_ResolvedOnce()
        {
            var a = new ModDictionaryEntry { PackageId = "a.mod", SteamId = "111", SteamName = "A" };
            var b = new ModDictionaryEntry { PackageId = "b.mod", SteamId = "222", SteamName = "B" };
            _dictionary.GetAllEntriesByPackageId("a.mod").Returns(new List<ModDictionaryEntry> { a });
            _dictionary.GetAllEntriesByPackageId("b.mod").Returns(new List<ModDictionaryEntry> { b });
            _dictionary.GetEntryByPackageId(Arg.Any<string>()).Returns(call => call.Arg<string>() == "a.mod" ? a : b);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(
                    new SteamPublishedFileDetails { PublishedFileId = "111", Title = "A", Result = 1, Creator = "76561198000000002" },
                    new SteamPublishedFileDetails { PublishedFileId = "222", Title = "B", Result = 1, Creator = "76561198000000002" }));
            _steamApi.GetPersonaNameAsync("76561198000000002", Arg.Any<CancellationToken>()).Returns("Shared Author");

            var result = await _resolver.ResolveAsync(new[] { "a.mod", "b.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result["a.mod"][0].Author.Should().Be("Shared Author");
            result["b.mod"][0].Author.Should().Be("Shared Author");
            await _steamApi.Received(1).GetPersonaNameAsync("76561198000000002", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ResolveAsync_NoAuthorWhenCreatorEmpty()
        {
            var entry = new ModDictionaryEntry { PackageId = "nocreator.mod", SteamId = "111", SteamName = "No Creator" };
            _dictionary.GetAllEntriesByPackageId("nocreator.mod").Returns(new List<ModDictionaryEntry> { entry });
            _dictionary.GetEntryByPackageId("nocreator.mod").Returns(entry);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "111", Title = "No Creator", Result = 1, Creator = ""
                }));

            var result = await _resolver.ResolveAsync(new[] { "nocreator.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result["nocreator.mod"][0].Author.Should().BeNull();
            await _steamApi.DidNotReceiveWithAnyArgs().GetPersonaNameAsync(default!, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ResolveAsync_NoAuthorWhenApiResultNotOk()
        {
            var entry = new ModDictionaryEntry { PackageId = "notok.mod", SteamId = "111", SteamName = "Not OK" };
            _dictionary.GetAllEntriesByPackageId("notok.mod").Returns(new List<ModDictionaryEntry> { entry });
            _dictionary.GetEntryByPackageId("notok.mod").Returns(entry);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "111", Title = "Not OK", Result = (int)SteamApiResultCode.FileNotFound,
                    Creator = "76561198000000003"
                }));

            var result = await _resolver.ResolveAsync(new[] { "notok.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result["notok.mod"][0].Author.Should().BeNull();
            await _steamApi.DidNotReceiveWithAnyArgs().GetPersonaNameAsync(default!, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ResolveAsync_NoAuthorWhenPersonaFails()
        {
            var entry = new ModDictionaryEntry { PackageId = "fail.mod", SteamId = "111", SteamName = "Fail Author" };
            _dictionary.GetAllEntriesByPackageId("fail.mod").Returns(new List<ModDictionaryEntry> { entry });
            _dictionary.GetEntryByPackageId("fail.mod").Returns(entry);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "111", Title = "Fail Author", Result = 1, Creator = "76561198000000004"
                }));
            _steamApi.GetPersonaNameAsync("76561198000000004", Arg.Any<CancellationToken>())
                .Returns<Task<string?>>(_ => throw new InvalidOperationException("offline"));

            var result = await _resolver.ResolveAsync(new[] { "fail.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result["fail.mod"][0].Author.Should().BeNull();
        }

        [Fact]
        public async Task ResolveAsync_DictionaryAuthors_AreNotOverridden()
        {
            var entry = new ModDictionaryEntry
            {
                PackageId = "dictauthor.mod", SteamId = "111", SteamName = "Dict Author", Authors = "Dictionary Author"
            };
            _dictionary.GetAllEntriesByPackageId("dictauthor.mod").Returns(new List<ModDictionaryEntry> { entry });
            _dictionary.GetEntryByPackageId("dictauthor.mod").Returns(entry);
            _steamApi.GetPublishedFileDetailsBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
                .Returns(ApiResponse(new SteamPublishedFileDetails
                {
                    PublishedFileId = "111", Title = "Dict Author", Result = 1, Creator = "76561198000000005"
                }));

            var result = await _resolver.ResolveAsync(new[] { "dictauthor.mod" }, cancellationToken: TestContext.Current.CancellationToken);

            result["dictauthor.mod"][0].Author.Should().BeNull();
            await _steamApi.DidNotReceiveWithAnyArgs().GetPersonaNameAsync(default!, TestContext.Current.CancellationToken);
        }
    }
}
