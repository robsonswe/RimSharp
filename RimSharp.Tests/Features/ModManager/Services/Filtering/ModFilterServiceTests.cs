using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.ModManager.Services.Filtering;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Services.Filtering
{
    public class ModFilterServiceTests
    {
        private readonly ModFilterService _service;

        public ModFilterServiceTests()
        {
            _service = new ModFilterService();
        }

        private List<ModItem> GetSampleMods()
        {
            return new List<ModItem>
            {
                new ModItem { Name = "Harmony", PackageId = "brrainz.harmony", Authors = "pardeike", ModType = ModType.Workshop, Tags = "Library" },
                new ModItem { Name = "HugsLib", PackageId = "unlimitedhugs.hugslib", Authors = "UnlimitedHugs", ModType = ModType.Workshop, Tags = "Library" },
                new ModItem { Name = "RocketMan", PackageId = "krkr.rocketman", Authors = "krkr", ModType = ModType.Workshop, Tags = "Performance", IsFavorite = true },
                new ModItem { Name = "Core", PackageId = "Ludeon.RimWorld", ModType = ModType.Core }
            };
        }

        [Fact]
        public void UpdateCollections_ShouldPopulateAvailableOptions()
        {
            // Arrange
            var mods = GetSampleMods();

            // Act
            _service.UpdateCollections(new (ModItem, int)[0], mods);

            // Assert
            _service.AllAvailableTags.Should().Contain(new[] { "Library", "Performance" });
            _service.AllAvailableAuthors.Should().Contain(new[] { "pardeike", "UnlimitedHugs", "krkr" });
        }

        [Fact]
        public void ApplyActiveFilter_WithText_ShouldFilterByName()
        {
            // Arrange
            var mods = GetSampleMods();
            var activeMods = mods.Select((m, i) => (m, i)).ToList();
            _service.UpdateCollections(activeMods, new ModItem[0]);

            // Act
            _service.ApplyActiveFilter("Harmony");

            // Assert
            _service.ActiveMods.Should().ContainSingle().Which.Name.Should().Be("Harmony");
        }

        [Fact]
        public void ApplyActiveFilter_WithPackageId_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            var activeMods = mods.Select((m, i) => (m, i)).ToList();
            _service.UpdateCollections(activeMods, new ModItem[0]);

            // Act
            _service.ApplyActiveFilter("unlimitedhugs");

            // Assert
            _service.ActiveMods.Should().ContainSingle().Which.Name.Should().Be("HugsLib");
        }

        [Fact]
        public void ApplyActiveFilterCriteria_WithModType_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            _service.UpdateCollections(new (ModItem, int)[0], mods);

            // Act
            var criteria = new ModFilterCriteria { SelectedModTypes = new List<ModType> { ModType.Core } };
            _service.ApplyInactiveFilterCriteria(criteria);

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.ModType.Should().Be(ModType.Core);
        }

        [Fact]
        public void ApplyActiveFilterCriteria_WithTags_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            _service.UpdateCollections(new (ModItem, int)[0], mods);

            // Act
            var criteria = new ModFilterCriteria { SelectedTags = new List<string> { "Performance" } };
            _service.ApplyInactiveFilterCriteria(criteria);

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.Name.Should().Be("RocketMan");
        }

        [Fact]
        public void ApplyActiveFilterCriteria_WithFavorite_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            _service.UpdateCollections(new (ModItem, int)[0], mods);

            // Act
            var criteria = new ModFilterCriteria { IsFavoriteFilter = true };
            _service.ApplyInactiveFilterCriteria(criteria);

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.Name.Should().Be("RocketMan");
        }

        [Fact]
        public void FuzzySearch_ShouldMatchSimilarNames()
        {
            // Arrange
            var mods = new List<ModItem> { new ModItem { Name = "Wall Light", PackageId = "walllight" } };
            _service.UpdateCollections(new (ModItem, int)[0], mods);

            // Act
            _service.ApplyInactiveFilter("Wal Light"); // Typos

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.Name.Should().Be("Wall Light");
        }
    }
}
