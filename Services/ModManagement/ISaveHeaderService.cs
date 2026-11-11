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
        /// absorbed automatically. The read does not stop AT the marker - it continues
        /// until the &lt;/info&gt; tag that closes the world info block right behind it,
        /// because that block carries the world name and the world seed. A 64 MB
        /// ceiling exists only as a guard against pathological or corrupt files - it
        /// is no feature-size limit.
        /// Returns null for unreadable, corrupt or truncated files. Fields that the
        /// head window does not contain stay null - the caller shows them as unknown.
        /// </summary>
        Task<SaveDetails?> ReadDetailsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans the WHOLE file for the human pawns of the player faction and
        /// returns what each of them carries: the name, the join status, the
        /// ages, the vanilla traits, the vanilla skills and the vanilla
        /// conditions. This is a separate, explicit read: it is never part of
        /// the head window and it is never run on its own - the user asks for
        /// it. The scan streams the file in chunks (a few MB of I/O), so it
        /// stays off the UI thread; the cancellation token is honored between
        /// the chunks.
        /// The file is only read - never written, moved or deleted.
        /// Returns an empty list when the file holds no human pawn, and never
        /// throws for a file that cannot be read.
        /// </summary>
        /// <param name="filePath">The save to scan.</param>
        /// <param name="cancellationToken">Stops the scan between the chunks.</param>
        Task<IReadOnlyList<SaveColonist>> ReadColonistNamesAsync(string filePath,
            CancellationToken cancellationToken = default);

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
        /// Formats the in-game date of a save the way the game does it: the
        /// year, the <b>quadrum</b> (Aprimay, Jugust, Septober, Decembary - the
        /// names of the game's own Quadrum enum), the day of the quadrum (1-15)
        /// and the hour of the day. RimWorld 1.7 has no "spring/summer"
        /// calendar year: the year has 12 twelfths and 4 quadrums.
        /// The colony's planet tile is an INDEX ("tileId,layerId" in 1.7, a
        /// plain tile id in 1.5), not a
        /// position: the game turns it into a longitude and a latitude only
        /// after it regenerates the planet mesh from the world seed (millions of
        /// tiles), so a reader cannot resolve it. The location therefore
        /// falls back to the game's own fallback for a tile it cannot resolve
        /// (WorldGrid.LongLatOf returns the zero vector) - longitude 0, so no
        /// time-of-day shift. The calendar itself is the same in 1.5 and 1.7
        /// (1.5: RimWorld/GenDate.cs — the same twelfths, quadrums, year and
        /// longitude shift), so the date does not branch on the game version.
        /// The season is a climate value, not a calendar one: the game derives
        /// it from the latitude of the colony (SeasonUtility.GetReportedSeason).
        /// The save carries the game's own answer to that (the reported season
        /// of the date notifier); pass it in and it is shown - when the save
        /// does not carry it, the date is shown without a season.
        /// Returns null when an input is missing or when every input is zero.
        /// </summary>
        static string? FormatInGameDate(int? ticksGame, long? gameStartAbsTick, int? startingYear,
            string? startingTile = null, string? reportedSeason = null)
        {
            return FormatInGameDateAt(ticksGame, gameStartAbsTick, startingYear,
                ResolveColonyLongitude(startingTile), reportedSeason);
        }

        /// <summary>
        /// Formats the in-game date at a known location. The longitude shifts
        /// the local ticks exactly like the game does (the time zone of the
        /// longitude, 15 degrees per zone, 2 500 ticks per hour) - so the hour
        /// of the day and the day of the year can differ from the readout
        /// without a shift. Everything else is the game's own calendar.
        /// </summary>
        static string? FormatInGameDateAt(int? ticksGame, long? gameStartAbsTick, int? startingYear,
            float longitude, string? reportedSeason = null)
        {
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

            // The local ticks: the absolute ticks shifted by the time zone of
            // the colony's longitude.
            long localTicks = absTick + LocalTicksOffsetFromLongitude(longitude);

            int year = startingYear.Value + (int)Math.Floor(localTicks / (double)TicksPerYear);
            int twelfth = PositiveModRemap(localTicks, TicksPerTwelfth, TwelfthsPerYear);
            string quadrum = QuadrumOfTwelfth(twelfth);
            int dayOfYear = PositiveModRemap(localTicks, TicksPerDay, DaysPerYear);
            int dayOfQuadrum = (dayOfYear % DaysPerQuadrum) + 1;
            int hour = PositiveModRemap(localTicks, TicksPerHour, HoursPerDay);
            int minute = (int)((PositiveMod(localTicks, TicksPerHour) * 60L) / TicksPerHour);

            string date = $"Year {year}, {quadrum}, day {dayOfQuadrum}, {hour:00}:{minute:00}";

            string? season = FormatReportedSeason(reportedSeason);
            return season is null ? date : $"{date}, {season}";
        }

        /// <summary>
        /// The tick constants of the game's calendar (RimWorld/GenDate.cs).
        /// </summary>
        private const long TicksPerHour = 2_500;
        private const long TicksPerDay = 60_000;
        private const long TicksPerTwelfth = 300_000;
        private const long TicksPerYear = 3_600_000;
        private const int HoursPerDay = 24;
        private const int DaysPerYear = 60;
        private const int DaysPerQuadrum = 15;
        private const int TwelfthsPerYear = 12;

        /// <summary>
        /// The names of the game's Quadrum enum (RimWorld/Quadrum.cs) - the
        /// fourths of a year.
        /// </summary>
        private static readonly string[] QuadrumNames = { "Aprimay", "Jugust", "Septober", "Decembary" };

        /// <summary>
        /// The quadrum of a twelfth (RimWorld/TwelfthUtility.GetQuadrum): the
        /// first three twelfths are Aprimay, then Jugust, Septober and
        /// Decembary.
        /// </summary>
        private static string QuadrumOfTwelfth(int twelfth)
            => QuadrumNames[Math.Clamp(twelfth / 3, 0, QuadrumNames.Length - 1)];

        /// <summary>
        /// The tick offset of a longitude: the game shifts the absolute ticks by
        /// the time zone of the longitude (15 degrees per zone, 2 500 ticks per
        /// hour) - RimWorld/GenDate.cs, LocalTicksOffsetFromLongitude.
        /// </summary>
        private static long LocalTicksOffsetFromLongitude(float longitude)
            => (long)Math.Round(longitude / 15f, MidpointRounding.ToEven) * TicksPerHour;

        /// <summary>
        /// The longitude of the colony, read from its planet tile. The tile is
        /// an index ("tileId,layerId" in 1.7, a plain tile id in 1.5) into the
        /// planet mesh that the game regenerates from the world seed, so a
        /// reader cannot turn it into a position - the game is followed into
        /// its own fallback for a tile it cannot resolve (WorldGrid.LongLatOf
        /// returns the zero vector): the longitude 0, i.e. no time-of-day shift.
        /// The calendar is the same in 1.5 and 1.7, so this holds for both.
        /// </summary>
        private static float ResolveColonyLongitude(string? startingTile)
            => 0f;

        /// <summary>
        /// The game's own remainder (Verse/GenMath.cs): a value in [0, m).
        /// </summary>
        private static long PositiveMod(long x, long m)
            => (x % m + m) % m;

        /// <summary>
        /// The game's own remainder (Verse/GenMath.cs): the value divided by d,
        /// remapped into [0, m).
        /// </summary>
        private static int PositiveModRemap(long x, long d, int m)
        {
            if (x < 0)
            {
                x -= d - 1;
            }

            return (int)((x / d % m + m) % m);
        }

        /// <summary>
        /// The season the game reported for the save, in the wording of the
        /// game (the names of its Season enum). An unknown or missing value
        /// stays empty - the date is then shown without a season.
        /// </summary>
        private static string? FormatReportedSeason(string? reportedSeason)
        {
            if (string.IsNullOrWhiteSpace(reportedSeason))
            {
                return null;
            }

            return reportedSeason.Trim() switch
            {
                "Spring" => "Spring",
                "Summer" => "Summer",
                "Fall" => "Fall",
                "Winter" => "Winter",
                "PermanentSummer" => "Permanent Summer",
                "PermanentWinter" => "Permanent Winter",
                _ => reportedSeason.Trim()
            };
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
    /// <param name="StartingTile">
    /// The planet tile the colony started on. It is NOT a
    /// position: the game turns the tile index into a longitude and a latitude
    /// only after it regenerates the planet mesh from the world seed. The tile
    /// is the anchor of the calendar - the date computation reads it (and falls
    /// back to the game's own fallback for a tile it cannot resolve), so it is
    /// extracted even though it is not displayed.
    /// The value is the raw text of the element: 1.7 writes a planet tile
    /// ("tileId,layerId"), 1.5 writes a plain tile id (an int) - the difference
    /// does not matter, because neither of them carries a position.
    /// </param>
    /// <param name="ReportedSeason">
    /// The season the game itself reported for the save ("Spring", "Summer",
    /// "Fall", "Winter", "PermanentSummer", "PermanentWinter"). The game
    /// derives the season from the latitude of the colony - the save carries
    /// the game's own answer, so the date shows the real season.
    /// </param>
    /// <param name="WorldName">
    /// The name of the world the colony lives on. It sits in the info block right
    /// behind the &lt;world&gt; marker - the head window reads that far.
    /// </param>
    /// <param name="WorldSeed">The seed string of the world.</param>
    /// <param name="PlayerFactionType">
    /// The faction def of the player ("PlayerColony", "PlayerTribe").
    /// </param>
    /// <param name="ScenarioSummary">The short summary of the scenario.</param>
    /// <param name="ScenarioDescription">The description text of the scenario.</param>
    /// <param name="IsPlanetMode">
    /// True when the scenario carries the planet layer part (planet mode), false
    /// when the scenario has parts but no planet layer, null when the scenario
    /// carries no parts at all - then nothing can be said about it.
    /// </param>
    /// <param name="ActiveDlcNames">
    /// The official DLCs the save was played with ("Royalty", "Ideology", ...).
    /// The head window does not carry that list - it is derived from the mod
    /// ids of the save's meta block and filled in by the caller.
    /// </param>
    public sealed record SaveDetails(
        string? ScenarioName, string? StorytellerDef, string? DifficultyDef,
        int? TicksGame, long? GameStartAbsTick, int? StartingYear,
        string? StartingTile, string? ReportedSeason,
        string? WorldName, string? WorldSeed,
        string? PlayerFactionType,
        string? ScenarioSummary, string? ScenarioDescription,
        bool? IsPlanetMode,
        IReadOnlyList<string> ActiveDlcNames);

    /// <summary>
    /// One human pawn of the player faction, as the save records it.
    /// Every part is optional: a save that carries no nick has no nick here -
    /// nothing is invented to fill a gap.
    /// </summary>
    /// <param name="First">The first name of the colonist.</param>
    /// <param name="Nick">The nick of the colonist, when the save carries one.</param>
    /// <param name="Last">The last name of the colonist.</param>
    /// <param name="Gender">
    /// The gender of the pawn ("Male", "Female"). The scribe omits the element
    /// when the value equals its default - and the default of a human is Male
    /// (corpus evidence: 16 explicit "Female", 0 explicit "Male", 10 absent,
    /// every one of them a male-name pawn) - so an absent element means
    /// "Male" here.
    /// </param>
    /// <param name="Xenotype">
    /// The xenotype (race) def the save records, e.g. "Baseliner". Null when
    /// the save carries none - a save without Biotech content has no
    /// xenotypes at all. A def with a mod namespace is not part of it.
    /// </param>
    /// <param name="AgeBiologicalYears">
    /// The biological age in years (the age of the body). Null when the save
    /// carries no age tracker. The chronological age (the time that existed) is
    /// deliberately NOT part of this: the game uses it in its internal logic
    /// only - every player-facing screen of the game shows the biological age.
    /// </param>
    /// <param name="Traits">
    /// The traits of the pawn - the vanilla ones only. A gene-derived trait
    /// and a mod trait are not part of it.
    /// </param>
    /// <param name="Skills">The vanilla skills of the pawn.</param>
    /// <param name="Conditions">The vanilla conditions (hediffs) of the pawn.</param>
    public sealed record SaveColonist(
        string? First, string? Nick, string? Last,
        string? Gender, string? Xenotype,
        double? AgeBiologicalYears,
        IReadOnlyList<string> Traits,
        IReadOnlyList<SaveColonistSkill> Skills,
        IReadOnlyList<SaveColonistCondition> Conditions)
    {
        /// <summary>
        /// The name as it is shown: the first name, the nick in quotes and the
        /// last name - the parts the save carries, in that order. A nick that
        /// only repeats the first or the last name is not shown a second time.
        /// </summary>
        public string DisplayName
        {
            get
            {
                var parts = new List<string>(3);

                string? first = string.IsNullOrWhiteSpace(First) ? null : First!.Trim();
                string? nick = string.IsNullOrWhiteSpace(Nick) ? null : Nick!.Trim();
                string? last = string.IsNullOrWhiteSpace(Last) ? null : Last!.Trim();

                if (first is not null)
                {
                    parts.Add(first);
                }

                if (nick is not null
                    && !string.Equals(nick, first, StringComparison.Ordinal)
                    && !string.Equals(nick, last, StringComparison.Ordinal))
                {
                    parts.Add($"\"{nick}\"");
                }

                if (last is not null)
                {
                    parts.Add(last);
                }

                return string.Join(" ", parts);
            }
        }
    }

    /// <summary>
    /// One vanilla skill of a colonist.
    /// </summary>
    /// <param name="Def">The skill def, e.g. "Shooting".</param>
    /// <param name="Level">The skill level - 0 when the save omits the level.</param>
    /// <param name="Passion">
    /// The passion the save records: "Minor", "Major" or null when the save
    /// carries none.
    /// </param>
    public sealed record SaveColonistSkill(string Def, int Level, string? Passion);

    /// <summary>
    /// One vanilla condition (hediff) of a colonist.
    /// </summary>
    /// <param name="Def">The condition def, e.g. "Stab".</param>
    /// <param name="Severity">
    /// The severity as the save records it. The unit depends on the class:
    /// an injury counts in points, the other conditions are a 0..1 fraction.
    /// </param>
    /// <param name="Class">The class of the hediff, e.g. "Hediff_Injury".</param>
    /// <param name="PainCategory">
    /// The pain category, when the save carries one ("LowPain", "MediumPain",
    /// "HighPain").
    /// </param>
    public sealed record SaveColonistCondition(string Def, double? Severity, string? Class, string? PainCategory);
}
