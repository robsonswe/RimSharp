using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using RimSharp.Infrastructure.Mods.IO;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.IO
{
    public class ModListFileParserTests
    {
        private readonly ModListFileParser _parser;

        public ModListFileParserTests()
        {
            _parser = new ModListFileParser();
        }

        [Fact]
        public void Parse_ShouldReturnPackageIds()
        {
            // Arrange
            var xml = @"
<ModsConfigData>
    <version>1.0</version>
    <activeMods>
        <li>brrainz.harmony</li>
        <li>Ludeon.RimWorld</li>
    </activeMods>
</ModsConfigData>";
            var doc = XDocument.Parse(xml);

            // Act
            var result = _parser.Parse(doc);

            // Assert
            result.Should().HaveCount(2);
            result.Should().ContainInOrder("brrainz.harmony", "ludeon.rimworld");
        }

        [Fact]
        public void Parse_WhenMissingActiveModsElement_ShouldReturnEmptyList()
        {
            // Arrange
            var xml = @"<ModsConfigData><version>1.0</version></ModsConfigData>";
            var doc = XDocument.Parse(xml);

            // Act
            var result = _parser.Parse(doc);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void Generate_ShouldCreateCorrectXml()
        {
            // Arrange
            var ids = new[] { "mod1", "MOD2" };

            // Act
            var doc = _parser.Generate(ids);

            // Assert
            doc.Root.Should().NotBeNull();
            doc.Root!.Name.LocalName.Should().Be("ModsConfigData");
            
            var liElements = doc.Root.Element("activeMods")?.Elements("li").ToList();
            liElements.Should().NotBeNull().And.HaveCount(2);
            liElements![0].Value.Should().Be("mod1");
            liElements![1].Value.Should().Be("mod2"); // Normalized to lower
        }
    }
}
