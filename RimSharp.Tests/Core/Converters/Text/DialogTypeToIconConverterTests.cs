using System;
using System.Globalization;
using FluentAssertions;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Converters.Text;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class DialogTypeToIconConverterTests
    {
        private readonly DialogTypeToIconConverter _converter;

        public DialogTypeToIconConverterTests()
        {
            _converter = new DialogTypeToIconConverter();
        }

        [Theory]
        [InlineData(MessageDialogType.Information, "fa-info")]
        [InlineData(MessageDialogType.Warning, "fa-triangle-exclamation")]
        [InlineData(MessageDialogType.Error, "fa-xmark")]
        [InlineData(MessageDialogType.Question, "fa-question")]
        [InlineData((MessageDialogType)999, "")]
        [InlineData(null, "")]
        public void Convert_ShouldReturnCorrectIcon(object? value, string expected)
        {
            // Act
            var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(expected);
        }
    }
}
