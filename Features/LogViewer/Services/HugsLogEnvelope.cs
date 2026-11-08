#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>One assembly a mod loaded, as listed in a shared log export.</summary>
    public sealed record HugsLogAssemblyEntry(string Name, string? VersionInfo);

    /// <summary>
    /// One loaded mod from the publisher's roster. The version tags are all
    /// optional: <c>[v:]</c> is the mod's own version, <c>[ov:]</c> a version
    /// file override, <c>[mv:]</c> the About/PublishedVersionId version.
    /// </summary>
    public sealed record HugsLogModEntry(
        string Name,
        string? PackageId,
        string? ModMetaDataVersion,
        string? OverrideVersion,
        string? ManifestVersion,
        IReadOnlyList<HugsLogAssemblyEntry> Assemblies,
        bool HasNoAssemblies);

    /// <summary>
    /// One patched method and the patches on it, grouped the way the publisher
    /// lists them: <c>Method: PRE: a, b post: c TRANS: d</c>.
    /// </summary>
    public sealed record HugsLogPatchMethod(
        string Method,
        IReadOnlyList<string> Prefixes,
        IReadOnlyList<string> Postfixes,
        IReadOnlyList<string> Transpilers);

    /// <summary>One Harmony build and the mods carrying it into the session.</summary>
    public sealed record HugsLogHarmonyVersionGroup(string Version, IReadOnlyList<string> Owners);

    /// <summary>
    /// The metadata envelope a shared HugsLib log upload carries in front of
    /// the log itself, plus the extracted log body. Publishers redact and
    /// reshape the log before uploading, so the body is not byte-identical to
    /// the file the sharer or the export button produced — the flags record
    /// what the publisher admits doing.
    /// </summary>
    public sealed class HugsLogEnvelope
    {
        /// <summary>The raw upload timestamp as written, kept for when it won't parse.</summary>
        public string? UploadedOnText { get; init; }

        /// <summary>The parsed upload time, on the uploader's wall clock, when it parsed.</summary>
        public DateTime? UploadedOn { get; init; }

        public IReadOnlyList<HugsLogModEntry> Mods { get; init; } = Array.Empty<HugsLogModEntry>();

        public IReadOnlyList<HugsLogPatchMethod> PatchMethods { get; init; } = Array.Empty<HugsLogPatchMethod>();

        public IReadOnlyList<HugsLogHarmonyVersionGroup> HarmonyVersions { get; init; } =
            Array.Empty<HugsLogHarmonyVersionGroup>();

        /// <summary>The platform-info line the publisher emitted, when the section was present at all.</summary>
        public string? PlatformInfo { get; init; }

        /// <summary>True when the publisher uploaded the previous launch's log instead of the live one.</summary>
        public bool UploadedPreviousLaunch { get; init; }

        /// <summary>True when the publisher cut the body at its line cap and said so.</summary>
        public bool BodyTrimmed { get; init; }

        /// <summary>True when the body carries [Rimworld_dir]/[Home_dir] redaction markers.</summary>
        public bool PathsScrubbed { get; init; }

        /// <summary>The log the publisher wrapped — everything after its "Log file contents" header.</summary>
        public string BodyText { get; init; } = string.Empty;

        /// <summary>Envelope lines, including the body header. Body line numbers restart after this.</summary>
        public int EnvelopeLineCount { get; init; }
    }

    /// <summary>
    /// Reads the envelope shared HugsLib-style log uploads carry. Every publisher
    /// generation writes the same skeleton (upload time, loaded mods, active
    /// patches with the Harmony version map, optional platform info, then the
    /// log body under a "Log file contents" header), but section extras differ
    /// by build, so parsing tolerates missing sections and only requires the
    /// upload line plus the body header. Anything that doesn't look like an
    /// upload is returned unchanged, so a plain Player.log loads exactly as before.
    /// </summary>
    public static class HugsLogEnvelopeParser
    {
        private const string UploadPrefix = "Log uploaded on ";
        private const string ModsHeader = "Loaded mods:";
        private const string PatchesHeader = "Active Harmony patches:";
        private const string HarmonyVersionsPrefix = "Harmony versions present:";
        private const string PlatformInfoPrefix = "Platform information:";
        private const string BodyHeader = "Log file contents:";
        private const string PreviousLaunchHeader = "Log file contents from previous game launch:";

        private static readonly Regex ModLine = new(
            @"^(?<name>.*?)\((?<pid>[^()]+)\)(?<tags>(?:\[(?:v|ov|mv):[^\]]*\])*)\s*:\s*(?<rest>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex VersionTag = new(
            @"\[(?<key>v|ov|mv):(?<value>[^\]]*)\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex AssemblyEntry = new(
            @"^(?<name>[^()]+)\((?<version>[^()]*)\)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>True when the text opens with the publisher's upload line.</summary>
        public static bool LooksLikeEnvelope(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var firstLine = FirstLine(text);
            return firstLine.StartsWith(UploadPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Parses an upload's envelope and extracts its log body, or returns null
        /// when the text is not a shared export.
        /// </summary>
        public static HugsLogEnvelope? TryParse(string? text)
        {
            if (!LooksLikeEnvelope(text)) return null;

            var lines = (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            var uploadedOnText = lines[0][UploadPrefix.Length..].Trim();
            var index = 1;

            var mods = new List<HugsLogModEntry>();
            if (index < lines.Length && lines[index] == ModsHeader)
            {
                index++;
                while (index < lines.Length &&
                       lines[index].Length > 0 &&
                       lines[index] != PatchesHeader &&
                       !lines[index].StartsWith(BodyHeader, StringComparison.Ordinal) &&
                       !lines[index].StartsWith(PreviousLaunchHeader, StringComparison.Ordinal))
                {
                    var mod = ParseModLine(lines[index]);
                    if (mod != null) mods.Add(mod);
                    index++;
                }
            }

            var patches = new List<HugsLogPatchMethod>();
            var harmonyVersions = new List<HugsLogHarmonyVersionGroup>();

            // Sections are separated by a blank line; step over it so the
            // patch header is found even right after the roster.
            while (index < lines.Length && lines[index].Length == 0) index++;

            if (index < lines.Length && lines[index] == PatchesHeader)
            {
                index++;
                while (index < lines.Length)
                {
                    var line = lines[index];
                    if (line.StartsWith(HarmonyVersionsPrefix, StringComparison.Ordinal))
                    {
                        harmonyVersions.AddRange(ParseHarmonyVersions(line));
                        index++;
                        break;
                    }

                    if (line.Length == 0 ||
                        line.StartsWith(PlatformInfoPrefix, StringComparison.Ordinal) ||
                        line.StartsWith(BodyHeader, StringComparison.Ordinal) ||
                        line.StartsWith(PreviousLaunchHeader, StringComparison.Ordinal))
                    {
                        break;
                    }

                    var patch = ParsePatchLine(line);
                    if (patch != null) patches.Add(patch);
                    index++;
                }
            }

            // The patch loop stops on the version line only when the section is
            // well-formed; the version map can also appear on its own before
            // the platform section, so give it a second chance.
            while (index < lines.Length)
            {
                var line = lines[index];
                if (line.StartsWith(HarmonyVersionsPrefix, StringComparison.Ordinal) &&
                    harmonyVersions.Count == 0)
                {
                    harmonyVersions.AddRange(ParseHarmonyVersions(line));
                    index++;
                    continue;
                }

                break;
            }

            string? platformInfo = null;
            while (index < lines.Length)
            {
                var line = lines[index];
                if (line.Length == 0)
                {
                    index++;
                    continue;
                }

                if (line.StartsWith(BodyHeader, StringComparison.Ordinal))
                {
                    break;
                }

                if (line.StartsWith(PreviousLaunchHeader, StringComparison.Ordinal))
                {
                    break;
                }

                if (line.StartsWith(PlatformInfoPrefix, StringComparison.Ordinal))
                {
                    // The "included" variant spills CPU/OS/Memory onto the
                    // following lines; the common "hidden" variant is one line.
                    var platformParts = new List<string> { line[PlatformInfoPrefix.Length..].Trim() };
                    while (index + 1 < lines.Length &&
                           lines[index + 1].Length > 0 &&
                           !lines[index + 1].StartsWith(BodyHeader, StringComparison.Ordinal) &&
                           !lines[index + 1].StartsWith(PreviousLaunchHeader, StringComparison.Ordinal))
                    {
                        index++;
                        platformParts.Add(lines[index].Trim());
                    }

                    platformInfo = string.Join(" ", platformParts.Where(p => p.Length > 0));
                    index++;
                    continue;
                }

                index++;
            }

            var previousLaunch = false;
            var bodyStart = -1;
            while (index < lines.Length)
            {
                if (lines[index].StartsWith(BodyHeader, StringComparison.Ordinal))
                {
                    bodyStart = index + 1;
                    break;
                }

                if (lines[index].StartsWith(PreviousLaunchHeader, StringComparison.Ordinal))
                {
                    previousLaunch = true;
                    bodyStart = index + 1;
                    break;
                }

                index++;
            }

            // No body header: not an upload this viewer can split. Let it load
            // as ordinary content rather than guessing where the body starts.
            if (bodyStart < 0) return null;

            var bodyLines = lines[bodyStart..];
            var bodyText = string.Join("\n", bodyLines);
            if (bodyText.Length > 0) bodyText += "\n";

            return new HugsLogEnvelope
            {
                UploadedOnText = uploadedOnText,
                UploadedOn = ParseUploadTime(uploadedOnText),
                Mods = mods,
                PatchMethods = patches,
                HarmonyVersions = harmonyVersions,
                PlatformInfo = platformInfo,
                UploadedPreviousLaunch = previousLaunch,
                BodyTrimmed = bodyLines.Any(l => l.Contains("log trimmed to", StringComparison.Ordinal)),
                PathsScrubbed = bodyLines.Any(l =>
                    l.Contains("[Rimworld_dir]", StringComparison.Ordinal) ||
                    l.Contains("[Home_dir]", StringComparison.Ordinal)),
                BodyText = bodyText,
                EnvelopeLineCount = bodyStart
            };
        }

        /// <summary>
        /// The log body of a shared export, or the original text when it isn't
        /// one. Used for both the full read and the diagnostics' head read, so
        /// the envelope is stripped once and line numbers stay consistent.
        /// </summary>
        public static string Strip(string? text)
        {
            return TryParse(text)?.BodyText ?? text ?? string.Empty;
        }

        private static string FirstLine(string text)
        {
            var newline = text.IndexOf('\n');
            var line = newline < 0 ? text : text[..newline];
            return line.TrimEnd('\r');
        }

        private static DateTime? ParseUploadTime(string raw)
        {
            if (raw.Length == 0) return null;

            // ToLongDateString() + ", " + ToLongTimeString(): the publisher's
            // culture formats the stamp, and the common case is en-US.
            var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
            foreach (var culture in new[] { "en-US", "en-GB", "" })
            {
                var provider = culture.Length == 0
                    ? CultureInfo.InvariantCulture
                    : CultureInfo.GetCultureInfo(culture);

                if (DateTime.TryParse(raw, provider, styles, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static HugsLogModEntry? ParseModLine(string line)
        {
            var match = ModLine.Match(line);
            if (!match.Success)
            {
                // A roster line without the Name(PackageId) shape: keep the
                // name anyway rather than dropping the mod from the count.
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                return colon < 0 ? null : new HugsLogModEntry(line[..colon], null, null, null, null,
                    Array.Empty<HugsLogAssemblyEntry>(), false);
            }

            string? modMetaVersion = null;
            string? overrideVersion = null;
            string? manifestVersion = null;
            foreach (Match tag in VersionTag.Matches(match.Groups["tags"].Value))
            {
                var value = tag.Groups["value"].Value;
                switch (tag.Groups["key"].Value)
                {
                    case "v": modMetaVersion = value; break;
                    case "ov": overrideVersion = value; break;
                    case "mv": manifestVersion = value; break;
                }
            }

            var rest = match.Groups["rest"].Value.Trim();
            var assemblies = new List<HugsLogAssemblyEntry>();
            var noAssemblies = false;

            if (rest.Length > 0 && rest != "(no assemblies)")
            {
                foreach (var part in rest.Split(new[] { ", " }, StringSplitOptions.None))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length == 0) continue;

                    var assemblyMatch = AssemblyEntry.Match(trimmed);
                    if (assemblyMatch.Success)
                    {
                        assemblies.Add(new HugsLogAssemblyEntry(
                            assemblyMatch.Groups["name"].Value,
                            assemblyMatch.Groups["version"].Value));
                    }
                    else
                    {
                        assemblies.Add(new HugsLogAssemblyEntry(trimmed, null));
                    }
                }
            }
            else if (rest == "(no assemblies)")
            {
                noAssemblies = true;
            }

            return new HugsLogModEntry(
                match.Groups["name"].Value,
                match.Groups["pid"].Value,
                modMetaVersion,
                overrideVersion,
                manifestVersion,
                assemblies,
                noAssemblies);
        }

        private static HugsLogPatchMethod? ParsePatchLine(string line)
        {
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            if (split < 0) return null;

            var method = line[..split];
            var rest = line[(split + 2)..];

            var prefixes = new List<string>();
            var postfixes = new List<string>();
            var transpilers = new List<string>();
            var markers = new (string Marker, List<string> Target)[]
            {
                ("PRE: ", prefixes),
                ("post: ", postfixes),
                ("TRANS: ", transpilers)
            };

            var positions = new List<(int Start, int End, List<string> Target)>();
            foreach (var (marker, target) in markers)
            {
                var from = 0;
                while (true)
                {
                    var found = rest.IndexOf(marker, from, StringComparison.Ordinal);
                    if (found < 0) break;
                    positions.Add((found, found + marker.Length, target));
                    from = found + marker.Length;
                }
            }

            positions.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (var i = 0; i < positions.Count; i++)
            {
                var end = i + 1 < positions.Count ? positions[i + 1].Start : rest.Length;
                var owners = rest[positions[i].End..end].Trim().TrimEnd(';', ' ');
                foreach (var owner in owners.Split(new[] { ", " }, StringSplitOptions.None))
                {
                    var trimmed = owner.Trim();
                    if (trimmed.Length > 0) positions[i].Target.Add(trimmed);
                }
            }

            return new HugsLogPatchMethod(method, prefixes, postfixes, transpilers);
        }

        private static IEnumerable<HugsLogHarmonyVersionGroup> ParseHarmonyVersions(string line)
        {
            var payload = line[HarmonyVersionsPrefix.Length..].Trim();
            if (payload.Length == 0) yield break;

            foreach (var group in payload.Split(new[] { "; " }, StringSplitOptions.None))
            {
                var trimmedGroup = group.Trim();
                if (trimmedGroup.Length == 0) continue;

                var split = trimmedGroup.IndexOf(": ", StringComparison.Ordinal);
                if (split < 0)
                {
                    yield return new HugsLogHarmonyVersionGroup(trimmedGroup, Array.Empty<string>());
                    continue;
                }

                var version = trimmedGroup[..split];
                var owners = trimmedGroup[(split + 2)..]
                    .Split(new[] { ", " }, StringSplitOptions.None)
                    .Select(o => o.Trim())
                    .Where(o => o.Length > 0)
                    .ToList();

                yield return new HugsLogHarmonyVersionGroup(version, owners);
            }
        }
    }
}
