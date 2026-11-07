using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    public class LogContentStoreTests
    {
        [Fact]
        public void AppendText_SplitsLinesAndNormalizesTerminators()
        {
            var store = new LogContentStore();

            store.AppendText("first\r\nsecond\nthird\rfourth");

            store.LineCount.Should().Be(4);
            store.Lines[0].Should().Be("first");
            store.Lines[1].Should().Be("second");
            store.Lines[2].Should().Be("third");
            store.Lines[3].Should().Be("fourth");
        }

        [Fact]
        public void AppendText_MultipleCallsKeepAppending()
        {
            var store = new LogContentStore();

            store.AppendText("one\ntwo\n");
            store.AppendText("three\n");

            store.LineCount.Should().Be(3); // trailing newline does not add an empty line
            store.IsEmpty.Should().BeFalse();
        }

        [Fact]
        public void Clear_ResetsEverything()
        {
            var store = new LogContentStore();
            store.AppendText("one\ntwo\n");

            store.Clear();

            store.LineCount.Should().Be(0);
            store.TotalBytes.Should().Be(0);
            store.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void AppendText_EvictsOldestLinesBeyondCap()
        {
            // Same eviction rule as production, with a tiny cap so the test stays fast.
            var store = new LogContentStore(maxTotalBytes: 1000);

            store.AppendText(new string('a', 900) + "\n");
            store.AppendText(new string('b', 900) + "\n");

            store.TotalBytes.Should().BeLessThanOrEqualTo(1000);
            store.LineCount.Should().Be(1);
            store.Lines[0].Should().Be(new string('b', 900));
        }
    }
}
