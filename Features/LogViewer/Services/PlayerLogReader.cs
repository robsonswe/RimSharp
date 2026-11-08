#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>Outcome of a full log read.</summary>
    public sealed class LogReadResult
    {
        public bool Found { get; init; }
        public string Text { get; init; } = string.Empty;
        /// <summary>Byte offset just past the last fully decoded character (tail anchor).</summary>
        public long BytesRead { get; init; }
        public long FileSize { get; init; }
        public string? ErrorMessage { get; init; }
    }

    /// <summary>
    /// Outcome of an incremental tail read. When <see cref="Reset"/> is true the
    /// file was truncated/rotated (or a new session wrote the "Mono path" header)
    /// and the caller must reload from the beginning.
    /// </summary>
    public sealed class LogTailResult
    {
        public bool Reset { get; init; }
        public string NewText { get; init; } = string.Empty;
        public long NewOffset { get; init; }
    }

    /// <summary>Outcome of a bounded read of the start of a log.</summary>
    public sealed class LogHeadResult
    {
        public bool Found { get; init; }
        /// <summary>Decoded text, cut at the last complete line in the window.</summary>
        public string Text { get; init; } = string.Empty;
        public long FileSize { get; init; }
        /// <summary>Bytes actually decoded from the window (may exceed the text length).</summary>
        public long BytesRead { get; init; }
        /// <summary>True when the window covered the whole file (nothing was left unread).</summary>
        public bool CoversWholeFile { get; init; }
    }

    /// <summary>
    /// Reads RimWorld's Player.log as text. Entry points:
    /// <list type="bullet">
    /// <item><see cref="ReadAsync"/> — full chunked read with progress.</item>
    /// <item><see cref="ReadHeadAsync"/> — bounded read of the file's start.</item>
    /// <item><see cref="ReadTail"/> — incremental read from a byte offset with
    /// truncation/new-session reset detection.</item>
    /// </list>
    /// Decoding is incremental (a persistent <see cref="Decoder"/> keeps partial
    /// multi-byte sequences pending instead of corrupting them at chunk
    /// boundaries), and the returned byte offset is always a character boundary,
    /// so tail reads never double-consume or lose bytes.
    /// </summary>
    public sealed class PlayerLogReader
    {
        private const int ReadBufferBytes = 81920;
        private const int CharBufferSize = 81920;

        public async Task<LogReadResult> ReadAsync(
            string path,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new LogReadResult { Found = false };
            }

            try
            {
                var length = new FileInfo(path).Length;
                var text = new StringBuilder();
                long consumedBytes;

                var readBuffer = new byte[ReadBufferBytes];
                var charBuffer = new char[CharBufferSize];
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var decoder = Encoding.UTF8.GetDecoder();

                var pending = 0;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(readBuffer, pending, readBuffer.Length - pending, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    var available = pending + bytesRead;
                    decoder.Convert(
                        readBuffer, 0, available, charBuffer, 0, charBuffer.Length,
                        flush: false, out var bytesUsed, out var charsUsed, out _);
                    text.Append(charBuffer, 0, charsUsed);

                    if (bytesUsed < available)
                    {
                        // Partial multi-byte sequence at the end: keep it for the next round.
                        Array.Copy(readBuffer, bytesUsed, readBuffer, 0, available - bytesUsed);
                        pending = available - bytesUsed;
                    }
                    else
                    {
                        pending = 0;
                    }

                    progress?.Report(length > 0 ? Math.Clamp((double)stream.Position / length, 0, 1) : 1);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // Flush any trailing partial sequence (becomes a replacement char).
                decoder.Convert(Array.Empty<byte>(), 0, 0, charBuffer, 0, charBuffer.Length,
                    flush: true, out _, out var flushedChars, out _);
                text.Append(charBuffer, 0, flushedChars);

                consumedBytes = stream.Position - pending;
                progress?.Report(1);

                return new LogReadResult
                {
                    Found = true,
                    Text = text.ToString(),
                    BytesRead = consumedBytes,
                    FileSize = length
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayerLogReader] Failed to read '{path}': {ex.Message}");
                return new LogReadResult { Found = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Reads the first <paramref name="maxBytes"/> bytes of the file and cuts
        /// at the last complete line, so a message split by the window boundary
        /// is never analyzed half-way. Used by the diagnostics: the startup
        /// banner, version, mod list and first issues all live at the head of a
        /// log, which the display store may have evicted already.
        /// </summary>
        public async Task<LogHeadResult> ReadHeadAsync(
            string path,
            int maxBytes,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new LogHeadResult { Found = false };
            }

            try
            {
                var length = new FileInfo(path).Length;
                var text = new StringBuilder();

                var readBuffer = new byte[ReadBufferBytes];
                var charBuffer = new char[CharBufferSize];
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var decoder = Encoding.UTF8.GetDecoder();

                var pending = 0;
                var budget = Math.Min(length, maxBytes);
                int bytesRead;
                while (budget > 0 &&
                       (bytesRead = await stream.ReadAsync(readBuffer, pending,
                            (int)Math.Min(readBuffer.Length - pending, budget), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    var available = pending + bytesRead;
                    decoder.Convert(
                        readBuffer, 0, available, charBuffer, 0, charBuffer.Length,
                        flush: false, out var bytesUsed, out var charsUsed, out _);
                    text.Append(charBuffer, 0, charsUsed);

                    if (bytesUsed < available)
                    {
                        Array.Copy(readBuffer, bytesUsed, readBuffer, 0, available - bytesUsed);
                        pending = available - bytesUsed;
                    }
                    else
                    {
                        pending = 0;
                    }

                    budget -= bytesRead;
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var content = text.ToString();
                var lastNewline = content.LastIndexOf('\n');
                if (lastNewline >= 0)
                {
                    content = content[..(lastNewline + 1)];
                }
                else if (content.Length > 0)
                {
                    // No complete line fits in the window: analyze nothing rather
                    // than half a message.
                    content = string.Empty;
                }

                return new LogHeadResult
                {
                    Found = true,
                    Text = content,
                    FileSize = length,
                    BytesRead = stream.Position - pending,
                    CoversWholeFile = stream.Position >= length
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayerLogReader] Head read failed for '{path}': {ex.Message}");
                return new LogHeadResult { Found = false };
            }
        }

        /// <summary>
        /// Reads the bytes appended to the file since <paramref name="fromOffset"/>.
        /// The returned anchor is a character boundary for the same reason as
        /// <see cref="ReadAsync"/>: the next call can seek straight to it.
        /// </summary>
        public LogTailResult ReadTail(string path, long fromOffset)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new LogTailResult();
            }

            try
            {
                var length = new FileInfo(path).Length;
                if (length < fromOffset)
                {
                    // File shrank (truncated or rotated by the game) — reload from 0.
                    return new LogTailResult { Reset = true };
                }

                var text = new StringBuilder();
                var readBuffer = new byte[ReadBufferBytes];
                var charBuffer = new char[CharBufferSize];

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(fromOffset, SeekOrigin.Begin);
                var decoder = Encoding.UTF8.GetDecoder();

                var pending = 0;
                int bytesRead;
                while ((bytesRead = stream.Read(readBuffer, pending, readBuffer.Length - pending)) > 0)
                {
                    var available = pending + bytesRead;
                    decoder.Convert(
                        readBuffer, 0, available, charBuffer, 0, charBuffer.Length,
                        flush: false, out var bytesUsed, out var charsUsed, out _);
                    text.Append(charBuffer, 0, charsUsed);

                    if (bytesUsed < available)
                    {
                        Array.Copy(readBuffer, bytesUsed, readBuffer, 0, available - bytesUsed);
                        pending = available - bytesUsed;
                    }
                    else
                    {
                        pending = 0;
                    }
                }

                decoder.Convert(Array.Empty<byte>(), 0, 0, charBuffer, 0, charBuffer.Length,
                    flush: true, out _, out var flushedChars, out _);
                text.Append(charBuffer, 0, flushedChars);

                var newText = text.ToString();
                var newOffset = stream.Position - pending;

                if (newText.StartsWith("Mono path", StringComparison.Ordinal))
                {
                    // A fresh session rewrote the log from the top.
                    return new LogTailResult { Reset = true };
                }

                return new LogTailResult { NewText = newText, NewOffset = newOffset };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayerLogReader] Tail read failed for '{path}': {ex.Message}");
                return new LogTailResult();
            }
        }
    }
}
