using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        private async Task WaitForFilteringAsync(Action action, int expectedEvents = 1)
        {
            using var semaphore = new System.Threading.SemaphoreSlim(0);
            int eventsReceived = 0;
            EventHandler handler = (s, e) => 
            {
                if (System.Threading.Interlocked.Increment(ref eventsReceived) >= expectedEvents)
                {
                    semaphore.Release();
                }
            };
            
            _service.FilteringCompleted += handler;
            try
            {
                action();
                // Use a reasonable timeout for tests
                bool signaled = await semaphore.WaitAsync(2000);
                if (!signaled && eventsReceived < expectedEvents)
                {
                    throw new TimeoutException($"Timed out waiting for {expectedEvents} filtering events. Only received {eventsReceived}.");
                }
            }
            finally
            {
                _service.FilteringCompleted -= handler;
            }
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
        public async Task UpdateCollections_ShouldPopulateAvailableOptions()
        {
            // Arrange
            var mods = GetSampleMods();

            // Act
            await WaitForFilteringAsync(() => 
                _service.UpdateCollections(new (ModItem, int)[0], mods),
                expectedEvents: 2 // One for active, one for inactive
            );

            // Assert
            _service.AllAvailableTags.Should().Contain(new[] { "Library", "Performance" });
            _service.AllAvailableAuthors.Should().Contain(new[] { "pardeike", "UnlimitedHugs", "krkr" });
        }

        [Fact]
        public async Task ApplyActiveFilter_WithText_ShouldFilterByName()
        {
            // Arrange
            var mods = GetSampleMods();
            var activeMods = mods.Select((m, i) => (m, i)).ToList();
            await WaitForFilteringAsync(() => 
                _service.UpdateCollections(activeMods, new ModItem[0]),
                expectedEvents: 2
            );

            // Act
            await WaitForFilteringAsync(() => 
                _service.ApplyActiveFilter("Harmony")
            );

            // Assert
            _service.ActiveMods.Should().ContainSingle().Which.Name.Should().Be("Harmony");
        }

        [Fact]
        public async Task ApplyActiveFilter_WithPackageId_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            var activeMods = mods.Select((m, i) => (m, i)).ToList();
            await WaitForFilteringAsync(() => 
                _service.UpdateCollections(activeMods, new ModItem[0]),
                expectedEvents: 2
            );

            // Act
            await WaitForFilteringAsync(() => 
                _service.ApplyActiveFilter("unlimitedhugs")
            );

            // Assert
            _service.ActiveMods.Should().ContainSingle().Which.Name.Should().Be("HugsLib");
        }

        [Fact]
        public async Task ApplyActiveFilterCriteria_WithModType_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            await WaitForFilteringAsync(() => 
                _service.UpdateCollections(new (ModItem, int)[0], mods),
                expectedEvents: 2
            );

            // Act
            var criteria = new ModFilterCriteria { SelectedModTypes = new List<ModType> { ModType.Core } };
            await WaitForFilteringAsync(() => 
                _service.ApplyInactiveFilterCriteria(criteria)
            );

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.ModType.Should().Be(ModType.Core);
        }

        [Fact]
        public async Task ApplyActiveFilterCriteria_WithTags_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            await WaitForFilteringAsync(() => 
                _service.UpdateCollections(new (ModItem, int)[0], mods),
                expectedEvents: 2
            );

            // Act
            var criteria = new ModFilterCriteria { SelectedTags = new List<string> { "Performance" } };
            await WaitForFilteringAsync(() => 
                _service.ApplyInactiveFilterCriteria(criteria)
            );

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.Name.Should().Be("RocketMan");
        }

        [Fact]
        public async Task ApplyActiveFilterCriteria_WithFavorite_ShouldFilter()
        {
            // Arrange
            var mods = GetSampleMods();
            await WaitForFilteringAsync(() => 
                _service.UpdateCollections(new (ModItem, int)[0], mods),
                expectedEvents: 2
            );

            // Act
            var criteria = new ModFilterCriteria { IsFavoriteFilter = true };
            await WaitForFilteringAsync(() => 
                _service.ApplyInactiveFilterCriteria(criteria)
            );

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.Name.Should().Be("RocketMan");
        }

        [Fact]
        public async Task FuzzySearch_ShouldMatchSimilarNames()
        {
            // Arrange
            var mods = new List<ModItem> { new ModItem { Name = "Wall Light", PackageId = "walllight" } };
            await WaitForFilteringAsync(() => 
                _service.UpdateCollections(new (ModItem, int)[0], mods),
                expectedEvents: 2
            );

            // Act
            await WaitForFilteringAsync(() => 
                _service.ApplyInactiveFilter("Wal Light") // Typos
            );

            // Assert
            _service.InactiveMods.Should().ContainSingle().Which.Name.Should().Be("Wall Light");
        }
    }
}
