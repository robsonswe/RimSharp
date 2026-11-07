using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

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

            var result = await new PlayerLogReader().ReadAsync(_path);

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

            var result = await new PlayerLogReader().ReadAsync(_path);

            result.Text.Should().Be(content);
            result.Text.Should().NotContain("\uFFFD");
            result.BytesRead.Should().Be(new FileInfo(_path).Length);
        }

        [Fact]
        public async Task ReadAsync_MissingFileReturnsNotFound()
        {
            var result = await new PlayerLogReader().ReadAsync(_path);

            result.Found.Should().BeFalse();
            result.Text.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadTail_AppendsOnlyNewBytes()
        {
            const string initial = "first\n";
            File.WriteAllText(_path, initial, new UTF8Encoding(false));
            var reader = new PlayerLogReader();
            var full = await reader.ReadAsync(_path);

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
            var full = await reader.ReadAsync(_path);

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
            var full = await reader.ReadAsync(_path);

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
            var full = await reader.ReadAsync(_path);

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
