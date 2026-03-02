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
        public void Parse_ShouldReturnCorrectIds()
        {

            var xml = @"<ModsConfigData>
                <activeMods>
                    <li>Ludeon.RimWorld</li>
                    <li>brrainz.harmony</li>
                </activeMods>
            </ModsConfigData>";
            var doc = XDocument.Parse(xml);

            var result = _parser.Parse(doc);

            result.Should().HaveCount(2);
            result.Should().Contain("ludeon.rimworld");
            result.Should().Contain("brrainz.harmony");
        }

        [Fact]
        public void Generate_ShouldReturnCorrectXml()
        {

            var ids = new List<string> { "Ludeon.RimWorld", "brrainz.harmony" };

            var doc = _parser.Generate(ids);

            var listItems = doc.Root?.Element("activeMods")?.Elements("li").ToList();
            listItems.Should().NotBeNull();
            listItems!.Should().HaveCount(2);
            listItems![0].Value.Should().Be("ludeon.rimworld");
            listItems![1].Value.Should().Be("brrainz.harmony");
        }
    }
}

