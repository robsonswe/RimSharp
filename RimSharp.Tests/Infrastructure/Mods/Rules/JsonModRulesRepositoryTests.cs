using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Rules
{
    public class JsonModRulesRepositoryTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _rulesFilePath;
        private readonly IDataUpdateService _mockDataUpdateService;

        public JsonModRulesRepositoryTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "RimSharpRulesTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _rulesFilePath = Path.Combine(_tempDir, "rules.json");

            _mockDataUpdateService = Substitute.For<IDataUpdateService>();
            _mockDataUpdateService.GetDataFilePath("rules.json").Returns(_rulesFilePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void Constructor_WhenDataUpdateServiceIsNull_ShouldThrowArgumentNullException()
        {
            Action act = () => new JsonModRulesRepository(null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("dataUpdateService");
        }

        [Fact]
        public void GetAllRules_WhenFileDoesNotExist_ShouldReturnEmptyDictionary()
        {
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rules = repo.GetAllRules();

            rules.Should().BeEmpty();
        }

        [Fact]
        public void GetAllRules_WhenFileIsValid_ShouldReturnParsedRules()
        {
            var json = @"{
  ""rules"": {
    ""mod.a"": {
      ""loadBefore"": { ""mod.b"": { ""name"": [""Mod B""] } },
      ""loadAfter"": {},
      ""incompatibilities"": {}
    }
  }
}";
            File.WriteAllText(_rulesFilePath, json);
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rules = repo.GetAllRules();

            rules.Should().ContainKey("mod.a");
            rules["mod.a"].LoadBefore.Should().ContainKey("mod.b");
        }

        [Fact]
        public void GetAllRules_WhenFileHasInvalidJson_ShouldReturnEmptyDictionary()
        {
            File.WriteAllText(_rulesFilePath, "{ this is not valid json }");
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rules = repo.GetAllRules();

            rules.Should().BeEmpty();
        }

        [Fact]
        public void GetAllRules_WhenJsonHasNoRulesKey_ShouldReturnEmptyDictionary()
        {
            File.WriteAllText(_rulesFilePath, "{}");
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rules = repo.GetAllRules();

            rules.Should().BeEmpty();
        }

        [Fact]
        public void GetAllRules_ShouldCacheResults_OnSecondCall()
        {
            var json = @"{ ""rules"": { ""mod.a"": {} } }";
            File.WriteAllText(_rulesFilePath, json);
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var first = repo.GetAllRules();
            File.WriteAllText(_rulesFilePath, "{}"); // Change file after first call
            var second = repo.GetAllRules();

            second.Should().BeSameAs(first);
        }

        [Fact]
        public void GetRulesForMod_WhenRuleExists_ShouldReturnRule()
        {
            var json = @"{
  ""rules"": {
    ""mod.a"": {
      ""loadBefore"": { ""mod.b"": {} },
      ""loadAfter"": {},
      ""incompatibilities"": {}
    }
  }
}";
            File.WriteAllText(_rulesFilePath, json);
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rule = repo.GetRulesForMod("mod.a");

            rule.LoadBefore.Should().ContainKey("mod.b");
        }

        [Fact]
        public void GetRulesForMod_WhenRuleDoesNotExist_ShouldReturnEmptyRule()
        {
            File.WriteAllText(_rulesFilePath, @"{ ""rules"": {} }");
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rule = repo.GetRulesForMod("nonexistent.mod");

            rule.Should().NotBeNull();
            rule.LoadBefore.Should().BeEmpty();
            rule.LoadAfter.Should().BeEmpty();
            rule.Incompatibilities.Should().BeEmpty();
        }

        [Fact]
        public void GetRulesForMod_WhenPackageIdIsEmpty_ShouldReturnEmptyRule()
        {
            File.WriteAllText(_rulesFilePath, @"{ ""rules"": { """": {} } }");
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rule = repo.GetRulesForMod(string.Empty);

            rule.Should().NotBeNull();
            rule.LoadBefore.Should().BeEmpty();
        }

        [Fact]
        public void GetRulesForMod_IsCaseInsensitive()
        {
            var json = @"{ ""rules"": { ""Mod.A"": { ""loadBefore"": { ""mod.b"": {} }, ""loadAfter"": {}, ""incompatibilities"": {} } } }";
            File.WriteAllText(_rulesFilePath, json);
            var repo = new JsonModRulesRepository(_mockDataUpdateService);

            var rule = repo.GetRulesForMod("mod.a");

            rule.LoadBefore.Should().ContainKey("mod.b");
        }
    }
}
