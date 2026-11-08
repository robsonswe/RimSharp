using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using RimSharp.Features.AppLogs.Services;
using Xunit;

namespace RimSharp.Tests.Features.AppLogs.Services
{
    /// <summary>
    /// Reader tests use the exact line shape LoggerService writes:
    /// <c>yyyy-MM-dd HH:mm:ss.fff [LEVEL] [module] message</c>, with
    /// exception text following as bare continuation lines.
    /// </summary>
    public class AppLogReaderTests
    {
        [Fact]
        public void ParseLevel_MapsEveryTagTheWriterEmits()
        {
            AppLogReader.ParseLevel("DEBUG").Should().Be(AppLogLevel.Debug);
            AppLogReader.ParseLevel("INFO").Should().Be(AppLogLevel.Info);
            AppLogReader.ParseLevel("WARNING").Should().Be(AppLogLevel.Warning);
            AppLogReader.ParseLevel("WARN").Should().Be(AppLogLevel.Warning);
            AppLogReader.ParseLevel("ERROR").Should().Be(AppLogLevel.Error);
            AppLogReader.ParseLevel("CRITICAL").Should().Be(AppLogLevel.Critical);
            AppLogReader.ParseLevel("FATAL").Should().Be(AppLogLevel.Critical);
        }

        [Fact]
        public void ParseLevel_UnknownTagsReadAsInfoRatherThanHidingTheLine()
        {
            AppLogReader.ParseLevel("TRACE").Should().Be(AppLogLevel.Debug);
            AppLogReader.ParseLevel("whatever").Should().Be(AppLogLevel.Info);
        }

        [Fact]
        public void Parse_ReadsTheLevelTimestampAndModuleOffEveryLine()
        {
            var entries = AppLogReader.Parse(
                "2026-11-07 18:31:56.481 [INFO] [General] Starting up\n" +
                "2026-11-07 18:32:01.100 [ERROR] [SteamCmdDownloader] steamcmd exited\n");

            entries.Should().HaveCount(2);
            entries[0].Level.Should().Be(AppLogLevel.Info);
            entries[0].Module.Should().Be("General");
            entries[0].Timestamp.Should().Be(new DateTime(2026, 11, 7, 18, 31, 56, 481));
            entries[1].Level.Should().Be(AppLogLevel.Error);
            entries[1].Module.Should().Be("SteamCmdDownloader");
            entries[1].Text.Should().Be("steamcmd exited");
            entries[1].IsProblem.Should().BeTrue();
        }

        [Fact]
        public void Parse_KeepsExceptionContinuationsWithTheirMessage()
        {
            var text =
                "2026-11-07 18:33:04.062 [ERROR] [DownloadedItemProcessor] Failed\n" +
                "Exception: System.NullReferenceException: Object reference not set to an instance of an object.\n" +
                "StackTrace:    at RimSharp.Services.Workshop.SteamWorkshopApiClient.Fetch()\n" +
                "2026-11-07 18:33:05.000 [INFO] [General] Recovered\n";

            var entries = AppLogReader.Parse(text);

            entries.Should().HaveCount(2);
            var failure = entries[0];
            failure.LineCount.Should().Be(3);
            failure.Text.Should().Contain("Failed");
            failure.Text.Should().Contain("NullReferenceException");
            failure.Text.Should().Contain("SteamWorkshopApiClient.Fetch()");
            entries[1].Text.Should().Be("Recovered");
        }

        [Fact]
        public void Parse_KeepsLeadingOrphanLinesVisible()
        {
            var entries = AppLogReader.Parse("a torn-off line without a tag\n");

            entries.Should().HaveCount(1);
            entries[0].Text.Should().Be("a torn-off line without a tag");
            entries[0].Level.Should().Be(AppLogLevel.Info);
        }

        [Fact]
        public void Parse_ToleratesCrLfAndBareCr()
        {
            var lf = AppLogReader.Parse("2026-11-07 18:31:56.481 [INFO] [General] one\n");
            var crlf = AppLogReader.Parse("2026-11-07 18:31:56.481 [INFO] [General] one\r\n");
            var cr = AppLogReader.Parse("2026-11-07 18:31:56.481 [INFO] [General] one\r");

            lf.Should().HaveCount(1);
            crlf.Should().HaveCount(1);
            cr.Should().HaveCount(1);
            crlf[0].Text.Should().Be("one");
        }

        [Fact]
        public void Parse_EmptyInputIsNoEntries()
        {
            AppLogReader.Parse(null).Should().BeEmpty();
            AppLogReader.Parse(string.Empty).Should().BeEmpty();
        }

        [Fact]
        public void CountStats_TalliesEverySeverity()
        {
            var text =
                "2026-11-07 18:31:56.481 [INFO] [General] a\n" +
                "2026-11-07 18:31:57.481 [DEBUG] [General] b\n" +
                "2026-11-07 18:31:58.481 [WARNING] [General] c\n" +
                "2026-11-07 18:31:59.481 [ERROR] [General] d\n" +
                "2026-11-07 18:32:00.481 [CRITICAL] [General] e\n";

            var stats = AppLogReader.CountStats(AppLogReader.Parse(text));

            stats.Debug.Should().Be(1);
            stats.Info.Should().Be(1);
            stats.Warning.Should().Be(1);
            stats.Error.Should().Be(1);
            stats.Critical.Should().Be(1);
            stats.Total.Should().Be(5);
        }

        [Fact]
        public void ReadFile_MissingFileIsNoEntries()
        {
            AppLogReader.ReadFile(Path.Combine(Path.GetTempPath(), "does-not-exist.log")).Should().BeEmpty();
            AppLogReader.ReadFile(string.Empty).Should().BeEmpty();
        }
    }
}
