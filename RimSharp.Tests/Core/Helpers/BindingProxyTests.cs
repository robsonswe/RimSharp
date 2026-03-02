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

            var proxy = new BindingProxy();
            var data = new { Name = "Test" };

            proxy.Data = data;

            proxy.Data.Should().Be(data);
        }
    }
}

