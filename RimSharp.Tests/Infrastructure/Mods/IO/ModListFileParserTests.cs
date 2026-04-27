using System;
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

        [Fact]
        public void Parse_WhenDocumentIsNull_ShouldThrowArgumentNullException()
        {

            Action act = () => _parser.Parse(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Generate_WhenIdsIsNull_ShouldThrowArgumentNullException()
        {

            Action act = () => _parser.Generate(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Parse_WhenActiveModsElementMissing_ShouldReturnEmpty()
        {

            var doc = XDocument.Parse("<ModsConfigData></ModsConfigData>");

            var result = _parser.Parse(doc);

            result.Should().BeEmpty();
        }

        [Fact]
        public void Parse_ShouldIgnoreEmptyAndWhitespaceLiElements()
        {

            var xml = @"<ModsConfigData>
                <activeMods>
                    <li>Ludeon.RimWorld</li>
                    <li>   </li>
                    <li></li>
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
        public void Parse_ShouldNormalizePackageIdsToLowercase()
        {

            var xml = @"<ModsConfigData>
                <activeMods>
                    <li>Author.ModName</li>
                </activeMods>
            </ModsConfigData>";
            var doc = XDocument.Parse(xml);

            var result = _parser.Parse(doc);

            result.Should().ContainSingle().Which.Should().Be("author.modname");
        }

        [Fact]
        public void Generate_WithEmptyList_ShouldReturnDocumentWithEmptyActiveMods()
        {

            var doc = _parser.Generate(new List<string>());

            var listItems = doc.Root?.Element("activeMods")?.Elements("li").ToList();
            listItems.Should().NotBeNull();
            listItems!.Should().BeEmpty();
        }

        [Fact]
        public void Generate_ShouldNormalizeToLowercase()
        {

            var ids = new List<string> { "Author.MOD" };

            var doc = _parser.Generate(ids);

            var value = doc.Root?.Element("activeMods")?.Elements("li").First().Value;
            value.Should().Be("author.mod");
        }

        [Fact]
        public void Generate_ShouldSkipNullAndEmptyEntries()
        {

            var ids = new List<string?> { "mod1", null, "", "mod2" }!;

            var doc = _parser.Generate(ids!);

            var listItems = doc.Root?.Element("activeMods")?.Elements("li").ToList();
            listItems.Should().HaveCount(2);
        }
    }
}

