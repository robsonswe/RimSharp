using System;
using System.IO;
using FluentAssertions;
using RimSharp.Infrastructure.Workshop;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Workshop
{
    /// <summary>
    /// ACF parser tests against the committed trimmed sample file
    /// (RimSharp.Tests\TestData\acf_sample_294100.acf) plus inline edge cases.
    /// </summary>
    public class AcfFileParserTests : IDisposable
    {
        private readonly AcfFileParser _parser = new();
        private readonly string _testTempDir;
        private readonly string _sampleAcfPath;

        public AcfFileParserTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
            _sampleAcfPath = GetTestDataFilePath("acf_sample_294100.acf");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        /// <summary>Walks up from the test base directory until the TestData folder appears.</summary>
        private static string GetTestDataFilePath(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "TestData", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }

            throw new FileNotFoundException($"TestData file '{fileName}' was not found in any parent directory.");
        }

        [Fact]
        public void TryParse_CommittedSample_ShouldSucceedWithAllItems()
        {
            var ok = _parser.TryParse(_sampleAcfPath, out var data, out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            data.Should().NotBeNull();
            data!.Items.Should().HaveCount(4);
        }

        [Fact]
        public void TryParse_ItemWithDetails_ShouldPreferDetailsTimeUpdated()
        {
            _parser.TryParse(_sampleAcfPath, out var data, out _);

            var item = data!.Items["2009463077"];
            item.TimeUpdated.Should().Be(1660080000);      // details value wins...
            item.ResolvedTimeUpdated.Should().Be(1660080000);
            item.TimeTouched.Should().Be(1699955428);
            item.SourceId.Should().Be(1660069393);
        }

        [Fact]
        public void TryParse_ItemWithZeroedDetails_ShouldFallBackToInstalledStamp()
        {
            _parser.TryParse(_sampleAcfPath, out var data, out _);

            var item = data!.Items["2535924806"];
            item.TimeUpdated.Should().Be(0);                        // details are zeroed...
            item.ResolvedTimeUpdated.Should().Be(1708950034);       // ...installed record is the fallback
        }

        [Fact]
        public void TryParse_InstalledOnlyItem_ShouldUseInstalledRecord()
        {
            // "1127840558" is in WorkshopItemsInstalled but has no WorkshopItemDetails entry
            _parser.TryParse(_sampleAcfPath, out var data, out _);

            var item = data!.Items["1127840558"];
            item.TimeUpdated.Should().Be(0);
            item.TimeTouched.Should().Be(0);
            item.ResolvedTimeUpdated.Should().Be(1690100000);
        }

        [Fact]
        public void TryParse_EscapedQuotesInValues_ShouldBeUnescaped()
        {
            _parser.TryParse(_sampleAcfPath, out var data, out _);

            var item = data!.Items["2535924806"];
            item.RawValues.Should().NotBeNull();
            item.RawValues!["local"].Should().Be("\"staging\" build pinned by user");
        }

        [Fact]
        public void TryParse_WhenFileMissing_ShouldFailWithReason()
        {
            var ok = _parser.TryParse(
                Path.Combine(_testTempDir, "no_such_acf.acf"), out var data, out var error);

            ok.Should().BeFalse();
            error.Should().Contain("does not exist");
            data.Should().NotBeNull();
            data!.Items.Should().BeEmpty();
        }

        [Fact]
        public void TryParse_WhenPathNull_ShouldFail()
        {
            var ok = _parser.TryParse(string.Empty, out var data, out var error);

            ok.Should().BeFalse();
            error.Should().NotBeNullOrEmpty();
            data!.Items.Should().BeEmpty();
        }

        [Fact]
        public void TryParse_WhenEmptyFile_ShouldFail()
        {
            var path = Path.Combine(_testTempDir, "empty.acf");
            File.WriteAllText(path, string.Empty);

            var ok = _parser.TryParse(path, out var data, out var error);

            ok.Should().BeFalse();
            error.Should().NotBeNullOrEmpty();
            data!.Items.Should().BeEmpty();
        }

        [Fact]
        public void TryParse_WhenUnbalancedBraces_ShouldFail()
        {
            // Truncated real ACF sequences end mid-object — must never parse "successfully"
            var path = Path.Combine(_testTempDir, "truncated.acf");
            File.WriteAllText(path, "\"AppWorkshop\"\n{\n\t\"WorkshopItemsInstalled\"\n\t{\n\t\t\"818773962\"\t\t\"1694112958\"");

            var ok = _parser.TryParse(path, out var data, out var error);

            ok.Should().BeFalse();
            error.Should().NotBeNullOrEmpty();
            data!.Items.Should().BeEmpty();
        }

        [Fact]
        public void TryParse_WhenEscapedQuoteNotClosed_ShouldFail()
        {
            // The value token is: open quote, escaped \" , then text with no closing
            // quote — unterminated, so the parser must reject it.
            var path = Path.Combine(_testTempDir, "broken_quote.acf");
            File.WriteAllText(path, "\"AppWorkshop\"\n{\n\t\"WorkshopItemDetails\"\n\t{\n\t\t\"1\"\n\t\t{\n\t\t\t\"sourceid\"\t\t\"\\\"unterminated1\n\t\t}\n\t}\n}");

            var ok = _parser.TryParse(path, out var data, out _);

            ok.Should().BeFalse();
            data!.Items.Should().BeEmpty();
        }

        [Fact]
        public void TryParse_BareKeysAndBareValues_ShouldStillParse()
        {
            // Loose-source tolerance: unquoted keys/values still map correctly
            var path = Path.Combine(_testTempDir, "bare.acf");
            File.WriteAllText(path, "AppWorkshop\n{\n\tWorkshopItemsInstalled\n\t{\n\t\t818773962\t\t1694112958\n\t}\n}");

            var ok = _parser.TryParse(path, out var data, out var error);

            ok.Should().BeTrue();
            data!.Items.Should().HaveCount(1);
            data.Items["818773962"].ResolvedTimeUpdated.Should().Be(1694112958);
        }

        [Fact]
        public void TryParse_WhenCorruptGarbage_ShouldFail()
        {
            var path = Path.Combine(_testTempDir, "garbage.acf");
            File.WriteAllText(path, "this is not an acf file at all");

            var ok = _parser.TryParse(path, out var data, out var error);

            ok.Should().BeFalse();
            error.Should().NotBeNullOrEmpty();
            data!.Items.Should().BeEmpty();
        }
    }
}
