using FluentAssertions;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Shared.Models
{
    public class PathSettingsTests
    {
        [Fact]
        public void PropertyChange_ShouldNotify()
        {
            // Arrange
            var settings = new PathSettings();
            string? changedProp = null;
            settings.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            // Act
            settings.GamePath = @"C:\NewPath";

            // Assert
            changedProp.Should().Be(nameof(PathSettings.GamePath));
            settings.GamePath.Should().Be(@"C:\NewPath");
        }
    }
}
