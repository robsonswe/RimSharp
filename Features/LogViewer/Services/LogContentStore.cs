#nullable enable
using System;
using System.Collections.Generic;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// Memory-bounded line storage for loaded log content. Lines are appended from
    /// decoded file content; once the retained text exceeds the cap the oldest
    /// lines are dropped. Mirrors the reference's chunked storage intent (bound
    /// memory on huge logs) with a simpler line-oriented shape that the
    /// line-level display binds to directly.
    /// </summary>
    public sealed class LogContentStore
    {
        /// <summary>Retained-text cap; beyond it the oldest lines are evicted.</summary>
        public const long MaxTotalBytes = 10 * 1024 * 1024;

        private readonly List<string> _lines = new();
        private readonly long _maxTotalBytes;
        private long _totalBytes;
        private string _pendingTail = string.Empty;

        public LogContentStore(long maxTotalBytes = MaxTotalBytes)
        {
            _maxTotalBytes = maxTotalBytes > 0 ? maxTotalBytes : MaxTotalBytes;
        }

        public IReadOnlyList<string> Lines => _lines;
        public int LineCount => _lines.Count;
        public long TotalBytes => _totalBytes;
        public bool IsEmpty => _lines.Count == 0;

        /// <summary>
        /// Appends decoded text as lines. Line terminators are normalized so
        /// Windows (CRLF), Unix (LF) and Unity's bare-CR output all split
        /// consistently. A trailing terminator does not produce a phantom empty
        /// line; a final unterminated line is kept.
        /// A chunk that ends mid-line keeps the trailing fragment pending and
        /// returns it as one line once its terminator arrives, unless
        /// <paramref name="finalChunk"/> is true — so a message split across
        /// incremental reads is never cut into two lines.
        /// </summary>
        public void AppendText(string? text, bool finalChunk = true)
        {
            if (_pendingTail.Length > 0)
            {
                text = _pendingTail + text;
                _pendingTail = string.Empty;
            }

            if (string.IsNullOrEmpty(text)) return;

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lastTerminator = normalized.LastIndexOf('\n');

            if (lastTerminator < 0)
            {
                if (finalChunk) AddLine(normalized);
                else _pendingTail = normalized;
                return;
            }

            var start = 0;
            for (var i = 0; i <= lastTerminator; i++)
            {
                if (normalized[i] != '\n') continue;

                AddLine(normalized[start..i]);
                start = i + 1;
            }

            var remainder = normalized[(lastTerminator + 1)..];
            if (remainder.Length > 0)
            {
                if (finalChunk) AddLine(remainder);
                else _pendingTail = remainder;
            }
        }

        private void AddLine(string line)
        {
            _lines.Add(line);
            _totalBytes += line.Length + 1;
            EvictWhileOverCap();
        }

        private void EvictWhileOverCap()
        {
            var index = 0;
            while (_totalBytes > _maxTotalBytes && index < _lines.Count)
            {
                _totalBytes -= _lines[index].Length + 1;
                index++;
            }

            if (index > 0)
            {
                _lines.RemoveRange(0, index);
            }
        }

        public void Clear()
        {
            _lines.Clear();
            _totalBytes = 0;
            _pendingTail = string.Empty;
        }
    }
}
