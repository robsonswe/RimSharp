using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Read-only access to RimWorld save files (*.rws).
    /// The service never writes, moves or deletes a save.
    /// </summary>
    public interface ISaveHeaderService
    {
        /// <summary>
        /// Lists the saves in the game saves folder, newest-first by last write time.
        /// Top-level *.rws files only (the game never writes saves into subfolders).
        /// Never throws: unreadable entries are omitted instead of failing the call.
        /// </summary>
        Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams ONLY the &lt;meta&gt; block of a save and returns its header.
        /// Returns null for unreadable, corrupt, truncated or garbage files.
        /// </summary>
        Task<SaveHeader?> ReadHeaderAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads ONLY the head window of a save and returns the details it contains.
        /// The window is defined by the &lt;world&gt; marker: the read continues until
        /// that marker is found, so heads grow with the content of the save and are
        /// absorbed automatically. A 64 MB ceiling exists only as a guard against
        /// pathological or corrupt files - it is no feature-size limit.
        /// Returns null for unreadable, corrupt or truncated files. Fields that the
        /// head window does not contain stay null - the caller shows them as unknown.
        /// </summary>
        Task<SaveDetails?> ReadDetailsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Compares the mods recorded in a save against the currently active and the
        /// currently installed mod lists.
        /// A save without recorded mods is <see cref="SaveCompat.Unknown"/>;
        /// a save referencing mods that are not installed is <see cref="SaveCompat.Missing"/>;
        /// a save whose mod set differs from the active set is <see cref="SaveCompat.Changed"/>;
        /// otherwise the save is <see cref="SaveCompat.Compatible"/>.
        /// </summary>
        SaveCompat Compare(SaveHeader header,
            IReadOnlyCollection<string> activeModIds,
            IReadOnlyCollection<string> allInstalledModIds);

        /// <summary>
        /// Lists the mods that are active now but were not in the save (added) and the
        /// mods that were in the save but are not active now (removed). Both lists are
        /// sorted; the comparison ignores case.
        /// </summary>
        (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) Diff(SaveHeader header,
            IReadOnlyCollection<string> activeModIds);

        /// <summary>
        /// Formats the in-game date from the raw tick fields of the save.
        /// 3,600,000 ticks per year, 900,000 ticks per season, 60,000 ticks per day
        /// and 2,500 ticks per hour; the day of the season is one-based.
        /// Seasons wrap within a year (Spring/Summer/Fall/Winter) and the year
        /// carries when the last season of a year rolls over.
        /// Returns null when an input is missing or when every input is zero.
        /// </summary>
        static string? FormatInGameDate(int? ticksGame, long? gameStartAbsTick, int? startingYear)
        {
            const long TicksPerHour = 2_500;
            const long TicksPerDay = 60_000;
            const long TicksPerSeason = 900_000;
            const long TicksPerYear = 3_600_000;

            if (ticksGame is null || gameStartAbsTick is null || startingYear is null)
            {
                return null;
            }

            if (ticksGame.Value == 0 && gameStartAbsTick.Value == 0 && startingYear.Value == 0)
            {
                return null; // Nothing recorded - no date can be derived.
            }

            long absTick = gameStartAbsTick.Value + ticksGame.Value;
            if (absTick < 0)
            {
                return null; // Broken tick data - never invent a date from it.
            }

            long yearsElapsed = absTick / TicksPerYear;
            long tickInYear = absTick % TicksPerYear;
            long seasonIndex = tickInYear / TicksPerSeason;
            long dayOfSeason = ((tickInYear % TicksPerSeason) / TicksPerDay) + 1;
            int hour = (int)((tickInYear % TicksPerDay) / TicksPerHour);

            string season = seasonIndex switch
            {
                0 => "Spring",
                1 => "Summer",
                2 => "Fall",
                3 => "Winter",
                _ => $"Season {seasonIndex}"
            };

            return $"Year {startingYear.Value + yearsElapsed}, {season}, day {dayOfSeason}, {hour:00}:00";
        }

        /// <summary>
        /// The official DLCs a save was played with, derived from the mod ids of
        /// the save's meta block. The base game ("ludeon.rimworld") is not a DLC
        /// and is never listed; mod ids that are no official DLC are ignored.
        /// The order of the names is the order the DLCs are shown in.
        /// </summary>
        static IReadOnlyList<string> ResolveActiveDlcNames(IReadOnlyCollection<string> modIds)
        {
            // The official DLC package ids, in the order the DLCs are shown.
            (string PackageId, string Name)[] officialDlc =
            {
                ("ludeon.rimworld.royalty", "Royalty"),
                ("ludeon.rimworld.ideology", "Ideology"),
                ("ludeon.rimworld.biotech", "Biotech"),
                ("ludeon.rimworld.anomaly", "Anomaly"),
                ("ludeon.rimworld.odyssey", "Odyssey")
            };

            if (modIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var ids = new HashSet<string>(modIds, StringComparer.OrdinalIgnoreCase);

            var names = new List<string>();
            foreach ((string packageId, string name) in officialDlc)
            {
                if (ids.Contains(packageId))
                {
                    names.Add(name);
                }
            }

            return names;
        }
    }

    /// <summary>
    /// A save file found in the saves folder.
    /// </summary>
    /// <param name="Name">The save name without the file extension.</param>
    /// <param name="Path">The full path of the save file.</param>
    /// <param name="SizeBytes">The file size in bytes.</param>
    /// <param name="LastModifiedUtc">The last write time of the file (UTC).</param>
    public sealed record SaveFileEntry(string Name, string Path, long SizeBytes, DateTime LastModifiedUtc);

    /// <summary>
    /// The parsed &lt;meta&gt; block of a save.
    /// </summary>
    /// <param name="Name">The save name without the file extension.</param>
    /// <param name="Path">The full path of the save file.</param>
    /// <param name="GameVersion">The game version the save was written by.</param>
    /// <param name="ModIds">The package ids of the mods used by the save.</param>
    /// <param name="ModSteamIds">
    /// The Workshop ids of the mods used by the save, index-aligned with
    /// <paramref name="ModIds"/>. Entries that are "0" or non-numeric are kept verbatim.
    /// Saves older than 1.6 have no such element - the list is then empty, never a
    /// length-matched placeholder.
    /// </param>
    /// <param name="ModNames">The display names of the mods used by the save.</param>
    public sealed record SaveHeader(string Name, string Path, string GameVersion,
        IReadOnlyList<string> ModIds, IReadOnlyList<string> ModSteamIds,
        IReadOnlyList<string> ModNames);

    /// <summary>
    /// How a save relates to the currently active mods.
    /// </summary>
    public enum SaveCompat
    {
        /// <summary>The save uses exactly the active mods.</summary>
        Compatible,

        /// <summary>The save uses a different set of mods - all of them are still installed.</summary>
        Changed,

        /// <summary>The save uses mods that are not installed anymore.</summary>
        Missing,

        /// <summary>The save records no mods - compatibility cannot be judged.</summary>
        Unknown,

        /// <summary>The save could not be read.</summary>
        Unreadable
    }

    /// <summary>
    /// The details read from the head window of a save.
    /// Every field is optional: a field the head window does not contain stays null.
    /// </summary>
    /// <param name="ScenarioName">The scenario the colony was started with.</param>
    /// <param name="StorytellerDef">The storyteller def of the colony.</param>
    /// <param name="DifficultyDef">The difficulty def of the colony.</param>
    /// <param name="TicksGame">The ticks played in the current session.</param>
    /// <param name="GameStartAbsTick">The absolute tick the colony started at.</param>
    /// <param name="StartingYear">The year the colony started in.</param>
    /// <param name="StartingTile">The tile the colony started on.</param>
    /// <param name="WealthTotal">The total wealth of the colony.</param>
    /// <param name="WealthItems">The wealth held in items.</param>
    /// <param name="WealthBuildings">The wealth held in buildings.</param>
    /// <param name="FreeColonists">The number of free colonists.</param>
    /// <param name="Prisoners">The number of prisoners.</param>
    /// <param name="ResearchDefsCompleted">The number of completed research projects.</param>
    /// <param name="ActiveDlcNames">
    /// The official DLCs the save was played with ("Royalty", "Ideology", ...).
    /// The head window does not carry that list - it is derived from the mod
    /// ids of the save's meta block and filled in by the caller.
    /// </param>
    public sealed record SaveDetails(
        string? ScenarioName, string? StorytellerDef, string? DifficultyDef,
        int? TicksGame, long? GameStartAbsTick, int? StartingYear,
        string? StartingTile,
        double? WealthTotal, double? WealthItems, double? WealthBuildings,
        int? FreeColonists, int? Prisoners,
        int? ResearchDefsCompleted, IReadOnlyList<string> ActiveDlcNames);
}
