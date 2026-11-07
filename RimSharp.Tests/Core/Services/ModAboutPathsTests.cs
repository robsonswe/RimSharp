using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using RimSharp.Core.Services;
using Xunit;

namespace RimSharp.Tests.Core.Services
{
    public class ModAboutPathsTests
    {
        [Fact]
        public void GetAboutFolder_WhenCanonicalExists_ShouldReturnCanonical()
        {
            var modDir = "Mod";
            var canonical = Path.Combine(modDir, "About");

            ModAboutPaths.GetAboutFolder(modDir, dir => dir == canonical, _ => Array.Empty<string>())
                .Should().Be(canonical);
        }

        [Fact]
        public void GetAboutFolder_WhenOnlyCaseVariantExists_ShouldReturnVariant()
        {
            var modDir = "Mod";
            var variant = Path.Combine(modDir, "about");

            var resolved = ModAboutPaths.GetAboutFolder(
                modDir,
                dir => dir == variant,
                _ => new[] { variant });

            resolved.Should().Be(variant);
        }

        [Fact]
        public void GetAboutFolder_WhenMultipleVariants_ShouldReturnFirstEnumeratedMatch()
        {
            var modDir = "Mod";
            var first = Path.Combine(modDir, "ABOUT");
            var second = Path.Combine(modDir, "aBoUt");

            var resolved = ModAboutPaths.GetAboutFolder(
                modDir,
                dir => dir == first || dir == second,
                _ => new[] { first, second });

            resolved.Should().Be(first);
        }

        [Fact]
        public void GetAboutFolder_WhenNoVariantExists_ShouldReturnCanonicalPath()
        {
            var modDir = "Mod";
            var canonical = Path.Combine(modDir, "About");

            ModAboutPaths.GetAboutFolder(modDir, _ => false, _ => new[] { Path.Combine(modDir, "Assemblies") })
                .Should().Be(canonical);
        }

        [Fact]
        public void GetAboutFilePath_WhenCaseVariantExists_ShouldComposeVariantAboutXml()
        {
            var modDir = "Mod";
            var variant = Path.Combine(modDir, "about");
            var expected = Path.Combine(variant, "About.xml");

            ModAboutPaths.GetAboutFilePath(modDir, dir => dir == variant, _ => new[] { variant })
                .Should().Be(expected);
        }

        [Fact]
        public void GetAboutFilePath_WhenNothingMatches_UsesCanonicalAboutXmlName()
        {
            var expected = Path.Combine("Mod", "About", "About.xml");

            ModAboutPaths.GetAboutFilePath("Mod", _ => false, _ => Enumerable.Empty<string>())
                .Should().Be(expected);
        }
    }
}
