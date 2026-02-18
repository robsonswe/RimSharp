using FluentAssertions;
using RimSharp.Shared.Services.Implementations;
using Xunit;

namespace RimSharp.Tests.Shared.Services.Implementations
{
    public class ApplicationNavigationServiceTests
    {
        [Fact]
        public void RequestTabSwitch_ShouldRaiseEvent()
        {
            // Arrange
            var service = new ApplicationNavigationService();
            string? requestedTab = null;
            service.TabSwitchRequested += (s, e) => requestedTab = e;

            // Act
            service.RequestTabSwitch("TestTab");

            // Assert
            requestedTab.Should().Be("TestTab");
        }
    }
}
