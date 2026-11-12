using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Read-only reader for RimWorld save files (*.rws).
    ///
    /// Two read scopes exist, both streaming and both bounded:
    ///  - the &lt;meta&gt; block (game version + mod lists), capped at 4 MB and stopped
    ///    at &lt;/meta&gt;, so the huge &lt;game&gt; block is never touched;
    ///  - the head window of the file, which ends at &lt;world&gt; - the window is
    ///    defined by that marker, so heads grow with the content of the save
    ///    and are absorbed automatically. The read does not stop AT the marker:
    ///    it continues until the &lt;/info&gt; tag that closes the world info
    ///    block right behind it, because that block carries the world name and
    ///    the world seed. A 64 MB ceiling guards against pathological or
    ///    corrupt files (and gzip expansion); it is no feature-size limit. The
    ///    map and world payloads (the vast majority of the file) are never touched.
    ///
    /// A third, separate scope exists: the whole-file scan for the colonist
    /// names. It is never run on its own - the user asks for it explicitly.
    ///
    /// Saves are never written, moved or deleted by this service.
    /// </summary>
    public class SaveHeaderService : ISaveHeaderService
    {
        /// <summary>Default cap of the accumulated &lt;meta&gt; window (4 MB).</summary>
        internal const int DefaultMetaCapBytes = 4 * 1024 * 1024;

        /// <summary>
        /// The hard ceiling of the accumulated head window (64 MB). The window
        /// itself is marker-defined - the read ends at the &lt;/info&gt; tag behind
        /// the &lt;world&gt; marker. This ceiling only stops pathological or corrupt
        /// files (and gzip bombs): a real save head is orders of magnitude
        /// smaller, so every valid save is read whole.
        /// </summary>
        internal const int MaxHeadWindowBytes = 64 * 1024 * 1024;

        /// <summary>Size of a single read chunk (64 KB).</summary>
        internal const int ChunkSize = 64 * 1024;

        /// <summary>Size of a single chunk of the colonist name scan (1 MB).</summary>
        internal const int ColonistScanChunkBytes = 1024 * 1024;

        /// <summary>
        /// The ceiling of the player-faction pre-pass of the colonist scan. The
        /// faction manager of the measured saves sits at ~0.9 MB (the
        /// &lt;world&gt; block opens at 74-200 KB), far before the maps, so the
        /// pre-pass stops long before it in every real save. The ceiling only
        /// stops a pathological or corrupt file that carries no player faction
        /// at all.
        /// </summary>
        internal const int PlayerFactionScanCapBytes = 64 * 1024 * 1024;

        private static readonly byte[] MetaEndMarker = Encoding.ASCII.GetBytes("</meta>");
        private static readonly byte[] WorldStartMarker = Encoding.ASCII.GetBytes("<world");
        private static readonly byte[] WorldInfoEndMarker = Encoding.ASCII.GetBytes("</info>");

        /// <summary>The tags that open and close the faction list of a save.</summary>
        private const string AllFactionsOpenTag = "<allFactions>";
        private const string AllFactionsCloseTag = "</allFactions>";

        private readonly IPathService _pathService;
        private readonly ILoggerService _logger;

        public SaveHeaderService(IPathService pathService, ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Test seam: overrides the cap of the &lt;meta&gt; window (default 4 MB).
        /// </summary>
        internal int? TestMetaCapBytes { get; set; }

        /// <summary>
        /// Test seam: overrides the ceiling of the head window (default 64 MB).
        /// </summary>
        internal int? TestDetailsCapBytes { get; set; }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ListSavesAsync started", nameof(SaveHeaderService));

            try
            {
                var entries = await Task.Run(() => ListSavesCore(cancellationToken), cancellationToken).ConfigureAwait(false);
                _logger.LogInfo($"ListSavesAsync completed: {entries.Count} save(s)", nameof(SaveHeaderService));
                return entries;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"ListSavesAsync failed: {ex.Message}", nameof(SaveHeaderService));
                return Array.Empty<SaveFileEntry>();
            }
        }

        private IReadOnlyList<SaveFileEntry> ListSavesCore(CancellationToken cancellationToken)
        {
            string? savesPath = _pathService.GetSavesPath();
            if (string.IsNullOrWhiteSpace(savesPath) || !Directory.Exists(savesPath))
            {
                return Array.Empty<SaveFileEntry>();
            }

            var entries = new List<SaveFileEntry>();
            foreach (FileInfo file in new DirectoryInfo(savesPath).GetFiles("*.rws", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    entries.Add(new SaveFileEntry(
                        Path.GetFileNameWithoutExtension(file.Name),
                        file.FullName,
                        file.Length,
                        file.LastWriteTimeUtc));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A save that cannot be stat'ed is skipped - the list stays usable.
                    _logger.LogWarning($"Skipping unreadable save file '{file.FullName}': {ex.Message}", nameof(SaveHeaderService));
                }
            }

            return entries
                .OrderByDescending(entry => entry.LastModifiedUtc)
                .ToList();
        }

        /// <inheritdoc />
        public async Task<SaveHeader?> ReadHeaderAsync(string filePath, CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ReadHeaderAsync started", nameof(SaveHeaderService));

            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return null;
                }

                int capBytes = TestMetaCapBytes ?? DefaultMetaCapBytes;
                HeadWindow? window = await Task.Run(
                        () => ReadWindowCore(filePath, capBytes, MetaEndMarker, tailMarker: null, ignoreCase: true, cancellationToken),
                        cancellationToken).ConfigureAwait(false);

                if (window is null)
                {
                    _logger.LogWarning($"ReadHeaderAsync: '{filePath}' could not be read", nameof(SaveHeaderService));
                    return null;
                }

                if (window.Stop != HeadStop.MarkerFound)
                {
                    // No </meta> inside the cap: the file is truncated, corrupt or not a save.
                    _logger.LogWarning($"ReadHeaderAsync: '{filePath}' has no readable meta block", nameof(SaveHeaderService));
                    return null;
                }

                string text = DecodeText(window.Bytes, 0, window.Length);
                XElement? meta = ExtractMeta(text);
                if (meta is null)
                {
                    _logger.LogWarning($"ReadHeaderAsync: '{filePath}' has an unreadable meta block", nameof(SaveHeaderService));
                    return null;
                }

                var header = new SaveHeader(
                    Path.GetFileNameWithoutExtension(filePath),
                    filePath,
                    meta.Element("gameVersion")?.Value.Trim() ?? string.Empty,
                    ReadList(meta, "modIds"),
                    ReadList(meta, "modSteamIds"),
                    ReadList(meta, "modNames"));

                _logger.LogInfo("ReadHeaderAsync completed", nameof(SaveHeaderService));
                return header;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ReadHeaderAsync failed for '{filePath}': {ex.Message}", nameof(SaveHeaderService));
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<SaveDetails?> ReadDetailsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ReadDetailsAsync started", nameof(SaveHeaderService));

            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return null;
                }

                int maxBytes = TestDetailsCapBytes ?? MaxHeadWindowBytes;
                HeadWindow? window = await Task.Run(
                        () => ReadWindowCore(filePath, maxBytes, WorldStartMarker, WorldInfoEndMarker, ignoreCase: false, cancellationToken),
                        cancellationToken).ConfigureAwait(false);

                if (window is null)
                {
                    _logger.LogWarning($"ReadDetailsAsync: '{filePath}' could not be read", nameof(SaveHeaderService));
                    return null;
                }

                byte[] headBytes;
                string worldTail = string.Empty;
                if (window.Stop == HeadStop.MarkerFound)
                {
                    // The head ends where <world> begins - the window stays in the head.
                    headBytes = window.Bytes.Take(window.MarkerOffset).ToArray();

                    // The read continues behind the marker until the </info> tag
                    // closes the world info block - that block carries the world
                    // name and the world seed.
                    if (window.TailOffset > window.MarkerOffset)
                    {
                        worldTail = DecodeText(window.Bytes, window.MarkerOffset, window.TailOffset - window.MarkerOffset);
                    }
                }
                else if (window.Stop == HeadStop.EndOfFile)
                {
                    // The whole file was read - it must be a complete document.
                    string wholeFile = DecodeText(window.Bytes, 0, window.Length).TrimEnd();
                    if (!wholeFile.EndsWith("</savegame>", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning($"ReadDetailsAsync: '{filePath}' is truncated", nameof(SaveHeaderService));
                        return null;
                    }

                    headBytes = window.Bytes.Take(window.Length).ToArray();
                }
                else
                {
                    // The cap was reached before the head ended - the window is incomplete.
                    _logger.LogWarning($"ReadDetailsAsync: '{filePath}' head window is incomplete", nameof(SaveHeaderService));
                    return null;
                }

                string head = DecodeText(headBytes, 0, headBytes.Length);
                if (!ContainsTagStart(head, "game"))
                {
                    _logger.LogWarning($"ReadDetailsAsync: '{filePath}' has no game block", nameof(SaveHeaderService));
                    return null;
                }

                SaveDetails details = ParseDetails(head, worldTail);
                _logger.LogInfo("ReadDetailsAsync completed", nameof(SaveHeaderService));
                return details;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ReadDetailsAsync failed for '{filePath}': {ex.Message}", nameof(SaveHeaderService));
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SaveColonist>> ReadColonistNamesAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("ReadColonistNamesAsync started", nameof(SaveHeaderService));

            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Array.Empty<SaveColonist>();
                }

                var colonists = await Task.Run(
                        () => ScanColonists(filePath, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInfo($"ReadColonistNamesAsync completed: {colonists.Count} colonist(s)", nameof(SaveHeaderService));
                return colonists;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ReadColonistNamesAsync failed for '{filePath}': {ex.Message}", nameof(SaveHeaderService));
                return Array.Empty<SaveColonist>();
            }
        }

        /// <inheritdoc />
        public SaveCompat Compare(SaveHeader header,
            IReadOnlyCollection<string> activeModIds,
            IReadOnlyCollection<string> allInstalledModIds)
        {
            ArgumentNullException.ThrowIfNull(header);

            if (header.ModIds.Count == 0)
            {
                // No mods recorded - compatibility cannot be judged.
                return SaveCompat.Unknown;
            }

            var saveIds = ToIdSet(header.ModIds);
            var activeIds = ToIdSet(activeModIds);
            var installedIds = ToIdSet(allInstalledModIds);

            if (saveIds.Any(id => !installedIds.Contains(id)))
            {
                return SaveCompat.Uninstalled;
            }

            if (!saveIds.SetEquals(activeIds))
            {
                return SaveCompat.Changed;
            }

            // The save records its mods in load order - a different order is a
            // different list, even when the mods are exactly the same.
            if (!AreSameOrder(header.ModIds, activeModIds))
            {
                return SaveCompat.Changed;
            }

            return SaveCompat.Compatible;
        }

        /// <inheritdoc />
        public (IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Reordered) Diff(
            SaveHeader header, IReadOnlyCollection<string> activeModIds)
        {
            ArgumentNullException.ThrowIfNull(header);

            var saveIds = ToIdSet(header.ModIds);
            var activeIds = ToIdSet(activeModIds);

            var added = activeIds.Where(id => !saveIds.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToList();
            var removed = saveIds.Where(id => !activeIds.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToList();
            var reordered = ComputeReorderedIds(header.ModIds, activeModIds);

            return (added, removed, reordered);
        }

        /// <summary>
        /// True when the two lists hold the same mods in the same order, compared
        /// without regard to case - the way the game treats package ids.
        /// </summary>
        private static bool AreSameOrder(IReadOnlyList<string>? saveIds, IReadOnlyCollection<string>? activeIds)
        {
            int saveCount = saveIds?.Count ?? 0;
            int activeCount = activeIds?.Count ?? 0;
            if (saveCount != activeCount)
            {
                return false;
            }

            if (saveIds == null || activeIds == null)
            {
                return true;
            }

            var activeList = activeIds as IReadOnlyList<string> ?? activeIds.ToList();
            for (int i = 0; i < saveIds.Count; i++)
            {
                if (!string.Equals(saveIds[i], activeList[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The mods whose load-order position differs between the save and the
        /// active list, judged by their neighbours: a mod is reordered when the
        /// mods around it - among the mods both lists share - are not the same.
        /// Adding or removing a mod only marks the mods directly beside the
        /// change, not every mod after it.
        /// </summary>
        private static IReadOnlyList<string> ComputeReorderedIds(
            IReadOnlyList<string>? saveIds, IReadOnlyCollection<string>? activeIds)
        {
            if (saveIds == null || activeIds == null)
            {
                return Array.Empty<string>();
            }

            var activeSet = ToIdSet(activeIds);
            var saveSet = ToIdSet(saveIds);

            // Only the mods the two lists share have a comparable position; the
            // rest is added or removed and has a state of its own.
            var saveCommon = saveIds.Where(id => id != null && activeSet.Contains(id)).ToList();
            var activeCommon = activeIds.Where(id => id != null && saveSet.Contains(id)).ToList();
            if (saveCommon.Count != activeCommon.Count)
            {
                // The sets agree but the filtered lists do not - duplicate or
                // case-mixed ids. Without a reliable pairing nothing is judged.
                return Array.Empty<string>();
            }

            // The position of every shared mod in the active list.
            var activeIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < activeCommon.Count; i++)
            {
                activeIndex.TryAdd(activeCommon[i], i);
            }

            var reordered = new List<string>();
            for (int i = 0; i < saveCommon.Count; i++)
            {
                if (!activeIndex.TryGetValue(saveCommon[i], out int j))
                {
                    continue; // Unreachable when the counts agree - kept for safety.
                }

                string? savePrev = i > 0 ? saveCommon[i - 1] : null;
                string? saveNext = i + 1 < saveCommon.Count ? saveCommon[i + 1] : null;
                string? activePrev = j > 0 ? activeCommon[j - 1] : null;
                string? activeNext = j + 1 < activeCommon.Count ? activeCommon[j + 1] : null;

                bool sameBefore = string.Equals(savePrev, activePrev, StringComparison.OrdinalIgnoreCase);
                bool sameAfter = string.Equals(saveNext, activeNext, StringComparison.OrdinalIgnoreCase);
                if (!sameBefore || !sameAfter)
                {
                    reordered.Add(saveCommon[i]);
                }
            }

            return reordered;
        }

        /// <summary>
        /// Builds a case-insensitive set of mod ids. Package ids are compared without
        /// regard to case, the same way the game treats them.
        /// </summary>
        private static HashSet<string> ToIdSet(IReadOnlyCollection<string>? ids)
            => new HashSet<string>(ids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public static string? FormatInGameDate(int? ticksGame, long? gameStartAbsTick, int? startingYear)
            => ISaveHeaderService.FormatInGameDate(ticksGame, gameStartAbsTick, startingYear);

        /// <inheritdoc />
        public static IReadOnlyList<string> ResolveActiveDlcNames(IReadOnlyCollection<string> modIds)
            => ISaveHeaderService.ResolveActiveDlcNames(modIds);

        #region Streaming core

        private enum HeadStop
        {
            /// <summary>The end of the file was reached.</summary>
            EndOfFile,

            /// <summary>The stop marker was found inside the window.</summary>
            MarkerFound,

            /// <summary>The cap was reached before the stop marker appeared.</summary>
            CapReached
        }

        private sealed class HeadWindow
        {
            public byte[] Bytes { get; init; } = Array.Empty<byte>();
            public int Length { get; init; }
            public HeadStop Stop { get; init; }
            public int MarkerOffset { get; init; } = -1;

            /// <summary>
            /// The end of the tail behind the marker - the position just after
            /// the closing tag the tail marker ended at. -1 when the tail was
            /// not found (the file ended or the cap was reached first).
            /// </summary>
            public int TailOffset { get; init; } = -1;
        }

        /// <summary>
        /// Reads at most <paramref name="maxBytes"/> bytes from the head of a file and
        /// stops as soon as the marker appears in the accumulated window. When a tail
        /// marker is given, the read does not stop at the marker: it continues until
        /// the tail marker appears behind it - that is how the world info block right
        /// behind the &lt;world&gt; marker is read.
        /// Gzip files are detected by their magic bytes and decompressed on the fly -
        /// only the head is ever decompressed.
        /// Returns null when the file cannot be opened or read.
        /// </summary>
        private HeadWindow? ReadWindowCore(string filePath, int maxBytes, byte[] marker,
            byte[]? tailMarker, bool ignoreCase, CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                return null;
            }

            byte[] buffer = new byte[Math.Min(maxBytes, ChunkSize * 2)];
            int length = 0;

            // The positions the markers are searched from. Both only move
            // forward, so every byte of the window is looked at once.
            int markerSearchFrom = 0;
            int tailSearchFrom = 0;
            int markerOffset = -1;
            int tailOffset = -1;

            try
            {
                using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    ChunkSize, useAsync: false);

                var probe = new byte[2];
                int probed = file.Read(probe, 0, 2);
                bool isGzip = probed == 2 && probe[0] == 0x1F && probe[1] == 0x8B;
                file.Seek(0, SeekOrigin.Begin);

                Stream source = file;
                GZipStream? gzip = null;
                if (isGzip)
                {
                    gzip = new GZipStream(file, CompressionMode.Decompress);
                    source = gzip;
                }

                try
                {
                    byte[] chunk = new byte[ChunkSize];

                    while (length < maxBytes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int wanted = Math.Min(chunk.Length, maxBytes - length);
                        int read = source.Read(chunk, 0, wanted);
                        if (read <= 0)
                        {
                            break; // End of file.
                        }

                        if (length + read > buffer.Length)
                        {
                            int newSize = (int)Math.Min((long)maxBytes,
                                Math.Max((long)buffer.Length * 2, length + (long)read));
                            Array.Resize(ref buffer, newSize);
                        }

                        Array.Copy(chunk, 0, buffer, length, read);
                        length += read;

                        // The stop marker: the head ends where it appears.
                        if (markerOffset < 0)
                        {
                            markerOffset = IndexOfBytes(buffer, length, marker, markerSearchFrom, ignoreCase);
                            markerSearchFrom = Math.Max(markerSearchFrom, length - marker.Length + 1);

                            if (markerOffset >= 0)
                            {
                                if (tailMarker is null)
                                {
                                    return new HeadWindow
                                    {
                                        Bytes = buffer,
                                        Length = length,
                                        Stop = HeadStop.MarkerFound,
                                        MarkerOffset = markerOffset,
                                        TailOffset = -1
                                    };
                                }

                                // The tail is searched behind the marker only - the
                                // info block of the game block closes long before it.
                                tailSearchFrom = markerOffset;
                            }
                        }

                        // The tail marker: the window continues until it closes.
                        if (markerOffset >= 0 && tailOffset < 0 && tailMarker is not null)
                        {
                            int tailIndex = IndexOfBytes(buffer, length, tailMarker, tailSearchFrom, ignoreCase);
                            tailSearchFrom = Math.Max(tailSearchFrom, length - tailMarker.Length + 1);

                            if (tailIndex >= 0)
                            {
                                tailOffset = tailIndex + tailMarker.Length;
                                return new HeadWindow
                                {
                                    Bytes = buffer,
                                    Length = length,
                                    Stop = HeadStop.MarkerFound,
                                    MarkerOffset = markerOffset,
                                    TailOffset = tailOffset
                                };
                            }
                        }
                    }

                    if (markerOffset >= 0)
                    {
                        // The head itself is complete - only the tail behind the
                        // marker is missing (the file ended or the cap was reached).
                        return new HeadWindow
                        {
                            Bytes = buffer,
                            Length = length,
                            Stop = HeadStop.MarkerFound,
                            MarkerOffset = markerOffset,
                            TailOffset = tailOffset
                        };
                    }

                    return new HeadWindow
                    {
                        Bytes = buffer,
                        Length = length,
                        Stop = length >= maxBytes ? HeadStop.CapReached : HeadStop.EndOfFile
                    };
                }
                finally
                {
                    gzip?.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Unreadable, locked, corrupt or unsupported file - the caller reports "unreadable".
                return null;
            }
        }

        private static int IndexOfBytes(byte[] buffer, int length, byte[] needle, int startIndex, bool ignoreCase)
        {
            if (needle.Length == 0 || length < needle.Length)
            {
                return -1;
            }

            int limit = length - needle.Length;
            for (int i = Math.Max(0, startIndex); i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    byte actual = buffer[i + j];
                    byte expected = needle[j];
                    if (ignoreCase)
                    {
                        if (actual >= (byte)'A' && actual <= (byte)'Z')
                        {
                            actual = (byte)(actual + 32);
                        }

                        if (expected >= (byte)'A' && expected <= (byte)'Z')
                        {
                            expected = (byte)(expected + 32);
                        }
                    }

                    if (actual != expected)
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string DecodeText(byte[] buffer, int offset, int length)
        {
            if (offset == 0 && length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                offset = 3; // UTF-8 BOM
                length -= 3;
            }

            return Encoding.UTF8.GetString(buffer, offset, length);
        }

        #endregion

        #region Meta parsing

        private static XElement? ExtractMeta(string text)
        {
            int start = text.IndexOf("<meta>", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            int end = text.IndexOf("</meta>", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return null;
            }

            string fragment = text.Substring(start, (end + "</meta>".Length) - start);
            try
            {
                return XElement.Parse(fragment);
            }
            catch (XmlException)
            {
                return null;
            }
        }

        private static IReadOnlyList<string> ReadList(XElement meta, string elementName)
        {
            XElement? element = meta.Element(elementName);
            if (element is null)
            {
                return Array.Empty<string>();
            }

            // Every <li> is kept - even an empty one - so the mod id, Workshop id and
            // mod name lists stay index aligned with each other.
            return element.Elements("li").Select(li => li.Value.Trim()).ToList();
        }

        #endregion

        #region Head window parsing

        private static SaveDetails ParseDetails(string head, string worldTail)
        {
            XElement? scenario = ParseFragment(ExtractElementXml(head, "scenario"));
            XElement? storyteller = ParseFragment(ExtractElementXml(head, "storyteller"));
            XElement? tickManager = ParseFragment(ExtractElementXml(head, "tickManager"));
            XElement? info = ParseFragment(ExtractElementXml(head, "info"));
            XElement? dateNotifier = ParseFragment(ExtractElementXml(head, "dateNotifier"));

            // The world info block sits behind the <world> marker - the head
            // window reads up to the </info> tag that closes it.
            XElement? worldInfo = ParseFragment(ExtractElementXml(worldTail, "info"));

            // The scenario carries the story of the colony: the summary, the
            // description, the faction of the player and the parts that say
            // whether the colony plays on a planet.
            string? scenarioSummary = TrimmedValue(scenario?.Element("summary"));
            string? scenarioDescription = TrimmedValue(scenario?.Element("description"));
            string? playerFactionType = TrimmedValue(scenario?.Element("playerFaction")?.Element("factionDef"));

            bool? isPlanetMode = null;
            XElement? scenarioParts = scenario?.Element("parts");
            if (scenarioParts is not null)
            {
                bool planetLayer = false;
                foreach (XElement part in scenarioParts.Elements("li"))
                {
                    string? partClass = TrimmedValue(part.Attribute("Class"));

                    if (string.Equals(partClass, "ScenPart_PlanetLayer", StringComparison.Ordinal))
                    {
                        planetLayer = true;
                    }
                }

                // The parts are there - the planet layer is either there or not.
                isPlanetMode = planetLayer;
            }

            // The DLC list is not read from the head window: it is derived from
            // the mod ids of the meta block and filled in by the caller.
            return new SaveDetails(
                TrimmedValue(scenario?.Element("name")),
                TrimmedValue(storyteller?.Element("def")),
                ResolveDifficulty(storyteller),
                ParseInt(tickManager?.Element("ticksGame")),
                ParseLong(tickManager?.Element("gameStartAbsTick")),
                ParseInt(tickManager?.Element("startingYear")),
                TrimmedValue(info?.Element("startingTile")),
                TrimmedValue(dateNotifier?.Element("lastSeason")),
                TrimmedValue(worldInfo?.Element("name")),
                TrimmedValue(worldInfo?.Element("seedString")),
                playerFactionType,
                scenarioSummary,
                scenarioDescription,
                isPlanetMode,
                Array.Empty<string>());
        }

        /// <summary>
        /// Resolves the difficulty of the colony. The verified layout stores the
        /// difficulty def inside &lt;storyteller&gt;&lt;difficulty&gt;; a layout that
        /// only carries the difficulty name is read as well.
        /// </summary>
        private static string? ResolveDifficulty(XElement? storyteller)
        {
            XElement? difficulty = storyteller?.Element("difficulty");
            if (difficulty is null)
            {
                return null;
            }

            return TrimmedValue(difficulty.Element("def")) ?? TrimmedValue(difficulty);
        }

        private static string? TrimmedValue(XElement? element)
        {
            string? value = element?.Value;
            if (value is null)
            {
                return null;
            }

            string trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static string? TrimmedValue(XAttribute? attribute)
        {
            string? value = attribute?.Value;
            if (value is null)
            {
                return null;
            }

            string trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static int? ParseInt(XElement? element)
        {
            string? value = TrimmedValue(element);
            if (value is null)
            {
                return null;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
        }

        private static long? ParseLong(XElement? element)
        {
            string? value = TrimmedValue(element);
            if (value is null)
            {
                return null;
            }

            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
        }

        /// <summary>
        /// Extracts the complete element (including its children) from a text window.
        /// Returns null when the element is not present or not closed inside the window.
        /// </summary>
        private static string? ExtractElementXml(string text, string elementName)
        {
            int start = IndexOfTagStart(text, elementName, 0);
            if (start < 0)
            {
                return null;
            }

            int depth = 0;
            int position = start;

            while (position < text.Length)
            {
                int open = text.IndexOf('<', position);
                if (open < 0)
                {
                    return null; // Never closed.
                }

                if (open + 3 < text.Length && string.CompareOrdinal(text, open, "<!--", 0, 4) == 0)
                {
                    int commentEnd = text.IndexOf("-->", open, StringComparison.Ordinal);
                    if (commentEnd < 0)
                    {
                        return null;
                    }

                    position = commentEnd + 3;
                    continue;
                }

                if (open + 1 < text.Length && (text[open + 1] == '?' || text[open + 1] == '!'))
                {
                    int declarationEnd = text.IndexOf('>', open);
                    if (declarationEnd < 0)
                    {
                        return null;
                    }

                    position = declarationEnd + 1;
                    continue;
                }

                int close = text.IndexOf('>', open);
                if (close < 0)
                {
                    return null; // Truncated inside the tag.
                }

                string inner = text.Substring(open + 1, close - open - 1).Trim();
                bool isClosing = inner.StartsWith("/", StringComparison.Ordinal);
                bool isSelfClosing = inner.EndsWith("/", StringComparison.Ordinal);
                string tagName = inner.TrimStart('/').TrimEnd('/').Trim();

                int separator = tagName.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                string baseName = separator < 0 ? tagName : tagName.Substring(0, separator);

                if (string.Equals(baseName, elementName, StringComparison.Ordinal))
                {
                    if (isClosing)
                    {
                        depth--;
                    }
                    else if (!isSelfClosing)
                    {
                        depth++;
                    }

                    if (depth == 0)
                    {
                        return text.Substring(start, (close + 1) - start);
                    }
                }

                position = close + 1;
            }

            return null;
        }

        private static bool ContainsTagStart(string text, string elementName)
            => IndexOfTagStart(text, elementName, 0) >= 0;

        /// <summary>
        /// Finds the start of an element ("&lt;name" followed by a delimiter).
        /// Closing tags are skipped.
        /// </summary>
        private static int IndexOfTagStart(string text, string elementName, int startIndex)
        {
            int position = Math.Max(0, startIndex);

            while (position < text.Length)
            {
                int open = text.IndexOf('<', position);
                if (open < 0)
                {
                    return -1;
                }

                if (open + 1 < text.Length && text[open + 1] == '/')
                {
                    position = open + 2;
                    continue;
                }

                if (open + 1 + elementName.Length <= text.Length &&
                    string.CompareOrdinal(text, open + 1, elementName, 0, elementName.Length) == 0)
                {
                    int after = open + 1 + elementName.Length;
                    if (after >= text.Length)
                    {
                        return -1;
                    }

                    char delimiter = text[after];
                    if (delimiter == '>' || delimiter == '/' || char.IsWhiteSpace(delimiter))
                    {
                        return open;
                    }
                }

                position = open + 1;
            }

            return -1;
        }

        private static XElement? ParseFragment(string? fragment)
        {
            if (string.IsNullOrEmpty(fragment))
            {
                return null;
            }

            try
            {
                return XElement.Parse(fragment);
            }
            catch (XmlException)
            {
                return null;
            }
        }

        #endregion

        #region Colonist scan

        /// <summary>
        /// The ticks of a RimWorld year (60 in-game days). Verified against the
        /// game's own ages of the pawns (6/6 on both axes).
        /// </summary>
        internal const long TicksPerYear = 3_600_000;

        /// <summary>
        /// The gender a human pawn has when the save does not name one. The
        /// scribe omits the element when the value equals its default - and
        /// the default of a human is Male (corpus evidence: 16 explicit
        /// "Female", 0 explicit "Male", 10 absent, every one of them a
        /// male-name pawn).
        /// </summary>
        private const string DefaultHumanGender = "Male";

        /// <summary>
        /// The vanilla skills of a pawn. The whole-file skill set of the
        /// measured saves is exactly these twelve - no mod skill was ever
        /// seen - so everything else is dropped.
        /// </summary>
        private static readonly string[] VanillaSkillDefs =
        {
            "Animals", "Artistic", "Construction", "Cooking", "Crafting", "Intellectual",
            "Medicine", "Melee", "Mining", "Plants", "Shooting", "Social"
        };

        /// <summary>
        /// Scans the whole file for the human pawns and returns what each of
        /// them carries. The file is streamed in 1 MB chunks - it is never held
        /// in memory as a whole. Only the human pawns are of interest: the game
        /// writes every pawn as a thing, and only the humans carry a human def
        /// right behind the thing tag.
        /// A map's thing list holds every map thing, so the marker alone would
        /// catch raiders, guests, traders and prisoners as well. The player
        /// faction is therefore resolved first and only the living members of
        /// the colony - the colonists and the slaves - are kept. A save whose
        /// player faction cannot be resolved lists nobody.
        /// The file is only read.
        /// </summary>
        private static IReadOnlyList<SaveColonist> ScanColonists(string filePath,
            CancellationToken cancellationToken)
        {
            // Without the faction of the player no pawn can be classified, and
            // an unclassifiable save is never guessed at.
            string? playerFactionId = ResolvePlayerFactionId(filePath, cancellationToken);
            if (playerFactionId is null)
            {
                return Array.Empty<SaveColonist>();
            }

            var colonists = new List<SaveColonist>();

            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, useAsync: false);

            var decoder = Encoding.UTF8.GetDecoder();
            byte[] byteBuffer = new byte[ColonistScanChunkBytes];
            char[] charBuffer = new char[ColonistScanChunkBytes + 8];

            string buffer = string.Empty;
            int scan = 0;
            bool endOfFile = false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!endOfFile)
                {
                    int read = file.Read(byteBuffer, 0, byteBuffer.Length);
                    if (read <= 0)
                    {
                        endOfFile = true;

                        // The last characters may be an incomplete sequence.
                        int flushed = decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, true);
                        if (flushed > 0)
                        {
                            buffer += new string(charBuffer, 0, flushed);
                        }
                    }
                    else
                    {
                        int charCount = decoder.GetChars(byteBuffer, 0, read, charBuffer, 0, false);
                        if (charCount > 0)
                        {
                            buffer += new string(charBuffer, 0, charCount);
                        }
                    }
                }

                bool progressed = false;

                while (true)
                {
                    int thingStart = IndexOfThingTag(buffer, scan);
                    if (thingStart < 0)
                    {
                        // The tail that could still be the start of a tag is kept.
                        scan = Math.Max(scan, buffer.Length - 32);
                        break;
                    }

                    int tagEnd = buffer.IndexOf('>', thingStart);
                    if (tagEnd < 0)
                    {
                        break; // The tag is not complete yet - more data is needed.
                    }

                    string tag = buffer.Substring(thingStart, tagEnd - thingStart + 1);
                    if (!IsPawnTag(tag))
                    {
                        scan = tagEnd + 1;
                        progressed = true;
                        continue;
                    }

                    int blockEnd = FindThingBlockEnd(buffer, tagEnd + 1);
                    if (blockEnd < 0)
                    {
                        break; // The block is not complete yet - more data is needed.
                    }

                    string block = buffer.Substring(thingStart, blockEnd - thingStart);
                    if (IsHumanPawn(block) && IsColonyMember(block, playerFactionId))
                    {
                        SaveColonist? colonist = ParseColonist(block);
                        if (colonist is not null)
                        {
                            colonists.Add(colonist);
                        }
                    }

                    scan = blockEnd;
                    progressed = true;
                }

                if (endOfFile && !progressed)
                {
                    break;
                }

                // The consumed part of the buffer is dropped - only the tail
                // that is still needed is kept.
                if (scan > 0)
                {
                    buffer = buffer.Substring(scan);
                    scan = 0;
                }
            }

            return colonists;
        }

        /// <summary>
        /// Resolves the id of the player faction - "Faction_" plus the loadID
        /// of the PlayerColony/PlayerTribe faction - with a bounded pre-pass
        /// over the head of the file. Every pawn writes its faction as that id,
        /// so it is the only way to tell a colonist from a raider, a guest, a
        /// trader or a prisoner.
        /// The read stops as soon as the id is resolved (the faction manager of
        /// the measured saves sits at ~0.9 MB, far before the maps) and on
        /// cancellation.
        /// Returns null when the save carries no player faction with a loadID:
        /// the caller then lists nobody - an unclassifiable save is never
        /// guessed at. A faction whose loadID equals the scribe default is
        /// written without the element at all (measured: the loadID-0
        /// OutlanderCivil faction of New Arrivals4), and such a player faction
        /// cannot be resolved either.
        /// The file is only read.
        /// </summary>
        private static string? ResolvePlayerFactionId(string filePath,
            CancellationToken cancellationToken)
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, useAsync: false);

            var decoder = Encoding.UTF8.GetDecoder();
            byte[] byteBuffer = new byte[ColonistScanChunkBytes];
            char[] charBuffer = new char[ColonistScanChunkBytes + 8];

            // The window holds what is still interesting: the tail that could
            // be the start of the "<allFactions>" marker before it is found,
            // the faction list itself behind it. Everything before the list is
            // dropped, so the window stays small.
            string window = string.Empty;
            int cursor = 0;
            bool listFound = false;
            long total = 0;
            bool endOfFile = false;

            while (!endOfFile)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = file.Read(byteBuffer, 0, byteBuffer.Length);
                if (read <= 0)
                {
                    endOfFile = true;

                    // The last characters may be an incomplete sequence.
                    int flushed = decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, true);
                    if (flushed > 0)
                    {
                        window += new string(charBuffer, 0, flushed);
                    }
                }
                else
                {
                    total += read;
                    int charCount = decoder.GetChars(byteBuffer, 0, read, charBuffer, 0, false);
                    if (charCount > 0)
                    {
                        window += new string(charBuffer, 0, charCount);
                    }
                }

                if (!listFound)
                {
                    int listStart = window.IndexOf(AllFactionsOpenTag, StringComparison.Ordinal);
                    if (listStart < 0)
                    {
                        // The tail that could still be the start of the marker
                        // is kept - the rest of the head is not interesting.
                        if (window.Length >= AllFactionsOpenTag.Length)
                        {
                            window = window.Substring(window.Length - AllFactionsOpenTag.Length + 1);
                        }

                        if (total > PlayerFactionScanCapBytes)
                        {
                            return null;
                        }

                        continue;
                    }

                    // The list itself is the only thing that is read to the end.
                    window = window.Substring(listStart);
                    cursor = AllFactionsOpenTag.Length;
                    listFound = true;
                }

                // The items of the list are walked one by one. A faction item
                // that carries no player def is dropped behind the cursor, so
                // every character of the list is looked at once.
                while (true)
                {
                    int listEnd = window.IndexOf(AllFactionsCloseTag, cursor, StringComparison.Ordinal);
                    int item = window.IndexOf("<li", cursor, StringComparison.Ordinal);

                    if (item >= 0 && (listEnd < 0 || item < listEnd))
                    {
                        int itemEnd = FindListItemEnd(window, item);
                        if (itemEnd < 0)
                        {
                            break; // The item is not complete yet - more data is needed.
                        }

                        string? loadId = PlayerFactionLoadId(window, item, itemEnd);
                        if (loadId is not null)
                        {
                            return "Faction_" + loadId;
                        }

                        cursor = itemEnd;
                        continue;
                    }

                    if (listEnd >= 0)
                    {
                        // The list ended without a player faction.
                        return null;
                    }

                    break; // More data is needed.
                }

                if (total > PlayerFactionScanCapBytes)
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// The index behind the end of the list item that opens at the given
        /// position: behind its closing tag, or behind the tag itself when the
        /// item is empty. Returns -1 when the item is not complete yet.
        /// </summary>
        private static int FindListItemEnd(string buffer, int itemStart)
        {
            int tagEnd = buffer.IndexOf('>', itemStart);
            if (tagEnd < 0)
            {
                return -1;
            }

            if (buffer[tagEnd - 1] == '/')
            {
                return tagEnd + 1; // An empty item.
            }

            int close = buffer.IndexOf("</li>", tagEnd, StringComparison.Ordinal);
            return close < 0 ? -1 : close + "</li>".Length;
        }

        /// <summary>
        /// The loadID of the player faction inside the given list item, or null
        /// when the item is no player faction or carries no loadID. The defs of
        /// the player faction are exactly "PlayerColony" and "PlayerTribe".
        /// The loadID is read behind the def and only inside the item, so a
        /// loadID that belongs to the next faction is never taken.
        /// </summary>
        private static string? PlayerFactionLoadId(string window, int itemStart, int itemEnd)
        {
            int def = window.IndexOf("<def>PlayerColony</def>", itemStart, StringComparison.Ordinal);
            if (def < 0 || def >= itemEnd)
            {
                def = window.IndexOf("<def>PlayerTribe</def>", itemStart, StringComparison.Ordinal);
                if (def < 0 || def >= itemEnd)
                {
                    return null;
                }
            }

            int loadId = window.IndexOf("<loadID>", def, StringComparison.Ordinal);
            if (loadId < 0 || loadId >= itemEnd)
            {
                return null;
            }

            int textStart = loadId + "<loadID>".Length;
            int textEnd = window.IndexOf("</loadID>", textStart, StringComparison.Ordinal);
            if (textEnd < 0 || textEnd > itemEnd)
            {
                return null;
            }

            string id = window.Substring(textStart, textEnd - textStart).Trim();
            return id.Length == 0 ? null : id;
        }

        /// <summary>
        /// Finds the next thing tag ("&lt;thing", never "&lt;thingDef" or
        /// "&lt;things&gt;") at or after the given position.
        /// </summary>
        private static int IndexOfThingTag(string buffer, int startIndex)
        {
            int position = Math.Max(0, startIndex);

            while (position < buffer.Length)
            {
                int open = buffer.IndexOf("<thing", position, StringComparison.Ordinal);
                if (open < 0)
                {
                    return -1;
                }

                int after = open + "<thing".Length;
                if (after >= buffer.Length)
                {
                    return -1;
                }

                char delimiter = buffer[after];
                if (delimiter == '>' || delimiter == '/' || char.IsWhiteSpace(delimiter))
                {
                    return open;
                }

                position = open + 1;
            }

            return -1;
        }

        /// <summary>True when the tag opens a pawn thing.</summary>
        private static bool IsPawnTag(string tag)
            => tag.Contains("Class=\"Pawn\"", StringComparison.Ordinal);

        /// <summary>
        /// Finds the end of a thing block: the tag that closes it. Things do
        /// not nest in a save, but the counter keeps the scan honest anyway.
        /// Returns -1 when the block is not complete in the buffer.
        /// </summary>
        private static int FindThingBlockEnd(string buffer, int from)
        {
            int depth = 1;
            int position = from;

            while (position < buffer.Length)
            {
                int open = buffer.IndexOf('<', position);
                if (open < 0)
                {
                    return -1;
                }

                if (open + 1 < buffer.Length && buffer[open + 1] == '/')
                {
                    int close = buffer.IndexOf('>', open);
                    if (close < 0)
                    {
                        return -1;
                    }

                    if (string.Equals(buffer.Substring(open + 2, close - open - 2).Trim(), "thing",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return close + 1;
                        }
                    }

                    position = close + 1;
                    continue;
                }

                int tagEnd = buffer.IndexOf('>', open);
                if (tagEnd < 0)
                {
                    return -1;
                }

                string tag = buffer.Substring(open + 1, tagEnd - open - 1);
                if (!tag.TrimEnd().EndsWith("/", StringComparison.Ordinal)
                    && string.Equals(TagName(tag), "thing", StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                }

                position = tagEnd + 1;
            }

            return -1;
        }

        /// <summary>The name of a tag: the text before the first space.</summary>
        private static string TagName(string tag)
        {
            string trimmed = tag.Trim();
            int separator = trimmed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            return separator < 0 ? trimmed : trimmed.Substring(0, separator);
        }

        /// <summary>
        /// True when the thing block holds a human: the human def follows the
        /// thing tag directly. Every other pawn (an animal) carries its own
        /// def there.
        /// </summary>
        private static bool IsHumanPawn(string block)
        {
            int tagEnd = block.IndexOf('>');
            if (tagEnd < 0)
            {
                return false;
            }

            int position = tagEnd + 1;
            while (position < block.Length && char.IsWhiteSpace(block[position]))
            {
                position++;
            }

            return block.IndexOf("<def>Human</def>", position, StringComparison.Ordinal) == position;
        }

        /// <summary>
        /// True when the pawn block is a living member of the colony - a
        /// colonist or a slave. Three filters, all read from the block the scan
        /// already holds (no extra file access):
        /// F1 the pawn carries the faction of the player. A raider, a guest, a
        ///    visitor, a trader and a quest lodger keep their own faction, and
        ///    a pawn without a faction element carries none at all.
        /// F2 the pawn is not held by another faction. The scribe writes the
        ///    literal "null" for an absent reference, and a pawn without the
        ///    element carries no host faction either. A prisoner and a held
        ///    colonist carry a real one.
        /// F3 the pawn is no prisoner. The status element is omitted when it
        ///    equals its default, so only the explicit spelling is rejected.
        /// A slave carries the faction of the player and no host faction
        /// (Pawn_GuestTracker.SetFaction), so it passes all three.
        /// The map assertion is belt-and-braces: the inner pawn of a corpse is
        /// a loadID reference, never a nested &lt;thing Class="Pawn"&gt; (M6),
        /// so a matched block is never a corpse - but every serialized map
        /// thing writes its map reference, and a block without one is not
        /// accepted.
        /// The join status is deliberately not read: it is the recruitment
        /// intent, not the status of the pawn.
        /// </summary>
        private static bool IsColonyMember(string block, string playerFactionId)
        {
            // F1 - the faction of the player.
            if (!string.Equals(BlockElementText(block, "faction"), playerFactionId, StringComparison.Ordinal))
            {
                return false;
            }

            // F2 - not held by another faction.
            if (BlockElementText(block, "hostFaction") is not null)
            {
                return false;
            }

            // F3 - no prisoner.
            if (block.Contains("<guestStatus>Prisoner</guestStatus>", StringComparison.Ordinal))
            {
                return false;
            }

            // The defensive corpse assertion (M6).
            return block.Contains("<map>", StringComparison.Ordinal)
                || block.Contains("<map ", StringComparison.Ordinal);
        }

        /// <summary>
        /// The text of the first element with the given name inside a pawn
        /// block, read without parsing the XML. Null when the element is
        /// absent, when the scribe wrote it as an empty self-closing tag
        /// (IsNull="True"), or when it carries the literal "null" - the game
        /// writes that literal for an absent reference.
        /// </summary>
        private static string? BlockElementText(string block, string elementName)
        {
            string open = "<" + elementName;
            int position = block.IndexOf(open, StringComparison.Ordinal);

            while (position >= 0)
            {
                int afterTag = position + open.Length;
                if (afterTag < block.Length && block[afterTag] != '>' && !char.IsWhiteSpace(block[afterTag]))
                {
                    // A longer element name that starts the same way.
                    position = block.IndexOf(open, position + 1, StringComparison.Ordinal);
                    continue;
                }

                int tagEnd = block.IndexOf('>', afterTag);
                if (tagEnd < 0)
                {
                    return null;
                }

                if (block[tagEnd - 1] == '/')
                {
                    return null; // An empty element: <faction IsNull="True" />
                }

                string close = "</" + elementName + ">";
                int end = block.IndexOf(close, tagEnd, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }

                string value = block.Substring(tagEnd + 1, end - tagEnd - 1).Trim();
                if (value.Length == 0 || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return value;
            }

            return null;
        }

        /// <summary>
        /// Reads everything a human pawn carries: the name, the join status,
        /// the ages, the vanilla traits, the vanilla skills and the vanilla
        /// conditions. Everything is read from the block the scan already
        /// holds - there is no extra file access.
        /// Returns null when the block carries no name at all.
        /// </summary>
        private static SaveColonist? ParseColonist(string block)
        {
            XElement? pawn = TryParseFragment(block);

            if (pawn is null)
            {
                // The block did not parse - the name is still worth reading.
                SaveColonistNameParts? fallback = ExtractColonistNameParts(block);
                return fallback is null
                    ? null
                    : new SaveColonist(fallback.First, fallback.Nick, fallback.Last, null, null, null,
                        Array.Empty<string>(), Array.Empty<SaveColonistSkill>(), Array.Empty<SaveColonistCondition>());
            }

            SaveColonistNameParts? names = ExtractNameParts(pawn) ?? ExtractColonistNameParts(block);
            if (names is null)
            {
                return null;
            }

            // The gender is read as the save records it. The scribe omits the
            // element when the value equals its default - and the default of
            // a human is Male (corpus evidence: 16 explicit "Female", 0
            // explicit "Male", 10 absent, every one of them a male-name pawn).
            string? gender = ElementValue(pawn.Element("gender")) ?? DefaultHumanGender;

            // The xenotype (race) - it sits in the gene block of the pawn. A
            // save without Biotech content has no genes block at all, then
            // the line stays empty.
            string? xenotype = ElementValue(pawn.Element("genes")?.Element("xenotype"));
            if (xenotype is not null && IsModNamespace(xenotype))
            {
                // A mod xenotype is not shown: the vanilla-only policy holds
                // for it as well.
                xenotype = null;
            }

            return new SaveColonist(
                names.First,
                names.Nick,
                names.Last,
                gender,
                xenotype,
                ReadBiologicalAge(pawn),
                ReadTraits(pawn),
                ReadSkills(pawn),
                ReadConditions(pawn));
        }

        /// <summary>Parses a fragment of a save - null when it is not valid XML.</summary>
        private static XElement? TryParseFragment(string fragment)
        {
            try
            {
                return XElement.Parse(fragment);
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>
        /// The name parts of a pawn: the game writes them as the children of
        /// the name element; a name triple with the parts as attributes is
        /// read as well. A part the save does not carry stays empty.
        /// </summary>
        private static SaveColonistNameParts? ExtractNameParts(XElement pawn)
        {
            XElement? name = pawn.Element("name");
            if (name is not null)
            {
                string? first = TrimmedValue(name.Element("first"));
                string? nick = TrimmedValue(name.Element("nick"));
                string? last = TrimmedValue(name.Element("last"));

                if (first is not null || nick is not null || last is not null)
                {
                    return new SaveColonistNameParts(first, nick, last);
                }
            }

            XElement? triple = pawn.Descendants("nameTriple").FirstOrDefault();
            if (triple is not null)
            {
                string? first = TrimmedValue(triple.Attribute("first"));
                string? nick = TrimmedValue(triple.Attribute("nick"));
                string? last = TrimmedValue(triple.Attribute("last"));

                if (first is not null || nick is not null || last is not null)
                {
                    return new SaveColonistNameParts(first, nick, last);
                }
            }

            return null;
        }

        /// <summary>
        /// The name parts of a pawn block, read without parsing the XML - the
        /// fallback for a block that does not parse.
        /// </summary>
        private static SaveColonistNameParts? ExtractColonistNameParts(string block)
        {
            int nameStart = block.IndexOf("<name Class=\"NameTriple\"", StringComparison.Ordinal);
            if (nameStart >= 0)
            {
                int open = block.IndexOf('>', nameStart);
                if (open >= 0)
                {
                    int close = block.IndexOf("</name>", open, StringComparison.Ordinal);
                    string inner = close >= 0
                        ? block.Substring(open + 1, close - open - 1)
                        : block.Substring(open + 1);

                    string? first = ExtractElementText(inner, "first");
                    string? nick = ExtractElementText(inner, "nick");
                    string? last = ExtractElementText(inner, "last");

                    if (first is not null || nick is not null || last is not null)
                    {
                        return new SaveColonistNameParts(first, nick, last);
                    }
                }
            }

            int tripleStart = block.IndexOf("<nameTriple", StringComparison.Ordinal);
            if (tripleStart >= 0)
            {
                int end = block.IndexOf('>', tripleStart);
                if (end >= 0)
                {
                    string tag = block.Substring(tripleStart, end - tripleStart + 1);

                    string? first = ExtractAttributeValue(tag, "first");
                    string? nick = ExtractAttributeValue(tag, "nick");
                    string? last = ExtractAttributeValue(tag, "last");

                    if (first is not null || nick is not null || last is not null)
                    {
                        return new SaveColonistNameParts(first, nick, last);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The biological age of a pawn in years (the age of the body). The
        /// chronological age (the time that existed) is not read: the game uses
        /// it in its internal logic only - every player-facing screen of the
        /// game shows the biological age.
        /// </summary>
        private static double? ReadBiologicalAge(XElement pawn)
        {
            XElement? ageTracker = pawn.Element("ageTracker");
            if (ageTracker is null)
            {
                return null;
            }

            if (long.TryParse(TrimmedValue(ageTracker.Element("ageBiologicalTicks")), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long biologicalTicks))
            {
                return biologicalTicks / (double)TicksPerYear;
            }

            return null;
        }

        /// <summary>
        /// The text of an element. The game writes the literal "null" for an
        /// absent value - that is read as no value at all.
        /// </summary>
        private static string? ElementValue(XElement? element)
        {
            string? value = TrimmedValue(element);
            return value is not null && string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
                ? null
                : value;
        }

        /// <summary>
        /// The traits of a pawn - the vanilla ones only. A trait whose def
        /// carries a mod namespace ("Something_") is dropped, and so is a
        /// gene-derived trait (its source gene is set).
        /// </summary>
        private static IReadOnlyList<string> ReadTraits(XElement pawn)
        {
            var traits = new List<string>();

            XElement? allTraits = pawn.Element("story")?.Element("traits")?.Element("allTraits");
            if (allTraits is null)
            {
                return traits;
            }

            foreach (XElement trait in allTraits.Elements("li"))
            {
                string? def = TrimmedValue(trait.Element("def"));
                if (def is null || IsModNamespace(def))
                {
                    continue;
                }

                // A gene-derived trait is not a trait the player chose - and a
                // gene can come from a mod.
                if (ElementValue(trait.Element("sourceGene")) is not null)
                {
                    continue;
                }

                traits.Add(def);
            }

            return traits;
        }

        /// <summary>
        /// The skills of a pawn - the twelve vanilla ones only. A skill the
        /// save does not list has the level 0, a passion the save does not
        /// list is none.
        /// </summary>
        private static IReadOnlyList<SaveColonistSkill> ReadSkills(XElement pawn)
        {
            var skills = new List<SaveColonistSkill>();

            XElement? skillList = pawn.Element("skills")?.Element("skills");
            if (skillList is null)
            {
                return skills;
            }

            foreach (XElement skill in skillList.Elements("li"))
            {
                string? def = TrimmedValue(skill.Element("def"));
                if (def is null || !VanillaSkillDefs.Contains(def, StringComparer.Ordinal))
                {
                    continue;
                }

                int level = 0;
                if (int.TryParse(TrimmedValue(skill.Element("level")), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int parsedLevel))
                {
                    level = parsedLevel;
                }

                skills.Add(new SaveColonistSkill(def, level, TrimmedValue(skill.Element("passion"))));
            }

            return skills;
        }

        /// <summary>
        /// The conditions (hediffs) of a pawn - the vanilla ones only. A
        /// condition whose def carries a mod namespace ("Something_") is
        /// dropped. Note: the body part of an injury is not derivable from the
        /// save, so it is never shown.
        /// </summary>
        private static IReadOnlyList<SaveColonistCondition> ReadConditions(XElement pawn)
        {
            var conditions = new List<SaveColonistCondition>();

            XElement? hediffs = pawn.Element("healthTracker")?.Element("hediffSet")?.Element("hediffs");
            if (hediffs is null)
            {
                return conditions;
            }

            foreach (XElement hediff in hediffs.Elements("li"))
            {
                string? def = TrimmedValue(hediff.Element("def"));
                if (def is null || IsModNamespace(def))
                {
                    continue;
                }

                double? severity = null;
                if (double.TryParse(TrimmedValue(hediff.Element("severity")), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double parsedSeverity))
                {
                    severity = parsedSeverity;
                }

                conditions.Add(new SaveColonistCondition(
                    def,
                    severity,
                    TrimmedValue(hediff.Attribute("Class")),
                    ElementValue(hediff.Element("painCategory"))));
            }

            return conditions;
        }

        /// <summary>
        /// True when a def carries a mod namespace ("Something_"). The rule
        /// caught the only mod trait and the only mod hediff of the measured
        /// saves. It is not a guarantee: a mod can ship an un-prefixed def
        /// (the backstories of the measured saves do exactly that) - that is
        /// why nothing else is read from a def but its name.
        /// </summary>
        private static bool IsModNamespace(string def)
            => ModNamespacePattern.IsMatch(def);

        private static readonly System.Text.RegularExpressions.Regex ModNamespacePattern =
            new("^[A-Za-z0-9]+_", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>The name parts of a pawn.</summary>
        private sealed record SaveColonistNameParts(string? First, string? Nick, string? Last);

        /// <summary>
        /// Reads the text of an element. An empty element (or a self-closing
        /// one) has no text - the value stays empty.
        /// </summary>
        private static string? ExtractElementText(string xml, string elementName)
        {
            string open = "<" + elementName + ">";
            int start = xml.IndexOf(open, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            string close = "</" + elementName + ">";
            int end = xml.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0)
            {
                return null;
            }

            string value = xml.Substring(start + open.Length, end - start - open.Length).Trim();
            return value.Length == 0 ? null : value;
        }

        /// <summary>Reads the value of an attribute of a tag.</summary>
        private static string? ExtractAttributeValue(string tag, string attributeName)
        {
            string marker = attributeName + "=\"";
            int start = tag.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            int end = tag.IndexOf('"', start);
            if (end < 0)
            {
                return null;
            }

            string value = tag.Substring(start, end - start).Trim();
            return value.Length == 0 ? null : value;
        }

        #endregion
    }
}
