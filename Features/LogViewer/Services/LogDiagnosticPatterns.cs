#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>Severity ranking for a diagnostic finding. Ordered so errors sort first.</summary>
    public enum LogDiagnosticSeverity
    {
        Error,
        Warning,
        Info
    }

    /// <summary>One known-issue signature: what matched, what it means, what to try.</summary>
    public sealed record LogDiagnosticPattern(
        Regex Pattern,
        string Title,
        string Description,
        string Suggestion,
        LogDiagnosticSeverity Severity);

    /// <summary>
    /// The known-issue catalog the Troubleshooter tab scans for. Every signature was
    /// checked against real Player.log files from both engine generations: Unity-era
    /// logs (1.6 and earlier — no timestamps, profiler and allocator sections) and
    /// Waylith logs (1.7+ — every message is timestamped and tagged, no profiler
    /// output). Patterns that only ever fired in Unity logs are kept for those logs;
    /// the Waylith-native ones cover the 1.7 symptom shapes (per-frame exception
    /// spam, audio decode failures, mod dependency metadata).
    /// </summary>
    public static class LogDiagnosticPatterns
    {
        private const RegexOptions Options =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        public static IReadOnlyList<LogDiagnosticPattern> All { get; } = new[]
        {
            // ── Errors ──────────────────────────────────────────────────────────
            new LogDiagnosticPattern(
                new Regex(@"NullReferenceException", Options),
                "Null Reference Exception",
                "A mod or the game dereferenced a null object.",
                "Usually conflicts or missing dependencies: run Maintenance → Dependencies and Maintenance → Incompatibilities, then disable the most recently added mod.",
                LogDiagnosticSeverity.Error),
            new LogDiagnosticPattern(
                new Regex(@"MissingMethodException", Options),
                "Missing Method Exception",
                "A mod is calling a method that does not exist in this game version.",
                "The mod is outdated: if Maintenance → Dependencies shows nothing missing, redownload or update the mod.",
                LogDiagnosticSeverity.Error),
            new LogDiagnosticPattern(
                new Regex(@"TypeLoadException", Options),
                "Type Load Exception",
                "Failed to load a type from a mod assembly.",
                "It may need a dependency — check Maintenance → Dependencies; with all deps present, the mod build is stale (redownload it).",
                LogDiagnosticSeverity.Error),
            new LogDiagnosticPattern(
                new Regex(@"Could not resolve cross-reference", Options),
                "Cross-reference Error",
                "A mod is referencing a def that does not exist.",
                "A required mod may be missing or the load order may be wrong: Maintenance → Dependencies first, then Sort on the toolbar.",
                LogDiagnosticSeverity.Error),
            new LogDiagnosticPattern(
                new Regex(@"Root level exception in OnGUI\(\)", Options),
                "UI Loop Exception",
                "An exception escaped into the graphical interface loop; the game keeps running but the interface misbehaves.",
                "A mod is hooking UI code that changed in this version. Check for updates of the mods near the first occurrence.",
                LogDiagnosticSeverity.Error),
            new LogDiagnosticPattern(
                new Regex(@"Exception drawing", Options),
                "Repeated Draw Exception",
                "The same drawing call throws every frame, flooding the log.",
                "A mod has an incompatible patch for whatever it tries to draw. Very high counts point at one specific mod — read the stack below the first occurrence.",
                LogDiagnosticSeverity.Error),

            // ── Warnings ───────────────────────────────────────────────────────
            new LogDiagnosticPattern(
                new Regex(@"Exception loading UnityEngine\.AudioClip from file", Options),
                "Audio Decode Failure",
                "A sound file could not be decoded, so it never plays.",
                "Common with lossy DLC sound assets on some drivers. Counts in the hundreds are tolerated; a single mod's own sounds failing means that mod's files are corrupt.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"Tried loading mod with the same packageId multiple times", Options),
                "Duplicate Mod",
                "The same mod is installed more than once (two folders, same packageId).",
                "Remove the duplicate with Maintenance → Duplicates and keep the copy you actually want.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"More than one value for a same version of List`\d+ named modDependenciesByVersion", Options),
                "Conflicting Dependency Data",
                "Several mods declare different dependency versions for the same target.",
                "Open Maintenance → Dependencies: the disagreement is listed there, and aligning the versions clears it.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"dependency .*needs to have <downloadUrl> and/or <steamWorkshopUrl> specified", Options),
                "Incomplete Mod Metadata",
                "Mods reference a dependency without saying where it can be downloaded.",
                "Harmless log noise — it does not affect Maintenance → Dependencies, which reads the installed mods directly. Only chase it if something fails to load.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"Could not find.*Def named", Options),
                "Missing Def",
                "The game cannot find a definition referenced by a mod.",
                "A dependency is missing or the load order is wrong: check Maintenance → Dependencies, then run Sort on the toolbar.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"XML error.*About\.xml", Options),
                "Mod XML Error",
                "A mod has malformed About.xml.",
                "The mod may be corrupted; try redownloading it.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"patch operation.*failed", Options),
                "Patch Operation Failed",
                "An XML patch from a mod could not be applied.",
                "Check the load order — run Sort on the toolbar; a patch lands when the mod it patches loads first.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"Shader.*not found", Options),
                "Missing Shader",
                "A shader file could not be loaded.",
                "Often harmless, but it can indicate a graphics mod problem.",
                LogDiagnosticSeverity.Warning),
            new LogDiagnosticPattern(
                new Regex(@"Persistent allocation .* was not freed before shutdown", Options),
                "Native Memory Leak Report",
                "The runtime reported allocations that were never released before the process exited.",
                "Normal for some mods at shutdown. Only worth chasing if the same allocation sizes grow every session.",
                LogDiagnosticSeverity.Warning),

            // ── Info (noise with known explanations) ───────────────────────────
            new LogDiagnosticPattern(
                new Regex(@"sourcePrecept.*null", Options),
                "Source Precept Null (Harmless)",
                "FloodLight or a similar mod generating precept errors.",
                "Generally harmless log noise; it can be ignored.",
                LogDiagnosticSeverity.Info),
            new LogDiagnosticPattern(
                new Regex(@"RocketMan", Options),
                "RocketMan Leftover Data",
                "RocketMan data found in a save while the mod is not loaded.",
                "Harmless log noise from a previously used mod.",
                LogDiagnosticSeverity.Info),
            new LogDiagnosticPattern(
                new Regex(@"GAGARIN: Cache (not found|expired)", Options),
                "GAGARIN Cache Rebuild",
                "Faster Game Loading had no usable cache and rebuilt it.",
                "Expected on the first launch after a game or modlist update; later launches reuse the cache.",
                LogDiagnosticSeverity.Info)
        };

        /// <summary>Severity label used by the troubleshooter list.</summary>
        public static string SeverityLabel(LogDiagnosticSeverity severity) => severity switch
        {
            LogDiagnosticSeverity.Error => "Error",
            LogDiagnosticSeverity.Warning => "Warning",
            _ => "Info"
        };
    }
}
