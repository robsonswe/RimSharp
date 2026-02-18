using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class ModRulesServiceTests
    {
        private readonly IModRulesRepository _mockRepository;
        private readonly ModRulesService _service;

        public ModRulesServiceTests()
        {
            _mockRepository = Substitute.For<IModRulesRepository>();
            _service = new ModRulesService(_mockRepository);
        }

        [Fact]
        public void ApplyRulesToMods_ShouldAddSupportedVersions()
        {
            // Arrange
            var mod = new ModItem { PackageId = "mod1", SupportedVersions = new List<VersionSupport>() };
            var rule = new ModRule { SupportedVersions = new List<string> { "1.5" } };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            // Act
            _service.ApplyRulesToMods(new[] { mod });

            // Assert
            mod.SupportedVersions.Should().ContainSingle(v => v.Version == "1.5" && v.Source == VersionSource.Database);
        }

        [Fact]
        public void ApplyRulesToMods_ShouldNotAddDuplicateVersions()
        {
            // Arrange
            var mod = new ModItem 
            { 
                PackageId = "mod1", 
                SupportedVersions = new List<VersionSupport> { new VersionSupport("1.5", VersionSource.Official) } 
            };
            var rule = new ModRule { SupportedVersions = new List<string> { "1.5" } };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            // Act
            _service.ApplyRulesToMods(new[] { mod });

            // Assert
            mod.SupportedVersions.Should().HaveCount(1);
            mod.SupportedVersions.First().Source.Should().Be(VersionSource.Official);
        }

        [Fact]
        public void ApplyRulesToMods_ShouldBeCaseInsensitive()
        {
            // Arrange
            var mod = new ModItem { PackageId = "MOD1" };
            var rule = new ModRule { SupportedVersions = new List<string> { "1.5" } };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            // Act
            _service.ApplyRulesToMods(new[] { mod });

            // Assert
            mod.SupportedVersions.Should().NotBeEmpty();
        }

        [Fact]
        public void ApplyRulesToMods_ShouldApplyLoadOrderRules()
        {
            // Arrange
            var mod = new ModItem { PackageId = "mod1" };
            var rule = new ModRule 
            { 
                LoadBefore = new Dictionary<string, ModDependencyRule> { { "otherMod", new ModDependencyRule() } },
                LoadAfter = new Dictionary<string, ModDependencyRule> { { "baseMod", new ModDependencyRule() } },
                LoadBottom = new LoadBottomRule { Value = true }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            // Act
            _service.ApplyRulesToMods(new[] { mod });

            // Assert
            mod.LoadBefore.Should().Contain("otherMod");
            mod.LoadAfter.Should().Contain("baseMod");
            mod.LoadBottom.Should().BeTrue();
        }

        [Fact]
        public void ApplyRulesToMods_ShouldApplyIncompatibilities()
        {
            // Arrange
            var mod = new ModItem { PackageId = "mod1" };
            var incompatibility = new ModIncompatibilityRule { HardIncompatibility = true };
            var rule = new ModRule 
            { 
                Incompatibilities = new Dictionary<string, ModIncompatibilityRule> { { "badMod", incompatibility } }
            };
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule> { { "mod1", rule } });

            // Act
            _service.ApplyRulesToMods(new[] { mod });

            // Assert
            mod.IncompatibleWith.Should().ContainKey("badMod");
            mod.IncompatibleWith["badMod"].HardIncompatibility.Should().BeTrue();
        }

        [Fact]
        public void GetRulesForMod_WhenNotFound_ShouldReturnEmptyRule()
        {
             // Arrange
            _mockRepository.GetAllRules().Returns(new Dictionary<string, ModRule>());

            // Act
            var result = _service.GetRulesForMod("nonexistent");

            // Assert
            result.Should().NotBeNull();
            result.SupportedVersions.Should().BeEmpty();
            result.LoadBefore.Should().BeEmpty();
        }
    }
}
