#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// Reads the mod list the log prints about itself. Both engine generations
    /// announce the active mods the same way — a line ending in "with mods:"
    /// followed by one "  - PackageId" line per mod — so the list is counted
    /// from the content whether or not the log is timestamped.
    /// </summary>
    public static class LogModListScanner
    {
        private const RegexOptions Options =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex Intro = new(@"with mods:\s*$", Options);

        private static readonly Regex Item = new(@"^\s*-\s*\S", Options);

        /// <summary>
        /// The number of mods listed in the first "with mods:" block, or 0 when
        /// the log never prints its mod list.
        /// </summary>
        public static int CountMods(IReadOnlyList<LogDiagnosticLine>? lines)
        {
            if (lines == null) return 0;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Text;
                if (string.IsNullOrEmpty(line) || !Intro.IsMatch(line)) continue;

                var count = 0;
                for (var j = i + 1; j < lines.Count; j++)
                {
                    var item = lines[j].Text;
                    if (string.IsNullOrEmpty(item) || !Item.IsMatch(item)) break;
                    count++;
                }

                if (count > 0) return count;
            }

            return 0;
        }
    }
}
