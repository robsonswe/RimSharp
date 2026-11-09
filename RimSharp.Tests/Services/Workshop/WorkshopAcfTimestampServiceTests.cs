using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.Workshop;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    /// <summary>
    /// Tests for applying ACF-derived workshop item timestamps onto scanned mods
    /// (keyed by SteamId), including the JSON cache and its LastWriteTimeUtc
    /// invalidation behavior.
    /// </summary>
    public class WorkshopAcfTimestampServiceTests : IDisposable
    {
        private readonly string _testTempDir;
        private readonly string _cacheDir;           // per-test cache location
        private readonly string _workshopContentDir; // workshop\content\294100
        private readonly string _acfPath;            // workshop\appworkshop_294100.acf
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();

        private const string GoodAcf =
            "\"AppWorkshop\"\n" +
            "{\n" +
            "\"WorkshopItemsInstalled\"\n" +
            "{\n" +
            "\"818773962\"\t\t\"1694112958\"\n" +
            "\"2009463077\"\t\t\"1660069393\"\n" +
            "}\n" +
            "\"WorkshopItemDetails\"\n" +
            "{\n" +
            "\"818773962\"\n" +
            "{\n" +
            "\"timeupdated\"\t\t\"1690000000\"\n" +
            "\"timetouched\"\t\t\"1700000000\"\n" +
            "}\n" +
            "\"2009463077\"\n" +
            "{\n" +
            "\"timeupdated\"\t\t\"1660080000\"\n" +
            "}\n" +
            "}\n" +
            "}\n";

        private const string DifferentAcf =
            "\"AppWorkshop\"\n" +
            "{\n" +
            "\"WorkshopItemsInstalled\"\n" +
            "{\n" +
            "\"818773962\"\t\t\"1700000000\"\n" +
            "\"2009463077\"\t\t\"1700000001\"\n" +
            "}\n" +
            "}\n";

        public WorkshopAcfTimestampServiceTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            _cacheDir = Path.Combine(_testTempDir, "cache");
            _workshopContentDir = Path.Combine(_testTempDir, "steamapps", "workshop", "content", "294100");
            _acfPath = Path.Combine(_testTempDir, "steamapps", "workshop", "appworkshop_294100.acf");
            Directory.CreateDirectory(_cacheDir);
            Directory.CreateDirectory(_workshopContentDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private WorkshopAcfTimestampService CreateService() =>
            new(new AcfFileParser(), _logger, _cacheDir);

        [Fact]
        public void ApplyToMods_ValidLayout_ShouldSetAcfTimeUpdatedFromAcf()
        {
            File.WriteAllText(_acfPath, GoodAcf);
            var mod = new ModItem { Name = "HD Texture Pack", SteamId = "2009463077" };
            var vanilla = new ModItem { Name = "Core" };

            CreateService().ApplyToMods(new[] { mod, vanilla }, _workshopContentDir);

            // Details entry wins over the Installed fallback record
            mod.AcfTimeUpdated.Should().Be(1660080000);
            vanilla.AcfTimeUpdated.Should().BeNull();
        }

        [Fact]
        public void ApplyToMods_SteamIdMissingFromAcf_ShouldClearToNull()
        {
            File.WriteAllText(_acfPath, GoodAcf);
            var mod = new ModItem { Name = "Not Locally Tracked", SteamId = "111111111" };

            CreateService().ApplyToMods(new[] { mod }, _workshopContentDir);

            mod.AcfTimeUpdated.Should().BeNull();
        }

        [Fact]
        public void ApplyToMods_ZeroStamp_ShouldStayNull()
        {
            // timeupdated zero AND no installed record in the manifest → resolved 0
            File.WriteAllText(_acfPath,
                "\"AppWorkshop\"\n{\n\"WorkshopItemDetails\"\n{\n\"1\"\n{\n\"timeupdated\"\t\t\"0\"\n}\n}\n}\n");
            var mod = new ModItem { Name = "Zero Record", SteamId = "1" };

            CreateService().ApplyToMods(new[] { mod }, _workshopContentDir);

            mod.AcfTimeUpdated.Should().BeNull();
        }

        [Fact]
        public void ApplyToMods_NonNumericSteamId_ShouldStayNullAndNotThrow()
        {
            File.WriteAllText(_acfPath, GoodAcf);
            var mod = new ModItem { Name = "Weird Id", SteamId = "not-a-number" };

            CreateService().ApplyToMods(new[] { mod }, _workshopContentDir);

            mod.AcfTimeUpdated.Should().BeNull();
        }

        [Fact]
        public void GetTimestamps_SteamCmdLayout_ShouldResolveTwoLevelsUp()
        {
            // SteamCMD: <prefix>\steam\steamapps\workshop\content\294100 — same ..\.. layout
            var steamCmdContent = Path.Combine(_testTempDir, "steamcmd", "steam", "steamapps", "workshop", "content", "294100");
            var steamCmdAcf = Path.Combine(_testTempDir, "steamcmd", "steam", "steamapps", "workshop", "appworkshop_294100.acf");
            Directory.CreateDirectory(steamCmdContent);
            File.WriteAllText(steamCmdAcf, GoodAcf);

            var stamps = CreateService().GetTimestamps(steamCmdContent);

            stamps["818773962"].Should().Be(1690000000);
            stamps["2009463077"].Should().Be(1660080000);
        }

        [Fact]
        public void GetTimestamps_MissingAcfFile_ShouldReturnEmptyWithoutCacheWrite()
        {
            var stamps = CreateService().GetTimestamps(_workshopContentDir);

            stamps.Should().BeEmpty();
            File.Exists(Path.Combine(_cacheDir, "workshopacf_cache.json")).Should().BeFalse();
        }

        [Fact]
        public void GetTimestamps_MissingWorkshopFolder_ShouldReturnEmpty()
        {
            CreateService().GetTimestamps(null).Should().BeEmpty();
            CreateService().GetTimestamps(string.Empty).Should().BeEmpty();
        }

        [Fact]
        public void GetTimestamps_CorruptAcf_ShouldReturnEmpty()
        {
            File.WriteAllText(_acfPath, "this is not an acf manifest");

            CreateService().GetTimestamps(_workshopContentDir).Should().BeEmpty();
        }

        [Fact]
        public void GetTimestamps_CachedManifest_ShouldBeReusedWithoutReparse()
        {
            // First parse — populates the cache keyed to the manifest's mtime
            File.WriteAllText(_acfPath, GoodAcf);
            var first = CreateService().GetTimestamps(_workshopContentDir);
            first["818773962"].Should().Be(1690000000);
            var cachedMtime = File.GetLastWriteTimeUtc(_acfPath);

            // Rewrite the manifest with DIFFERENT stamps but restore the exact
            // mtime — a re-scan must serve the cached map, not re-read the file.
            File.WriteAllText(_acfPath, DifferentAcf);
            File.SetLastWriteTimeUtc(_acfPath, cachedMtime);

            var second = CreateService().GetTimestamps(_workshopContentDir);

            var cacheFile = Path.Combine(_cacheDir, "workshopacf_cache.json");
            var cacheText = File.Exists(cacheFile) ? File.ReadAllText(cacheFile) : "(no cache file written)";
            second["818773962"].Should().Be(1690000000,
                $"because the cache is: {cacheText}; actual mtime now: {File.GetLastWriteTimeUtc(_acfPath).ToString("O")} " +
                $"(kind {File.GetLastWriteTimeUtc(_acfPath).Kind}) vs cachedMtime captured: {cachedMtime.ToString("O")} (kind {cachedMtime.Kind})");
            second["2009463077"].Should().Be(1660080000);
        }

        [Fact]
        public void GetTimestamps_AcfMtimeChange_ShouldInvalidateCache()
        {
            File.WriteAllText(_acfPath, GoodAcf);
            var first = CreateService().GetTimestamps(_workshopContentDir);
            first["818773962"].Should().Be(1690000000);
            var cachedMtime = File.GetLastWriteTimeUtc(_acfPath);

            // Steam rewrote the manifest: new stamp + strictly newer LastWriteTimeUtc → re-parse.
            // The mtime is pinned deliberately: on a busy runner two fast writes can share one
            // filesystem timestamp quantum, which would make invalidation a coin flip.
            File.WriteAllText(_acfPath, DifferentAcf);
            File.SetLastWriteTimeUtc(_acfPath, cachedMtime.AddSeconds(1));

            var second = CreateService().GetTimestamps(_workshopContentDir);

            second["818773962"].Should().Be(1700000000);
            second["2009463077"].Should().Be(1700000001);
            second.Should().HaveCount(2);
        }

        [Fact]
        public void ApplyToMods_AfterCacheInvalidation_ShouldReflectNewStamps()
        {
            File.WriteAllText(_acfPath, GoodAcf);
            var mod = new ModItem { SteamId = "818773962" };
            CreateService().ApplyToMods(new[] { mod }, _workshopContentDir);
            mod.AcfTimeUpdated.Should().Be(1690000000);
            var cachedMtime = File.GetLastWriteTimeUtc(_acfPath);

            // The item updates via Steam → the manifest is rewritten by the client.
            // Pinning the new mtime keeps invalidation deterministic on runners where
            // two fast writes share one filesystem timestamp quantum.
            File.WriteAllText(_acfPath, DifferentAcf);
            File.SetLastWriteTimeUtc(_acfPath, cachedMtime.AddSeconds(1));

            var freshMod = new ModItem { SteamId = "818773962" };
            CreateService().ApplyToMods(new[] { freshMod }, _workshopContentDir);

            freshMod.AcfTimeUpdated.Should().Be(1700000000);
        }
    }
}
