using System;
using FluentAssertions;
using RimSharp.Features.LogViewer.Services;
using Xunit;

namespace RimSharp.Tests.Features.LogViewer.Services
{
    /// <summary>
    /// Validator tests use the head shapes of every file family the viewer can
    /// be handed, plus prose and binary content that must not pass silently.
    /// </summary>
    public class LogFileValidatorTests
    {
        private const string WaylithHead =
            "=== Waylith log ===\n" +
            "Started:   2026-11-07 18:31:56.481 +03:00\n" +
            "Version:   1.7.5000.1275\n" +
            "2026-11-07 18:31:56.481  INFO [Verse] RimWorld 1.7.5000 rev1275\n";

        private const string UnityHead =
            "Mono path[0] = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed'\n" +
            "Initialize engine version: 2022.3.35f1 (011206c7a712)\n" +
            "GfxDevice: creating device client; threaded=1; jobified=0\n";

        private const string ExportHead =
            "Log uploaded on Saturday, November 7, 2026, 6:33:35 PM\n" +
            "Loaded mods:\n" +
            "Harmony(brrainz.harmony)[mv:3.0.1.0]: 0Harmony(3.0.0), HarmonyMod(3.0.1)\n";

        private const string AppLogHead =
            "2026-11-08 00:28:23.471 [INFO] [App.Startup] RimSharp starting up\n" +
            "2026-11-08 00:28:23.504 [INFO] [ModManager] Detected 61 installed mods\n";

        private const string Prose =
            "Meeting notes for the week\n" +
            "- Buy milk\n" +
            "- Call the mod author about the conflict\n";

        [Fact]
        public void Classify_RecognizesBothEngineGenerations()
        {
            LogFileValidator.Classify(WaylithHead, "Player.log").Verdict.Should().Be(LogFileVerdict.Valid);
            LogFileValidator.Classify(UnityHead, "Player.log").Verdict.Should().Be(LogFileVerdict.Valid);
            LogFileValidator.Classify(WaylithHead, "whatever.txt").Verdict.Should().Be(LogFileVerdict.Valid);
        }

        [Fact]
        public void Classify_RecognizesSharedExports()
        {
            var validation = LogFileValidator.Classify(ExportHead, "hugslib17.txt");

            validation.Verdict.Should().Be(LogFileVerdict.Valid);
            validation.Message.Should().Contain("Shared log upload");
        }

        [Fact]
        public void Classify_AppLogsAreInvalidHere_AndPointAtTheOtherWindow()
        {
            // This viewer reads Player.logs and shared uploads only; the app's
            // own logs belong to the App Logs window.
            var byShape = LogFileValidator.Classify(AppLogHead, "whatever.txt");
            byShape.Verdict.Should().Be(LogFileVerdict.Invalid);
            byShape.Message.Should().Contain("App Logs window");

            var byName = LogFileValidator.Classify("no tagged lines here", "RimSharp_General_20261108.log");
            byName.Verdict.Should().Be(LogFileVerdict.Invalid);
            byName.Message.Should().Contain("App Logs window");
        }

        [Fact]
        public void Classify_KnownGameFileNamesOpenWithoutContent()
        {
            // A truncated or hand-trimmed log still loads when it carries the
            // game's own file name — but the name is not a format, so the
            // auto-load path keeps its reports locked for these.
            foreach (var name in new[] { "Player.log", "Player-prev.log", "output_log.txt" })
            {
                var validation = LogFileValidator.Classify("half a line with no header", name);

                validation.Verdict.Should().Be(LogFileVerdict.Valid);
                validation.FormatRecognized.Should().BeFalse();
            }
        }

        [Fact]
        public void Classify_ContentShapesAreFormatRecognized()
        {
            LogFileValidator.Classify(WaylithHead, "Player.log").FormatRecognized.Should().BeTrue();
            LogFileValidator.Classify(UnityHead, "Player.log").FormatRecognized.Should().BeTrue();
            LogFileValidator.Classify(ExportHead, "hugslib17.txt").FormatRecognized.Should().BeTrue();
            LogFileValidator.Classify(AppLogHead, "whatever.txt").FormatRecognized.Should().BeTrue();
        }

        [Fact]
        public void Classify_ProseIsInvalid_WithAnExplanation()
        {
            var validation = LogFileValidator.Classify(Prose, "notes.txt");

            validation.Verdict.Should().Be(LogFileVerdict.Invalid);
            validation.Message.Should().Contain("doesn't look like a RimWorld Player.log or a shared log upload");
        }

        [Fact]
        public void Classify_EmptyFileIsInvalid()
        {
            LogFileValidator.Classify(null, "empty.txt").Verdict.Should().Be(LogFileVerdict.Invalid);
            LogFileValidator.Classify(string.Empty, "empty.txt").Verdict.Should().Be(LogFileVerdict.Invalid);
        }

        [Fact]
        public void Classify_BinaryContentIsInvalid()
        {
            // NUL bytes: a renamed executable.
            var binary = "MZ\0\0\0 some binary payload \0\0\0\0\0\0\0\0";

            var validation = LogFileValidator.Classify(binary, "payload.log");

            validation.Verdict.Should().Be(LogFileVerdict.Invalid);
            validation.Message.Should().Contain("not a text log");
        }

        [Fact]
        public void Classify_UndecodableTextIsInvalid()
        {
            // A UTF-16 file lands here: our reader decodes UTF-8, so most of
            // this becomes replacement characters.
            var utf16 = new string('\uFFFD', 200) + "tail";

            LogFileValidator.Classify(utf16, "legacy.txt").Verdict.Should().Be(LogFileVerdict.Invalid);
        }

        [Fact]
        public void LooksBinary_RealLogsWithOccasionalReplacementsStillPass()
        {
            // One replacement character in a real log is noise, not a decode
            // failure.
            var log = WaylithHead + "   at Some.Mod.Method() in SomeMod.dll\ufffd\n";

            LogFileValidator.LooksBinary(log).Should().BeFalse();
        }
    }
}
