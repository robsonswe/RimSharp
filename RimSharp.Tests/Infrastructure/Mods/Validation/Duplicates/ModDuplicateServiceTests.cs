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
    }
}

