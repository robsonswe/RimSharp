using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Services.Management
{
    public class ModStateTrackerTests
    {
        private readonly ModStateTracker _tracker;

        public ModStateTrackerTests()
        {
            _tracker = new ModStateTracker();
        }

        [Fact]
        public void Initialize_ShouldCategorizeMods()
        {
            // Arrange
            var mod1 = new ModItem { Name = "Mod 1", PackageId = "mod1" };
            var mod2 = new ModItem { Name = "Mod 2", PackageId = "mod2" };
            var lookup = new Dictionary<string, ModItem> { ["mod1"] = mod1, ["mod2"] = mod2 };
            var active = new[] { "mod1" };

            // Act
            _tracker.Initialize(new[] { mod1, mod2 }, lookup, active);

            // Assert
            _tracker.IsModActive(mod1).Should().BeTrue();
            _tracker.IsModActive(mod2).Should().BeFalse();
            _tracker.AllInactiveMods.Should().ContainSingle().Which.Should().Be(mod2);
        }

        [Fact]
        public void Initialize_ShouldTrackMissingMods()
        {
            // Arrange
            var active = new[] { "missing" };

            // Act
            _tracker.Initialize(new ModItem[0], new Dictionary<string, ModItem>(), active);

            // Assert
            _tracker.MissingModIds.Should().Contain("missing");
        }

        [Fact]
        public void Deactivate_ShouldPreventCoreModDeactivation()
        {
            // Arrange
            var core = new ModItem { Name = "Core", PackageId = "core", ModType = ModType.Core };
            var lookup = new Dictionary<string, ModItem> { ["core"] = core };
            _tracker.Initialize(new[] { core }, lookup, new[] { "core" });

            // Act
            _tracker.Deactivate(core);

            // Assert
            _tracker.IsModActive(core).Should().BeTrue();
        }

        [Fact]
        public void Activate_ShouldMoveModToActive()
        {
            // Arrange
            var mod = new ModItem { Name = "Mod", PackageId = "mod" };
            _tracker.Initialize(new[] { mod }, new Dictionary<string, ModItem> { ["mod"] = mod }, new string[0]);

            // Act
            _tracker.Activate(mod);

            // Assert
            _tracker.IsModActive(mod).Should().BeTrue();
            _tracker.AllInactiveMods.Should().BeEmpty();
        }
    }
}
