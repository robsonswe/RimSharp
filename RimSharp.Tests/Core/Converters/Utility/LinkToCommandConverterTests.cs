using System;
using System.Globalization;
using System.Windows.Input;
using FluentAssertions;
using RimSharp.Core.Converters.Utility;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Utility
{
    public class LinkToCommandConverterTests
    {
        private readonly LinkToCommandConverter _converter;

        public LinkToCommandConverterTests()
        {
            _converter = new LinkToCommandConverter();
        }

        [Fact]
        public void Convert_WithString_ShouldReturnCommand()
        {
            // Act
            var result = _converter.Convert("https://google.com", typeof(ICommand), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeAssignableTo<ICommand>();
        }

        [Fact]
        public void Convert_WithNull_ShouldReturnNull()
        {
            // Act
            var result = _converter.Convert(null, typeof(ICommand), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeNull();
        }
    }
}
