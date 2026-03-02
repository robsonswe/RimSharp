using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Services.Management
{
    public class ModLookupServiceTests
    {
        private readonly ModLookupService _service;

        public ModLookupServiceTests()
        {
            _service = new ModLookupService();
        }

        [Fact]
        public void Initialize_ShouldBuildLookup()
        {

            var mods = new List<ModItem>
            {
                new ModItem { PackageId = "Mod.A", Name = "Mod A" },
                new ModItem { PackageId = "Mod.B", Name = "Mod B" }
            };

            _service.Initialize(mods);

            _service.TryGetMod("Mod.A", out var modA).Should().BeTrue();
            modA!.Name.Should().Be("Mod A");
            _service.TryGetMod("mod.b", out var modB).Should().BeTrue(); // Case-insensitive
            modB!.Name.Should().Be("Mod B");
        }

        [Fact]
        public void Register_ShouldAddMod()
        {

            var mod = new ModItem { PackageId = "Mod.C", Name = "Mod C" };

            _service.Register(mod);

            _service.TryGetMod("Mod.C", out var found).Should().BeTrue();
            found.Should().Be(mod);
        }

        [Fact]
        public void Remove_ShouldOnlyRemoveCorrectInstance()
        {

            var mod1 = new ModItem { PackageId = "Mod.D" };
            var mod2 = new ModItem { PackageId = "Mod.D" };
            _service.Initialize(new[] { mod1 });

            _service.Remove(mod2); // Different instance, same PackageId

            _service.TryGetMod("Mod.D", out var found).Should().BeTrue();
            found.Should().Be(mod1);

            _service.Remove(mod1);

            _service.TryGetMod("Mod.D", out _).Should().BeFalse();
        }

        [Fact]
        public void TryGetMod_WithEmptyId_ShouldReturnFalse()
        {
            // Act & Assert
            _service.TryGetMod(null, out _).Should().BeFalse();
            _service.TryGetMod("", out _).Should().BeFalse();
        }
    }
}

