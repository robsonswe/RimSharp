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
        public void AppendText_NonFinalChunkHoldsPartialLineUntilTerminated()
        {
            var store = new LogContentStore();

            store.AppendText("one\ntwo", finalChunk: false);

            store.LineCount.Should().Be(1);
            store.Lines[0].Should().Be("one");

            // The remainder of the split line merges with the next chunk.
            store.AppendText("three\n", finalChunk: false);

            store.LineCount.Should().Be(2);
            store.Lines[1].Should().Be("twothree");
        }

        [Fact]
        public void AppendText_FinalChunkFlushesThePendingLine()
        {
            var store = new LogContentStore();

            store.AppendText("one\ntwo", finalChunk: false);
            store.AppendText("three", finalChunk: true);

            store.LineCount.Should().Be(2);
            store.Lines[1].Should().Be("twothree");
        }

        [Fact]
        public void AppendText_EmptyNonFinalChunkKeepsPendingLine()
        {
            var store = new LogContentStore();

            store.AppendText("partial", finalChunk: false);
            store.AppendText(string.Empty, finalChunk: false);

            store.LineCount.Should().Be(0);
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

        [Fact]
        public void AppendText_EvictionKeepsExactCountsAcrossManyCompactions()
        {
            // The dead prefix compacts in batches; the evicted count and the
            // retained window's first line number must stay exact regardless,
            // because the diagnostics merge the head read with the store by
            // file line number. A 1000-byte cap forces hundreds of compactions.
            var store = new LogContentStore(maxTotalBytes: 1000);
            for (var i = 0; i < 20000; i++)
            {
                store.AppendText($"line {i}\n");
            }

            (store.LinesEvicted + store.LineCount).Should().Be(20000);
            store.FirstRetainedLineNumber.Should().Be(store.LinesEvicted + 1);
            store.TotalBytes.Should().BeLessThanOrEqualTo(1000);
            store.Lines[^1].Should().Be("line 19999");
        }

        [Fact]
        public void Clear_ResetsEvictionSoAReloadNumbersFromTheFirstLine()
        {
            // Regression: a reload (source switch, refresh) reused the previous
            // file's eviction count, numbering the retained window past the
            // head read and leaving a hole in the diagnostics' line numbers.
            var store = new LogContentStore(maxTotalBytes: 1000);
            store.AppendText(new string('a', 900) + "\n");
            store.AppendText(new string('b', 900) + "\n");
            store.LinesEvicted.Should().Be(1);

            store.Clear();
            store.AppendText("fresh line\nsecond line\n");

            store.LinesEvicted.Should().Be(0);
            store.FirstRetainedLineNumber.Should().Be(1);
            store.Lines.Should().Equal("fresh line", "second line");
        }
    }
}
