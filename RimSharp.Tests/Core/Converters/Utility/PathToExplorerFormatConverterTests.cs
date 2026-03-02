using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Utility;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Utility
{
    public class PathToExplorerFormatConverterTests
    {
        private readonly PathToExplorerFormatConverter _converter;

        public PathToExplorerFormatConverterTests()
        {
            _converter = new PathToExplorerFormatConverter();
        }

        [Fact]
        public void Convert_ShouldReturnStringRepresentation()
        {

            var result = _converter.Convert(@"C:\Windows\System32", typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be(@"C:\Windows\System32");
        }

        [Fact]
        public void Convert_WithNull_ShouldReturnNull()
        {

            var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().BeNull();
        }
    }
}

