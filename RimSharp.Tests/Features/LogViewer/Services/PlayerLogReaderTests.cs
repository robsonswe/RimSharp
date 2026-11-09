using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;
using Xunit.v3;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    public class PlayerLogReaderTests : System.IDisposable
    {
        private readonly string _path;

        public PlayerLogReaderTests()
        {
            _path = Path.Combine(Path.GetTempPath(), "rimsharp-reader-" + Path.GetRandomFileName() + ".log");
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best effort */ }
        }

        [Fact]
        public async Task ReadAsync_ReturnsContentAndExactByteCount()
        {
            File.WriteAllText(_path, "line one\nline two\nline three\n", new UTF8Encoding(false));

            var result = await new PlayerLogReader().ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            result.Found.Should().BeTrue();
            result.Text.Should().Be("line one\nline two\nline three\n");
            result.BytesRead.Should().Be(new FileInfo(_path).Length);
            result.FileSize.Should().Be(new FileInfo(_path).Length);
        }

        [Fact]
        public async Task ReadAsync_MultiByteContentRoundTripsWithoutReplacementChars()
        {
            const string content = "日本語のログ\n二行目\n";
            File.WriteAllText(_path, content, new UTF8Encoding(false));

            var result = await new PlayerLogReader().ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            result.Text.Should().Be(content);
            result.Text.Should().NotContain("\uFFFD");
            result.BytesRead.Should().Be(new FileInfo(_path).Length);
        }

        [Fact]
        public async Task ReadAsync_MissingFileReturnsNotFound()
        {
            var result = await new PlayerLogReader().ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            result.Found.Should().BeFalse();
            result.Text.Should().BeEmpty();
        }

        // ── Streaming read (bounded memory for huge logs) ──────────────────

        [Fact]
        public async Task ReadIntoStoreAsync_MatchesAWholeFileReadLineForLine()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 4000; i++)
            {
                builder.Append("2026-11-07 18:31:56.481 [Verse] message ").Append(i);
                builder.Append(i % 97 == 0 ? " 日本語の行\n" : " plain tail\n");
            }

            File.WriteAllText(_path, builder.ToString(), new UTF8Encoding(false));
            var expected = await new PlayerLogReader().ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            var store = new LogContentStore();
            var result = await new PlayerLogReader().ReadIntoStoreAsync(_path, store, cancellationToken: TestContext.Current.CancellationToken);

            result.Found.Should().BeTrue();
            result.FileSize.Should().Be(new FileInfo(_path).Length);
            store.Lines.Should().Equal(expected.Text.TrimEnd('\n').Split('\n'));
            store.LinesEvicted.Should().Be(0); // 300 KB fits under the cap
            store.FirstRetainedLineNumber.Should().Be(1);
            store.Lines.Should().NotContain(l => l.Contains('\uFFFD'));
        }

        [Fact]
        public async Task ReadIntoStoreAsync_KeepsAMultiByteCharStraddlingTheChunkBoundary()
        {
            // The read buffer is 81920 bytes: place a three-byte character so
            // it starts at 81919 and is split across two chunk reads. A naive
            // chunk decode would corrupt it into a replacement character.
            var line = new string('a', 81919) + "→ tail\n";
            var content = line + "second line\n";
            File.WriteAllText(_path, content, new UTF8Encoding(false));

            var store = new LogContentStore();
            var result = await new PlayerLogReader().ReadIntoStoreAsync(_path, store, cancellationToken: TestContext.Current.CancellationToken);

            result.Found.Should().BeTrue();
            store.Lines.Should().HaveCount(2);
            store.Lines[0].Should().Be(new string('a', 81919) + "→ tail");
            store.Lines[1].Should().Be("second line");
            store.Lines.Should().NotContain(l => l.Contains('\uFFFD'));
        }

        [Fact]
        public async Task ReadIntoStoreAsync_KeepsAFinalUnterminatedLine()
        {
            File.WriteAllText(_path, "first\nsecond without terminator", new UTF8Encoding(false));

            var store = new LogContentStore();
            await new PlayerLogReader().ReadIntoStoreAsync(_path, store, cancellationToken: TestContext.Current.CancellationToken);

            store.Lines.Should().Equal("first", "second without terminator");
        }

        [Fact]
        public async Task ReadIntoStoreAsync_EvictsOverTheCapWithExactNumbering()
        {
            // Multi-megabyte content through a kilobyte-scale store: eviction
            // must count every appended line, exactly as a whole-file read
            // would, so the diagnostics' head+window merge keeps working.
            var builder = new StringBuilder();
            for (var i = 0; i < 20000; i++)
            {
                builder.Append("line ").Append(i).Append('\n');
            }

            File.WriteAllText(_path, builder.ToString(), new UTF8Encoding(false));

            var store = new LogContentStore(maxTotalBytes: 4096);
            var result = await new PlayerLogReader().ReadIntoStoreAsync(_path, store, cancellationToken: TestContext.Current.CancellationToken);

            result.Found.Should().BeTrue();
            store.LinesEvicted.Should().BeGreaterThan(0);
            store.FirstRetainedLineNumber.Should().Be(store.LinesEvicted + 1);
            store.TotalBytes.Should().BeLessThan(4096 + 64);
            store.Lines[^1].Should().Be("line 19999");
            store.Lines[0].Should().StartWith("line ");
        }

        [Fact]
        public async Task ReadIntoStoreAsync_MissingFileReturnsNotFound()
        {
            var store = new LogContentStore();
            var result = await new PlayerLogReader().ReadIntoStoreAsync(_path, store, cancellationToken: TestContext.Current.CancellationToken);

            result.Found.Should().BeFalse();
            store.Lines.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadTail_AppendsOnlyNewBytes()
        {
            const string initial = "first\n";
            File.WriteAllText(_path, initial, new UTF8Encoding(false));
            var reader = new PlayerLogReader();
            var full = await reader.ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            File.AppendAllText(_path, "second\nthird\n", new UTF8Encoding(false));
            var tail = reader.ReadTail(_path, full.BytesRead);

            tail.Reset.Should().BeFalse();
            tail.NewText.Should().Be("second\nthird\n");
            tail.NewOffset.Should().Be(new FileInfo(_path).Length);
        }

        [Fact]
        public async Task ReadTail_NoNewContentReturnsEmpty()
        {
            File.WriteAllText(_path, "only line\n", new UTF8Encoding(false));
            var reader = new PlayerLogReader();
            var full = await reader.ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            var tail = reader.ReadTail(_path, full.BytesRead);

            tail.Reset.Should().BeFalse();
            tail.NewText.Should().BeEmpty();
            tail.NewOffset.Should().Be(full.BytesRead);
        }

        [Fact]
        public async Task ReadTail_MultiByteBoundaryDoesNotDuplicateOrDropCharacters()
        {
            // Write multi-byte content, tail from the exact anchor returned by the
            // full read: the decoder must resume cleanly at a char boundary.
            const string initial = "日本語\n";
            File.WriteAllText(_path, initial, new UTF8Encoding(false));
            var reader = new PlayerLogReader();
            var full = await reader.ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            File.AppendAllText(_path, "二行目\n", new UTF8Encoding(false));
            var tail = reader.ReadTail(_path, full.BytesRead);

            tail.NewText.Should().Be("二行目\n");
            tail.NewText.Should().NotContain("\uFFFD");
        }

        [Fact]
        public async Task ReadTail_ShrunkFileSignalsReset()
        {
            File.WriteAllText(_path, "a fairly long initial log line\n", new UTF8Encoding(false));
            var reader = new PlayerLogReader();
            var full = await reader.ReadAsync(_path, cancellationToken: TestContext.Current.CancellationToken);

            File.WriteAllText(_path, "short\n", new UTF8Encoding(false));

            var tail = reader.ReadTail(_path, full.BytesRead);

            tail.Reset.Should().BeTrue();
        }

        [Fact]
        public void ReadTail_MonoPathHeaderSignalsReset()
        {
            // A fresh session rewrites the log from the top with the Mono header.
            File.WriteAllText(_path, "Mono path[0] = 'C:/RimWorld/RimWorldWin64_Data/Managed'\n", new UTF8Encoding(false));

            var tail = new PlayerLogReader().ReadTail(_path, fromOffset: 0);

            tail.Reset.Should().BeTrue();
        }

        [Fact]
        public void ReadTail_MissingFileReturnsNoReset()
        {
            var tail = new PlayerLogReader().ReadTail(_path, fromOffset: 42);

            tail.Reset.Should().BeFalse();
            tail.NewText.Should().BeEmpty();
        }
    }
}
