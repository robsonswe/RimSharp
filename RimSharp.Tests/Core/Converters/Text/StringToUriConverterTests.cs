using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class StringToUriConverterTests
    {
        private readonly StringToUriConverter _converter;

        public StringToUriConverterTests()
        {
            _converter = new StringToUriConverter();
        }

        [Theory]
        [InlineData("https://google.com", "https://google.com/")]
        [InlineData("http://test.org/path", "http://test.org/path")]
        [InlineData("invalid-url", null)]
        [InlineData("ftp://google.com", null)]
        [InlineData(null, null)]
        [InlineData(123, null)]
        public void Convert_ShouldReturnCorrectUri(object? value, string? expectedUri)
        {
            // Act
            var result = _converter.Convert(value, typeof(Uri), null, CultureInfo.InvariantCulture);

            // Assert
            if (expectedUri == null)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().BeOfType<Uri>();
                ((Uri)result!).AbsoluteUri.Should().Be(expectedUri);
            }
        }
    }
}
