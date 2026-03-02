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

            args.DroppedItems.Should().BeEmpty();
            args.TargetListName.Should().BeEmpty();
            args.DropIndex.Should().Be(0);
        }

        [Fact]
        public void DropModArgs_ShouldAllowSettingProperties()
        {

            var items = new List<ModItem>
            {
                new ModItem { PackageId = "mod1", Name = "Mod 1" },
                new ModItem { PackageId = "mod2", Name = "Mod 2" }
            };

            var args = new DropModArgs
            {
                DroppedItems = items,
                TargetListName = "ActiveMods",
                DropIndex = 5
            };

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

            args.DroppedItems.Should().BeEmpty();
        }

        [Fact]
        public void DropModArgs_ShouldAcceptNullTargetListName()
        {
            // Arrange & Act
            var args = new DropModArgs
            {
                DroppedItems = new List<ModItem>(),
                TargetListName = null!,
                DropIndex = 0
            };

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

            args.DropIndex.Should().Be(-1);
        }
    }
}

