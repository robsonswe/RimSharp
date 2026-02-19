using System;
using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Services.Commands
{
    public class DropModArgsTests
    {
        [Fact]
        public void DropModArgs_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var args = new DropModArgs();

            // Assert
            args.DroppedItems.Should().BeNull();
            args.TargetListName.Should().BeNull();
            args.DropIndex.Should().Be(0);
        }

        [Fact]
        public void DropModArgs_ShouldAllowSettingProperties()
        {
            // Arrange
            var items = new List<ModItem>
            {
                new ModItem { PackageId = "mod1", Name = "Mod 1" },
                new ModItem { PackageId = "mod2", Name = "Mod 2" }
            };

            // Act
            var args = new DropModArgs
            {
                DroppedItems = items,
                TargetListName = "ActiveMods",
                DropIndex = 5
            };

            // Assert
            args.DroppedItems.Should().HaveCount(2);
            args.TargetListName.Should().Be("ActiveMods");
            args.DropIndex.Should().Be(5);
        }

        [Fact]
        public void DropModArgs_ShouldAcceptEmptyItemList()
        {
            // Arrange & Act
            var args = new DropModArgs
            {
                DroppedItems = new List<ModItem>(),
                TargetListName = "TestList",
                DropIndex = 0
            };

            // Assert
            args.DroppedItems.Should().BeEmpty();
        }

        [Fact]
        public void DropModArgs_ShouldAcceptNullTargetListName()
        {
            // Arrange & Act
            var args = new DropModArgs
            {
                DroppedItems = new List<ModItem>(),
                TargetListName = null,
                DropIndex = 0
            };

            // Assert
            args.TargetListName.Should().BeNull();
        }

        [Fact]
        public void DropModArgs_ShouldAcceptNegativeDropIndex()
        {
            // Arrange & Act
            var args = new DropModArgs
            {
                DroppedItems = new List<ModItem>(),
                TargetListName = "TestList",
                DropIndex = -1
            };

            // Assert
            args.DropIndex.Should().Be(-1);
        }
    }
}
