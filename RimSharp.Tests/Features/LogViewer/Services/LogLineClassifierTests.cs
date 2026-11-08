using System.Collections.Generic;
using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    public class LogLineClassifierTests
    {
        [Theory]
        [InlineData("11:04:37 PM Initializing MonoManager.", LogLineSeverity.Info)]
        [InlineData("Loaded 4 mods with warnings", LogLineSeverity.Info)]
        [InlineData("Key binding conflict: A vs B", LogLineSeverity.Keybind)]
        [InlineData("[MyMod] Error while resolving XML", LogLineSeverity.Error)]
        [InlineData("Warning: deprecated API in use", LogLineSeverity.Warning)]
        [InlineData("[E] Failed to load texture", LogLineSeverity.Error)]
        [InlineData("Unhandled exception in RimWorld.Mod", LogLineSeverity.Exception)]
        [InlineData("   at Verse.Awake ()", LogLineSeverity.Exception)]
        [InlineData("Plain log line with nothing special", LogLineSeverity.None)]
        public void Classify_AssignsExpectedSeverity(string line, LogLineSeverity expected)
        {
            LogLineClassifier.Classify(line).Severity.Should().Be(expected);
        }

        [Fact]
        public void Classify_CountsCategoriesNonExclusively()
        {
            // A bracketed mod line with an error is both a mod issue and an error.
            var classification = LogLineClassifier.Classify("[MyMod] Error while resolving XML");

            classification.IsModIssue.Should().BeTrue();
            classification.IsError.Should().BeTrue();
            classification.Severity.Should().Be(LogLineSeverity.Error);
        }

        [Fact]
        public void Classify_ExceptionWinsOverErrorOnSameLine()
        {
            var classification = LogLineClassifier.Classify("Exception: error while patching");

            classification.IsException.Should().BeTrue();
            classification.IsError.Should().BeTrue();
            classification.Severity.Should().Be(LogLineSeverity.Exception);
        }

        [Fact]
        public void ClassifyMessage_MembershipSpansTheWholeMessage()
        {
            // The exception only appears on the continuation line; the message
            // as a whole must count as an exception.
            var classification = LogLineClassifier.ClassifyMessage("Loading scene\n  UnityException: boom");

            classification.IsException.Should().BeTrue();
            classification.Severity.Should().Be(LogLineSeverity.Exception);
        }

        [Fact]
        public void ClassifyMessage_SeverityPrefersTheLeadLine()
        {
            // A Warning whose body is a stack trace stays warning-colored; the
            // frames still mark the message as a stack trace.
            var classification = LogLineClassifier.ClassifyMessage("Warning: something\n  at Verse.Foo ()");

            classification.IsStackTrace.Should().BeTrue();
            classification.Severity.Should().Be(LogLineSeverity.Warning);
        }

        [Fact]
        public void ClassifyMessage_ExceptionLeadKeepsExceptionSeverity()
        {
            var classification = LogLineClassifier.ClassifyMessage("NullReferenceException: x\n  at Verse.Awake ()");

            classification.Severity.Should().Be(LogLineSeverity.Exception);
        }

        [Fact]
        public void Analyze_AccumulatesCountersPerCategory()
        {
            var stats = LogLineClassifier.Analyze(new[]
            {
                "11:04:37 PM Loaded mods",
                "Warning: one",
                "Warning: two",
                "[E] Failed",
                "[MyMod] Error while resolving",
                "nothing here"
            });

            stats.TotalLines.Should().Be(6);
            stats.Infos.Should().Be(1);
            stats.Warnings.Should().Be(2);
            stats.Errors.Should().Be(2);
            stats.ModIssues.Should().Be(1);
            // All Issues counts lines, not bucket membership: the four issue
            // lines count once each even though one of them is both an error
            // and a mod issue.
            stats.AllIssues.Should().Be(4);
        }

        [Theory]
        [InlineData(LogFilterMode.InfosOnly, "11:04:37 PM Loaded mods", true)]
        [InlineData(LogFilterMode.InfosOnly, "Warning: x", false)]
        [InlineData(LogFilterMode.WarningsOnly, "Warning: x", true)]
        [InlineData(LogFilterMode.ErrorsOnly, "[E] Failed", true)]
        [InlineData(LogFilterMode.ErrorsOnly, "Warning: x", false)]
        [InlineData(LogFilterMode.ExceptionsOnly, "Unhandled exception", true)]
        [InlineData(LogFilterMode.ExceptionsOnly, "   at Verse.Awake ()", false)]
        [InlineData(LogFilterMode.KeybindsOnly, "Key binding conflict", true)]
        [InlineData(LogFilterMode.ModIssues, "[MyMod] Error while resolving", true)]
        [InlineData(LogFilterMode.AllIssues, "Warning: x", true)]
        [InlineData(LogFilterMode.AllIssues, "11:04:37 PM Loaded", false)]
        [InlineData(LogFilterMode.AllEntries, "anything", true)]
        public void MatchesFilter_SelectsByCategory(LogFilterMode mode, string line, bool expected)
        {
            LogLineClassifier.MatchesFilter(LogLineClassifier.Classify(line), mode).Should().Be(expected);
        }

        [Fact]
        public void MatchesTextFilter_ComposesWithModeFilter()
        {
            var classification = LogLineClassifier.Classify("[MyMod] Error while resolving");

            LogLineClassifier.MatchesTextFilter("[MyMod] Error while resolving", "mymod").Should().BeTrue();
            LogLineClassifier.MatchesTextFilter("[MyMod] Error while resolving", "othermod").Should().BeFalse();
            LogLineClassifier.MatchesTextFilter("[MyMod] Error while resolving", "   ").Should().BeTrue();
        }

        [Fact]
        public void CollapseRepeated_CollapsesConsecutiveDuplicatesOnly()
        {
            var collapsed = LogLineClassifier.CollapseRepeated(new[]
            {
                "same",
                "same",
                "same",
                "other",
                "same"
            });

            collapsed.Should().Equal("same (Repeated 3 times)", "other", "same");
        }

        [Fact]
        public void CollapseRepeated_EmptyInputYieldsEmptyList()
        {
            LogLineClassifier.CollapseRepeated(new List<string?>()).Should().BeEmpty();
        }
    }
}
