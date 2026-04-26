using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using RimSharp.Infrastructure.Mods.IO;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.IO
{
    public class ModFolderResolverTests : IDisposable
    {
        private readonly string _tempDir;

        public ModFolderResolverTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RimSharpModFolderResolverTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private string CreateModDir(string name)
        {
            var path = Path.Combine(_tempDir, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private void WriteLoadFoldersXml(string modPath, string xml)
        {
            File.WriteAllText(Path.Combine(modPath, "LoadFolders.xml"), xml);
        }

        [Fact]
        public void Resolve_WithNoXmlAndNoVersionDir_ShouldReturnDotAsCurrentFolder()
        {
            var modPath = CreateModDir("SimpleMod");

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain(".");
        }

        [Fact]
        public void Resolve_WithNoXmlAndExactVersionDir_ShouldIncludeVersionDir()
        {
            var modPath = CreateModDir("VersionedMod");
            Directory.CreateDirectory(Path.Combine(modPath, "1.5"));

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain(".");
            result.Current.Should().Contain("1.5");
        }

        [Fact]
        public void Resolve_WithNoXmlAndCommonDir_ShouldIncludeCommon()
        {
            var modPath = CreateModDir("CommonMod");
            Directory.CreateDirectory(Path.Combine(modPath, "Common"));

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain(".");
            result.Current.Should().Contain("Common");
        }

        [Fact]
        public void Resolve_WithXmlMatchingExactVersion_ShouldReturnSpecifiedFolders()
        {
            var modPath = CreateModDir("XmlMod");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li>1.5</li>
  </v1.5>
</loadFolders>");

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain("1.5");
            result.Current.Should().NotContain(".");
        }

        [Fact]
        public void Resolve_WithXmlDefaultVersion_ShouldReturnDefaultFolders()
        {
            var modPath = CreateModDir("XmlDefaultMod");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <default>
    <li>.</li>
    <li>Common</li>
  </default>
</loadFolders>");

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain(".");
            result.Current.Should().Contain("Common");
        }

        [Fact]
        public void Resolve_WithXmlAndIfModActiveCondition_WhenModIsActive_ShouldIncludeFolder()
        {
            var modPath = CreateModDir("ConditionalMod");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModActive=""required.mod"">ConditionalFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string> { "required.mod" };

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Current.Should().Contain("ConditionalFolder");
        }

        [Fact]
        public void Resolve_WithXmlAndIfModActiveCondition_WhenModIsNotActive_ShouldExcludeFromCurrent()
        {
            var modPath = CreateModDir("ConditionalModInactive");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModActive=""required.mod"">ConditionalFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string>();

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Current.Should().NotContain("ConditionalFolder");
            result.Max.Should().Contain("ConditionalFolder");
        }

        [Fact]
        public void Resolve_WithXmlAndIfModNotActiveCondition_WhenModIsNotActive_ShouldIncludeFolder()
        {
            var modPath = CreateModDir("NotActiveMod");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModNotActive=""excluded.mod"">CompatFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string>();

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Current.Should().Contain("CompatFolder");
        }

        [Fact]
        public void Resolve_WithXmlAndIfModNotActiveCondition_WhenModIsActive_ShouldExcludeFolder()
        {
            var modPath = CreateModDir("NotActiveModExcluded");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModNotActive=""excluded.mod"">CompatFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string> { "excluded.mod" };

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Current.Should().NotContain("CompatFolder");
        }

        [Fact]
        public void Resolve_WithXmlAndIfModActiveAll_WhenAllModsActive_ShouldIncludeFolder()
        {
            var modPath = CreateModDir("AllActiveMod");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModActiveAll=""mod.a,mod.b"">BothFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string> { "mod.a", "mod.b" };

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Current.Should().Contain("BothFolder");
        }

        [Fact]
        public void Resolve_WithXmlAndIfModActiveAll_WhenOnlyOneModActive_ShouldExcludeFolder()
        {
            var modPath = CreateModDir("PartiallyActive");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModActiveAll=""mod.a,mod.b"">BothFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string> { "mod.a" };

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Current.Should().NotContain("BothFolder");
        }

        [Fact]
        public void Resolve_WithXmlAndIfModNotActiveAll_WhenAllModsActive_ShouldExcludeFolder()
        {
            var modPath = CreateModDir("NotAllActive");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModNotActiveAll=""mod.a,mod.b"">NoneFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string> { "mod.a", "mod.b" };

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Current.Should().NotContain("NoneFolder");
        }

        [Fact]
        public void Resolve_WithDependencies_ShouldPopulateDependenciesList()
        {
            var modPath = CreateModDir("DependencyMod");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li ifModActive=""dep.mod"">DepFolder</li>
  </v1.5>
</loadFolders>");
            var activeMods = new HashSet<string>();

            var result = ModFolderResolver.Resolve(modPath, "1.5", activeMods);

            result.Dependencies.Should().Contain("dep.mod");
        }

        [Fact]
        public void Resolve_WithXmlFallbackToClosestVersion_ShouldUseBestAvailableVersion()
        {
            var modPath = CreateModDir("FallbackVersion");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.4>
    <li>1.4Folder</li>
  </v1.4>
</loadFolders>");

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain("1.4Folder");
        }

        [Fact]
        public void Resolve_WithMalformedXml_ShouldFallBackToDirectoryLogic()
        {
            var modPath = CreateModDir("MalformedXml");
            File.WriteAllText(Path.Combine(modPath, "LoadFolders.xml"), "<<invalid xml>>");

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain(".");
        }

        [Fact]
        public void Resolve_WithLogParameter_ShouldPopulateLog()
        {
            var modPath = CreateModDir("LogMod");
            var log = new StringBuilder();

            ModFolderResolver.Resolve(modPath, "1.5", log: log);

            log.Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Resolve_WithVersionedDirWithVPrefix_ShouldIncludeVersionDir()
        {
            var modPath = CreateModDir("VPrefixMod");
            Directory.CreateDirectory(Path.Combine(modPath, "v1.5"));

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().Contain("v1.5");
        }

        [Fact]
        public void Resolve_WithMultipleFoldersInXml_ShouldReturnAllFolders()
        {
            var modPath = CreateModDir("MultiFolderMod");
            WriteLoadFoldersXml(modPath, @"
<loadFolders>
  <v1.5>
    <li>.</li>
    <li>Common</li>
    <li>1.5</li>
  </v1.5>
</loadFolders>");

            var result = ModFolderResolver.Resolve(modPath, "1.5");

            result.Current.Should().HaveCount(3);
            result.Current.Should().ContainInOrder(".", "Common", "1.5");
        }
    }
}
