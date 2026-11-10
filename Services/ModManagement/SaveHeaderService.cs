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
    ///  - the head window of the file (the first ~160 KB), which always covers the
    ///    &lt;game&gt; head section and stops at &lt;world&gt;, so the map and world
    ///    payloads (the vast majority of the file) are never touched.
    ///
    /// Saves are never written, moved or deleted by this service.
    /// </summary>
    public class SaveHeaderService : ISaveHeaderService
    {
        /// <summary>Default cap of the accumulated &lt;meta&gt; window (4 MB).</summary>
        internal const int DefaultMetaCapBytes = 4 * 1024 * 1024;

        /// <summary>Default cap of the accumulated head window (160 KB).</summary>
        internal const int DefaultDetailsCapBytes = 160 * 1024;

        /// <summary>Size of a single read chunk (64 KB).</summary>
        internal const int ChunkSize = 64 * 1024;

        private static readonly byte[] MetaEndMarker = Encoding.ASCII.GetBytes("</meta>");
        private static readonly byte[] WorldStartMarker = Encoding.ASCII.GetBytes("<world");

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
        /// Test seam: overrides the cap of the head window (default 160 KB).
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
                        () => ReadWindowCore(filePath, capBytes, MetaEndMarker, ignoreCase: true, cancellationToken),
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

                string text = DecodeText(window.Bytes, window.Length);
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

                int capBytes = TestDetailsCapBytes ?? DefaultDetailsCapBytes;
                HeadWindow? window = await Task.Run(
                        () => ReadWindowCore(filePath, capBytes, WorldStartMarker, ignoreCase: false, cancellationToken),
                        cancellationToken).ConfigureAwait(false);

                if (window is null)
                {
                    _logger.LogWarning($"ReadDetailsAsync: '{filePath}' could not be read", nameof(SaveHeaderService));
                    return null;
                }

                byte[] headBytes;
                if (window.Stop == HeadStop.MarkerFound)
                {
                    // The head ends where <world> begins - the window stays in the head.
                    headBytes = window.Bytes.Take(window.MarkerOffset).ToArray();
                }
                else if (window.Stop == HeadStop.EndOfFile)
                {
                    // The whole file was read - it must be a complete document.
                    string wholeFile = DecodeText(window.Bytes, window.Length).TrimEnd();
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

                string head = DecodeText(headBytes, headBytes.Length);
                if (!ContainsTagStart(head, "game"))
                {
                    _logger.LogWarning($"ReadDetailsAsync: '{filePath}' has no game block", nameof(SaveHeaderService));
                    return null;
                }

                SaveDetails details = ParseDetails(head);
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
                return SaveCompat.Missing;
            }

            if (!saveIds.SetEquals(activeIds))
            {
                return SaveCompat.Changed;
            }

            return SaveCompat.Compatible;
        }

        /// <inheritdoc />
        public (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) Diff(SaveHeader header,
            IReadOnlyCollection<string> activeModIds)
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

            return (added, removed);
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
        }

        /// <summary>
        /// Reads at most <paramref name="capBytes"/> bytes from the head of a file and
        /// stops as soon as the marker appears in the accumulated window.
        /// Gzip files are detected by their magic bytes and decompressed on the fly -
        /// only the head is ever decompressed.
        /// Returns null when the file cannot be opened or read.
        /// </summary>
        private HeadWindow? ReadWindowCore(string filePath, int capBytes, byte[] marker,
            bool ignoreCase, CancellationToken cancellationToken)
        {
            if (capBytes <= 0)
            {
                return null;
            }

            byte[] buffer = new byte[Math.Min(capBytes, ChunkSize * 2)];
            int length = 0;

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

                    while (length < capBytes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int wanted = Math.Min(chunk.Length, capBytes - length);
                        int read = source.Read(chunk, 0, wanted);
                        if (read <= 0)
                        {
                            break; // End of file.
                        }

                        if (length + read > buffer.Length)
                        {
                            int newSize = (int)Math.Min((long)capBytes,
                                Math.Max((long)buffer.Length * 2, length + (long)read));
                            Array.Resize(ref buffer, newSize);
                        }

                        Array.Copy(chunk, 0, buffer, length, read);
                        length += read;

                        // Only the freshly added bytes are searched (plus a small overlap
                        // so a marker spanning two chunks is still found).
                        int searchFrom = Math.Max(0, length - read - (marker.Length - 1));
                        int markerIndex = IndexOfBytes(buffer, length, marker, searchFrom, ignoreCase);
                        if (markerIndex >= 0)
                        {
                            return new HeadWindow
                            {
                                Bytes = buffer,
                                Length = length,
                                Stop = HeadStop.MarkerFound,
                                MarkerOffset = markerIndex
                            };
                        }
                    }

                    return new HeadWindow
                    {
                        Bytes = buffer,
                        Length = length,
                        Stop = length >= capBytes ? HeadStop.CapReached : HeadStop.EndOfFile
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

        private static string DecodeText(byte[] buffer, int length)
        {
            int offset = 0;
            if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                offset = 3; // UTF-8 BOM
            }

            return Encoding.UTF8.GetString(buffer, offset, length - offset);
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

        private static SaveDetails ParseDetails(string head)
        {
            XElement? scenario = ParseFragment(ExtractElementXml(head, "scenario"));
            XElement? storyteller = ParseFragment(ExtractElementXml(head, "storyteller"));
            XElement? tickManager = ParseFragment(ExtractElementXml(head, "tickManager"));
            XElement? info = ParseFragment(ExtractElementXml(head, "info"));
            XElement? history = ParseFragment(ExtractElementXml(head, "history"));
            XElement? researchManager = ParseFragment(ExtractElementXml(head, "researchManager"));
            XElement? components = ParseFragment(ExtractElementXml(head, "components"));

            double? wealthTotal = null;
            double? wealthItems = null;
            double? wealthBuildings = null;
            int? freeColonists = null;
            int? prisoners = null;

            XElement? recorderGroups = history?.Element("autoRecorderGroups");
            if (recorderGroups is not null)
            {
                foreach (XElement group in recorderGroups.Elements("li"))
                {
                    string? key = ResolveRecorderKey(group);
                    if (key is null)
                    {
                        continue;
                    }

                    XElement? records = group.Element("records");
                    if (records is null)
                    {
                        continue;
                    }

                    double? value = DecodeLastRecord(records.Value);
                    if (value is null)
                    {
                        continue; // Broken record - the field stays unknown.
                    }

                    switch (key)
                    {
                        case "Wealth_Total":
                            wealthTotal = value;
                            break;
                        case "Wealth_Items":
                            wealthItems = value;
                            break;
                        case "Wealth_Buildings":
                            wealthBuildings = value;
                            break;
                        case "FreeColonists":
                            freeColonists = (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
                            break;
                        case "Prisoners":
                            prisoners = (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
                            break;
                    }
                }
            }

            int? researchDefsCompleted = null;
            XElement? researchKeys = researchManager?.Element("progress")?.Element("keys");
            if (researchKeys is not null)
            {
                researchDefsCompleted = researchKeys.Elements("li").Count();
            }

            var activeDlcClasses = new List<string>();
            if (components is not null)
            {
                foreach (XElement component in components.Elements("li"))
                {
                    string? className = component.Attribute("Class")?.Value;
                    if (string.IsNullOrEmpty(className) || !className.StartsWith("GameComponent_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string dlc = className.Substring("GameComponent_".Length);
                    if (dlc.Length > 0 && !activeDlcClasses.Contains(dlc))
                    {
                        activeDlcClasses.Add(dlc);
                    }
                }
            }

            return new SaveDetails(
                TrimmedValue(scenario?.Element("name")),
                TrimmedValue(storyteller?.Element("def")),
                ResolveDifficulty(storyteller),
                ParseInt(tickManager?.Element("ticksGame")),
                ParseLong(tickManager?.Element("gameStartAbsTick")),
                ParseInt(tickManager?.Element("startingYear")),
                ParseDouble(info?.Element("realPlayTimeInteracting")),
                TrimmedValue(info?.Element("startingTile")),
                wealthTotal,
                wealthItems,
                wealthBuildings,
                freeColonists,
                prisoners,
                researchDefsCompleted,
                activeDlcClasses);
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

        /// <summary>
        /// Resolves the recorder key of a group. Depending on the game version the key
        /// lives in a &lt;defName&gt; element or in a def attribute; older layouts only
        /// have a &lt;def&gt; element.
        /// </summary>
        private static string? ResolveRecorderKey(XElement group)
        {
            string? key = TrimmedValue(group.Element("defName"));
            if (!string.IsNullOrEmpty(key))
            {
                return key;
            }

            key = TrimmedValue(group.Attribute("defName"));
            if (!string.IsNullOrEmpty(key))
            {
                return key;
            }

            key = TrimmedValue(group.Attribute("def"));
            if (!string.IsNullOrEmpty(key))
            {
                return key;
            }

            return TrimmedValue(group.Element("def"));
        }

        /// <summary>
        /// Decodes the last record of a recorder group: the records are whitespace
        /// separated base64 tokens, each one a 4-byte little-endian float32.
        /// The last token is the current value of the recorder.
        /// </summary>
        private static double? DecodeLastRecord(string records)
        {
            if (string.IsNullOrWhiteSpace(records))
            {
                return null;
            }

            string[] tokens = records.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(tokens[^1]);
            }
            catch (FormatException)
            {
                return null;
            }

            if (bytes.Length != 4)
            {
                return null;
            }

            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToSingle(bytes, 0);
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

        private static double? ParseDouble(XElement? element)
        {
            string? value = TrimmedValue(element);
            if (value is null)
            {
                return null;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
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
    }
}
