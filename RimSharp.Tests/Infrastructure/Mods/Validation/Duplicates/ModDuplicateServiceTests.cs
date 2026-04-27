using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Infrastructure.Mods.Validation.Duplicates;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Validation.Duplicates
{
    public class ModDuplicateServiceTests
    {
        private readonly ModDuplicateService _service;

        public ModDuplicateServiceTests()
        {
            _service = new ModDuplicateService();
        }

        [Fact]
        public void FindDuplicateGroups_WhenNoDuplicates_ShouldReturnEmpty()
        {

            var mods = new List<ModItem>
            {
                new ModItem { PackageId = "mod1" },
                new ModItem { PackageId = "mod2" }
            };

            var result = _service.FindDuplicateGroups(mods);

            result.Should().BeEmpty();
        }

        [Fact]
        public void FindDuplicateGroups_ShouldDetectDuplicatesCaseInsensitively()
        {

            var mod1 = new ModItem { PackageId = "mod1" };
            var mod2 = new ModItem { PackageId = "MOD1" };
            var mod3 = new ModItem { PackageId = "mod2" };
            var mods = new List<ModItem> { mod1, mod2, mod3 };

            var result = _service.FindDuplicateGroups(mods);

            result.Should().HaveCount(1);
            result[0].Key.Should().Be("mod1");
            result[0].Should().HaveCount(2);
        }

        [Fact]
        public void FindDuplicateGroups_WhenInputIsNull_ShouldReturnEmpty()
        {

            var result = _service.FindDuplicateGroups(null!);

            result.Should().BeEmpty();
        }

        [Fact]
        public void FindDuplicateGroups_WhenInputIsEmpty_ShouldReturnEmpty()
        {

            var result = _service.FindDuplicateGroups(new List<ModItem>());

            result.Should().BeEmpty();
        }

        [Fact]
        public void FindDuplicateGroups_WhenMultipleDuplicateGroups_ShouldDetectAll()
        {

            var mod1a = new ModItem { PackageId = "mod1" };
            var mod1b = new ModItem { PackageId = "mod1" };
            var mod2a = new ModItem { PackageId = "mod2" };
            var mod2b = new ModItem { PackageId = "MOD2" };
            var mod3 = new ModItem { PackageId = "mod3" };
            var mods = new List<ModItem> { mod1a, mod1b, mod2a, mod2b, mod3 };

            var result = _service.FindDuplicateGroups(mods);

            result.Should().HaveCount(2);
            result.Should().Contain(g => g.Key == "mod1" && g.Count() == 2);
            result.Should().Contain(g => g.Key == "mod2" && g.Count() == 2);
        }

        [Fact]
        public void FindDuplicateGroups_ThreeWayDuplicate_ShouldReturnAllThree()
        {

            var mod1 = new ModItem { PackageId = "dup.mod" };
            var mod2 = new ModItem { PackageId = "dup.mod" };
            var mod3 = new ModItem { PackageId = "DUP.MOD" };
            var mods = new List<ModItem> { mod1, mod2, mod3 };

            var result = _service.FindDuplicateGroups(mods);

            result.Should().HaveCount(1);
            result[0].Should().HaveCount(3);
        }

        [Fact]
        public void FindDuplicateGroups_ShouldIgnoreModsWithNullOrEmptyPackageId()
        {

            var mod1 = new ModItem { PackageId = null! };
            var mod2 = new ModItem { PackageId = "" };
            var mod3 = new ModItem { PackageId = "real.mod" };
            var mods = new List<ModItem> { mod1, mod2, mod3 };

            var result = _service.FindDuplicateGroups(mods);

            result.Should().BeEmpty();
        }
    }
}

