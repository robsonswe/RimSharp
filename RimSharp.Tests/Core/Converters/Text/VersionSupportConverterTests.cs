using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using FluentAssertions;
using RimSharp.Core.Converters.Text;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Core.Converters.Text
{
    public class VersionSupportConverterTests
    {
        private readonly VersionSupportConverter _converter;

        public VersionSupportConverterTests()
        {
            _converter = new VersionSupportConverter();
        }

        [Fact]
        public void Convert_WithNullOrEmpty_ShouldReturnNone()
        {

            var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("None");
        }

        [Fact]
        public void Convert_ToString_ShouldReturnCommaSeparatedList()
        {

            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.4", VersionSource.Official),
                new VersionSupport("1.5", VersionSource.External, true)
            };

            var result = _converter.Convert(versions, typeof(string), null, CultureInfo.InvariantCulture);

            result.Should().Be("1.4, 1.5");
        }

        [Fact]
        public void Convert_ToString_WithOfficialOnlyParameter_ShouldFilterCorrectly()
        {

            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.4", VersionSource.Official),
                new VersionSupport("1.5", VersionSource.External, true)
            };

            var result = _converter.Convert(versions, typeof(string), "OfficialOnly", CultureInfo.InvariantCulture);

            result.Should().Be("1.4");
        }

        [Fact]
        public void Convert_ToString_WithNoMatches_ShouldReturnNone()
        {

            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.5", VersionSource.External, true)
            };

            var result = _converter.Convert(versions, typeof(string), "OfficialOnly", CultureInfo.InvariantCulture);

            result.Should().Be("None");
        }

        [Fact]
        public void Convert_ToPanel_ExternalWithSourceName_ShouldNameSourceInTooltip()
        {
            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.5", VersionSource.External, true, "Community NVW")
            };

            var panel = _converter.Convert(versions, typeof(WrapPanel), null, CultureInfo.InvariantCulture) as WrapPanel;
            var textBlock = panel!.Children.OfType<TextBlock>().Single();

            ToolTip.GetTip(textBlock).Should().Be("Unofficial: Compatibility added via Community NVW.");
        }

        [Fact]
        public void Convert_ToPanel_ExternalWithoutSourceName_ShouldUseFallbackTooltip()
        {
            var versions = new List<VersionSupport>
            {
                new VersionSupport("1.5", VersionSource.External, true)
            };

            var panel = _converter.Convert(versions, typeof(WrapPanel), null, CultureInfo.InvariantCulture) as WrapPanel;
            var textBlock = panel!.Children.OfType<TextBlock>().Single();

            ToolTip.GetTip(textBlock).Should().Be("Unofficial: Compatibility added via an external source.");
        }
    }
}
