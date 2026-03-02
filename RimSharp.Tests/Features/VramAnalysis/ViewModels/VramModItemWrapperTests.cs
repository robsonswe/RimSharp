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

            var mod = new ModItem { Name = "Test Mod" };
            var wrapper = new VramModItemWrapper(mod);

            wrapper.EstimatedVramCompressed = 100;
            wrapper.MaxEstimatedVramCompressed = 100;

            wrapper.HasConditionalContent.Should().BeFalse();
        }

        [Fact]
        public void HasConditionalContent_WhenVramNotEqualsMax_ShouldBeTrue()
        {

            var mod = new ModItem { Name = "Test Mod" };
            var wrapper = new VramModItemWrapper(mod);

            wrapper.EstimatedVramCompressed = 100;
            wrapper.MaxEstimatedVramCompressed = 200;

            wrapper.HasConditionalContent.Should().BeTrue();
        }

        [Fact]
        public void SetProperty_ShouldNotifyChange()
        {

            var mod = new ModItem { Name = "Test Mod" };
            var wrapper = new VramModItemWrapper(mod);
            string? changedProp = null;
            wrapper.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            wrapper.TextureCount = 5;

            changedProp.Should().Be(nameof(VramModItemWrapper.TextureCount));
            wrapper.TextureCount.Should().Be(5);
        }
    }
}

