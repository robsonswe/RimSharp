using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Mods.Rules;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Rules
{
    public class RuleProvenanceLabelsTests
    {
        private static ModRuleSourceRegistry CreateRegistry(params ModRuleSourceRecord[] sources)
        {
            // A substitute config service returns null for the registry key, so
            // the constructor falls back to compiled defaults; ReplaceAll then
            // installs exactly the records the test needs (no file I/O).
            var registry = new ModRuleSourceRegistry(Substitute.For<IConfigService>());
            registry.ReplaceAll(sources);
            return registry;
        }

        [Fact]
        public void GetSourceDisplayName_KnownId_ShouldReturnRecordName()
        {
            var registry = CreateRegistry(new ModRuleSourceRecord
            {
                Id = "my-source",
                Name = "My Fancy Rules"
            });

            RuleProvenanceLabels.GetSourceDisplayName("my-source", registry)
                .Should().Be("My Fancy Rules");
        }

        [Fact]
        public void GetSourceDisplayName_KnownIdCaseInsensitive_ShouldReturnRecordName()
        {
            var registry = CreateRegistry(new ModRuleSourceRecord
            {
                Id = "my-source",
                Name = "My Fancy Rules"
            });

            RuleProvenanceLabels.GetSourceDisplayName("MY-SOURCE", registry)
                .Should().Be("My Fancy Rules");
        }

        [Fact]
        public void GetSourceDisplayName_KnownIdWithBlankName_ShouldReturnId()
        {
            var registry = CreateRegistry(new ModRuleSourceRecord
            {
                Id = "my-source",
                Name = "   "
            });

            RuleProvenanceLabels.GetSourceDisplayName("my-source", registry)
                .Should().Be("my-source");
        }

        [Fact]
        public void GetSourceDisplayName_UnknownId_ShouldReturnIdItself()
        {
            var registry = CreateRegistry(new ModRuleSourceRecord
            {
                Id = "other-source",
                Name = "Other"
            });

            RuleProvenanceLabels.GetSourceDisplayName("removed-source", registry)
                .Should().Be("removed-source");
        }

        [Fact]
        public void GetSourceDisplayName_UnknownIdWithNullRegistry_ShouldReturnIdItself()
        {
            RuleProvenanceLabels.GetSourceDisplayName("removed-source", null)
                .Should().Be("removed-source");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetSourceDisplayName_BlankId_ShouldReturnUnknownLabel(string? sourceId)
        {
            var registry = CreateRegistry(new ModRuleSourceRecord
            {
                Id = "my-source",
                Name = "My Fancy Rules"
            });

            RuleProvenanceLabels.GetSourceDisplayName(sourceId, registry)
                .Should().Be(RuleProvenanceLabels.UnknownLabel);
        }

        [Fact]
        public void GetSourceDisplayName_BlankIdWithNullRegistry_ShouldReturnUnknownLabel()
        {
            RuleProvenanceLabels.GetSourceDisplayName(null, null)
                .Should().Be(RuleProvenanceLabels.UnknownLabel);
        }
    }
}
