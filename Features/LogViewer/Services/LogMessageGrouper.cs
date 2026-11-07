#nullable enable
using System;
using System.Collections.Generic;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>One whole log message: a lead line plus its continuation lines.</summary>
    public readonly record struct LogMessage(string Text, int LineCount);

    /// <summary>
    /// Groups raw log lines into whole messages. A line that begins with
    /// whitespace continues the previous message — Unity indents stack-trace
    /// frames and wrapped output — so a message runs from a line starting with
    /// a non-whitespace character up to the next such line. Blank lines are
    /// separators: they are dropped and do not break the message they sit
    /// inside. An indented first line (an evicted or rotated lead) has nothing
    /// to attach to and starts its own message.
    /// </summary>
    public static class LogMessageGrouper
    {
        public static List<LogMessage> Group(IEnumerable<string?> lines)
        {
            var messages = new List<LogMessage>();
            var parts = new List<string>();
            var lineCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (line.Trim().Length == 0) continue;

                var isContinuation = char.IsWhiteSpace(line[0]);

                if (isContinuation && parts.Count > 0)
                {
                    parts.Add(line);
                    lineCount++;
                    continue;
                }

                if (parts.Count > 0)
                {
                    messages.Add(new LogMessage(string.Join("\n", parts), lineCount));
                }

                parts.Clear();
                parts.Add(line);
                lineCount = 1;
            }

            if (parts.Count > 0)
            {
                messages.Add(new LogMessage(string.Join("\n", parts), lineCount));
            }

            return messages;
        }
    }
}
