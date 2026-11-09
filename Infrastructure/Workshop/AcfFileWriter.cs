using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Infrastructure.Workshop
{
    /// <summary>
    /// Writes Valve KeyValues ("ACF") text for appworkshop_294100.acf files.
    /// Produces output compatible with <see cref="AcfFileParser"/>.
    /// </summary>
    public static class AcfFileWriter
    {
        /// <summary>
        /// Writes a workshop ACF file from the parsed data, excluding stale keys.
        /// </summary>
        /// <param name="filePath">Path to write the ACF file.</param>
        /// <param name="data">Parsed workshop data.</param>
        /// <param name="staleKeys">Keys to exclude from the output (orphaned mods).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task WriteAsync(string filePath, WorkshopAcfData data, ISet<string> staleKeys, CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            var writer = new AcfWriter(sb);

            writer.WriteRoot("AppWorkshop", () =>
            {
                writer.WriteSection("WorkshopItemsInstalled", () =>
                {
                    foreach (var kvp in data.Items)
                    {
                        if (staleKeys.Contains(kvp.Key))
                            continue;

                        // Use ResolvedTimeUpdated as the installed timestamp
                        writer.WriteKeyValue(kvp.Key, kvp.Value.ResolvedTimeUpdated.ToString());
                    }
                });

                writer.WriteSection("WorkshopItemDetails", () =>
                {
                    foreach (var kvp in data.Items)
                    {
                        if (staleKeys.Contains(kvp.Key))
                            continue;

                        writer.WriteObject(kvp.Key, () =>
                        {
                            var item = kvp.Value;
                            if (item.TimeUpdated != 0)
                                writer.WriteKeyValue("timeupdated", item.TimeUpdated.ToString());
                            if (item.TimeTouched != 0)
                                writer.WriteKeyValue("timetouched", item.TimeTouched.ToString());
                            if (item.SourceId != 0)
                                writer.WriteKeyValue("sourceid", item.SourceId.ToString());

                            // Write any additional raw values not already covered
                            if (item.RawValues != null)
                            {
                                foreach (var rawKvp in item.RawValues)
                                {
                                    var key = rawKvp.Key.ToLowerInvariant();
                                    if (key != "timeupdated" && key != "timetouched" && key != "sourceid")
                                    {
                                        writer.WriteKeyValue(rawKvp.Key, rawKvp.Value);
                                    }
                                }
                            }
                        });
                    }
                });
            });

            await File.WriteAllTextAsync(filePath, sb.ToString(), cancellationToken);
        }

        private sealed class AcfWriter
        {
            private readonly StringBuilder _sb;
            private int _indentLevel = 0;

            public AcfWriter(StringBuilder sb)
            {
                _sb = sb;
            }

            public void WriteRoot(string name, Action content)
            {
                WriteLine($"\"{Escape(name)}\"");
                WriteLine("{");
                _indentLevel++;
                content();
                _indentLevel--;
                WriteLine("}");
            }

            public void WriteSection(string name, Action content)
            {
                WriteLine($"\"{Escape(name)}\"");
                WriteLine("{");
                _indentLevel++;
                content();
                _indentLevel--;
                WriteLine("}");
            }

            public void WriteObject(string name, Action content)
            {
                WriteLine($"\"{Escape(name)}\"");
                WriteLine("{");
                _indentLevel++;
                content();
                _indentLevel--;
                WriteLine("}");
            }

            public void WriteKeyValue(string key, string value)
            {
                WriteLine($"\"{Escape(key)}\"\t\t\"{Escape(value)}\"");
            }

            private void WriteLine(string line)
            {
                _sb.Append('\t', _indentLevel);
                _sb.AppendLine(line);
            }

            private static string Escape(string s)
            {
                return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            }
        }
    }
}