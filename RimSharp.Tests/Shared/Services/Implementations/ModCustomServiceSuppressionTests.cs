using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    /// <summary>
    /// Covers the additive override/disable layer introduced for the unified
    /// CustomizeMod dialog: disabled inherited rules, custom-overrides-inherited
    /// suppression with snapshot restoration, and mutually-exclusive load
    /// position overrides.
    /// </summary>
    public class ModCustomServiceSuppressionTests : IDisposable
    {
        private readonly ILoggerService _mockLogger;
        private readonly string _testTempDir;

        public ModCustomServiceSuppressionTests()
        {
            _mockLogger = Substitute.For<ILoggerService>();
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_CustomSuppression_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
                Directory.Delete(_testTempDir, true);
        }

        [Fact]
        public async Task DisabledLoadBefore_StripsInheritedRule_AndRestoresOnRemoval()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                DisabledLoadBefore = new List<string> { "other.mod" }
            });

            var mod = new ModItem { PackageId = "test.mod" };
            mod.LoadBefore.Add("other.mod");

            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().NotContain("other.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().Contain("other.mod");
        }

        [Fact]
        public async Task CustomLoadAfter_SuppressesInheritedLoadBefore_AndRestoresOnRemoval()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadAfter = new Dictionary<string, ModDependencyRule> { ["other.mod"] = new ModDependencyRule() }
            });

            var mod = new ModItem { PackageId = "test.mod" };
            mod.LoadBefore.Add("other.mod");

            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().NotContain("other.mod");
            mod.LoadAfter.Should().Contain("other.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().Contain("other.mod");
            mod.LoadAfter.Should().NotContain("other.mod");
        }

        [Fact]
        public async Task CustomIncompatibleWith_SuppressesInheritedOrderRules_AndRestoresOnRemoval()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                IncompatibleWith = new Dictionary<string, ModIncompatibilityRule> { ["bad.mod"] = new ModIncompatibilityRule() }
            });

            var mod = new ModItem { PackageId = "test.mod" };
            mod.LoadBefore.Add("bad.mod");
            mod.LoadAfter.Add("bad.mod");

            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().NotContain("bad.mod");
            mod.LoadAfter.Should().NotContain("bad.mod");
            mod.IncompatibleWith.Should().ContainKey("bad.mod");

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().Contain("bad.mod");
            mod.LoadAfter.Should().Contain("bad.mod");
            mod.IncompatibleWith.Should().NotContainKey("bad.mod");
        }

        [Fact]
        public async Task ForceTop_OverridesInheritedLoadBottom_AndRestoresOnRemoval()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadTop = new LoadBottomRule { Value = true }
            });

            var mod = new ModItem { PackageId = "test.mod", LoadBottom = true };

            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadTop.Should().BeTrue();
            mod.LoadBottom.Should().BeFalse();

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadTop.Should().BeFalse();
            mod.LoadBottom.Should().BeTrue();
        }

        [Fact]
        public async Task ForceBottom_OverridesInheritedLoadTop_AndRestoresOnRemoval()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                LoadBottom = new LoadBottomRule { Value = true }
            });

            var mod = new ModItem { PackageId = "test.mod", LoadTop = true };

            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBottom.Should().BeTrue();
            mod.LoadTop.Should().BeFalse();

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBottom.Should().BeFalse();
            mod.LoadTop.Should().BeTrue();
        }

        [Fact]
        public async Task DisableInheritedLoadTop_ClearsFlag_AndRestoresOnRemoval()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                DisableInheritedLoadTop = true
            });

            var mod = new ModItem { PackageId = "test.mod", LoadTop = true };

            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadTop.Should().BeFalse();

            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo());
            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadTop.Should().BeTrue();
        }

        [Fact]
        public async Task StripCustomInfoFromMods_RemovesContributionsAndRestoresSuppressedRules()
        {
            var service = new ModCustomService(_testTempDir, _mockLogger);
            await service.SaveCustomModInfoAsync("test.mod", new ModCustomInfo
            {
                DisabledLoadBefore = new List<string> { "other.mod" },
                Tags = "qol"
            });

            var mod = new ModItem { PackageId = "test.mod", Tags = "base" };
            mod.LoadBefore.Add("other.mod");

            service.ApplyCustomInfoToMods(new[] { mod });
            mod.LoadBefore.Should().NotContain("other.mod");
            mod.TagList.Should().Contain("qol");

            service.StripCustomInfoFromMods(new[] { mod });
            mod.LoadBefore.Should().Contain("other.mod");
            mod.TagList.Should().NotContain("qol");
            mod.TagList.Should().Contain("base");
        }
    }
}
