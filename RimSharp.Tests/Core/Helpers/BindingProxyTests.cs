using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Core.Helpers;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Core.Helpers
{
    public class BindingProxyTests
    {
        public BindingProxyTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void DataProperty_ShouldStoreValue()
        {
            // Arrange
            var proxy = new BindingProxy();
            var data = new { Name = "Test" };

            // Act
            proxy.Data = data;

            // Assert
            proxy.Data.Should().Be(data);
        }
    }
}
