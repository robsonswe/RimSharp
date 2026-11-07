using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    public class LogMessageGrouperTests
    {
        [Fact]
        public void Group_IndentedLinesJoinTheLeadMessage()
        {
            var messages = LogMessageGrouper.Group(new[]
            {
                "Error in MyMod: resolving XML",
                "  at Verse.Foo ()",
                "  at RimSharp.Bar ()",
                "Another message"
            });

            messages.Should().HaveCount(2);
            messages[0].Text.Should().Be("Error in MyMod: resolving XML\n  at Verse.Foo ()\n  at RimSharp.Bar ()");
            messages[0].LineCount.Should().Be(3);
            messages[1].Text.Should().Be("Another message");
            messages[1].LineCount.Should().Be(1);
        }

        [Fact]
        public void Group_BlankLinesAreSeparatorsAndDoNotBreakTheMessage()
        {
            var messages = LogMessageGrouper.Group(new[]
            {
                "Error in MyMod",
                "",
                "  at Verse.Foo ()",
                "   "
            });

            messages.Should().HaveCount(1);
            messages[0].Text.Should().Be("Error in MyMod\n  at Verse.Foo ()");
            messages[0].LineCount.Should().Be(2);
        }

        [Fact]
        public void Group_IndentedFirstLineStartsItsOwnMessage()
        {
            // A lead that was evicted or rotated away leaves orphaned frames;
            // they group together instead of vanishing.
            var messages = LogMessageGrouper.Group(new[]
            {
                "  orphaned frame ()",
                "  another frame ()",
                "Real message"
            });

            messages.Should().HaveCount(2);
            messages[0].Text.Should().Be("  orphaned frame ()\n  another frame ()");
            messages[1].Text.Should().Be("Real message");
        }

        [Fact]
        public void Group_TabIndentedLinesAreContinuations()
        {
            var messages = LogMessageGrouper.Group(new[]
            {
                "Wrapped output",
                "\tcontinued here"
            });

            messages.Should().HaveCount(1);
            messages[0].LineCount.Should().Be(2);
        }

        [Fact]
        public void Group_EmptyInputYieldsNoMessages()
        {
            LogMessageGrouper.Group(new string?[] { null, "", "   " }).Should().BeEmpty();
        }
    }
}
