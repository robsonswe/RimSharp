using FluentAssertions;
using RimSharp.Features.VramAnalysis.ViewModels;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Features.VramAnalysis.ViewModels
{
    public class VramModItemWrapperTests
    {
        [Fact]
        public void HasConditionalContent_WhenVramEqualsMax_ShouldBeFalse()
        {
            // Arrange
            var mod = new ModItem { Name = "Test Mod" };
            var wrapper = new VramModItemWrapper(mod);

            // Act
            wrapper.EstimatedVramCompressed = 100;
            wrapper.MaxEstimatedVramCompressed = 100;

            // Assert
            wrapper.HasConditionalContent.Should().BeFalse();
        }

        [Fact]
        public void HasConditionalContent_WhenVramNotEqualsMax_ShouldBeTrue()
        {
            // Arrange
            var mod = new ModItem { Name = "Test Mod" };
            var wrapper = new VramModItemWrapper(mod);

            // Act
            wrapper.EstimatedVramCompressed = 100;
            wrapper.MaxEstimatedVramCompressed = 200;

            // Assert
            wrapper.HasConditionalContent.Should().BeTrue();
        }

        [Fact]
        public void SetProperty_ShouldNotifyChange()
        {
            // Arrange
            var mod = new ModItem { Name = "Test Mod" };
            var wrapper = new VramModItemWrapper(mod);
            string? changedProp = null;
            wrapper.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            // Act
            wrapper.TextureCount = 5;

            // Assert
            changedProp.Should().Be(nameof(VramModItemWrapper.TextureCount));
            wrapper.TextureCount.Should().Be(5);
        }
    }
}
