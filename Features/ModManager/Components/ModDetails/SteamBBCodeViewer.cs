#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace RimSharp.Features.ModManager.Components.ModDetails
{
    public class SteamBBCodeViewer : StackPanel
    {
        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<SteamBBCodeViewer, string?>(nameof(Text));

        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        static SteamBBCodeViewer()
        {
            TextProperty.Changed.AddClassHandler<SteamBBCodeViewer>((sender, _) => sender.Rebuild());
        }

        private static readonly Regex s_tagRegex =
            new(@"\[(/?)([A-Za-z0-9_*]+)(?:=([^\]]*)|(\s+[^\]]*))?\]", RegexOptions.Compiled);

        private static readonly HashSet<string> KnownInlineTags = new(StringComparer.OrdinalIgnoreCase)
            { "b", "i", "u", "s", "strike", "url", "img", "previewimg", "previewicon", "emoticon", "color", "spoiler" };

        private static readonly HttpClient s_http = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders = { { "User-Agent", "RimSharp-ModManager/1.0 (AvaloniaUI; +https://github.com)" } }
        };

        private string _src = string.Empty;
        private int _pos;
        private CancellationTokenSource? _imageCts;

        private void Rebuild()
        {
            _imageCts?.Cancel();
            _imageCts?.Dispose();
            _imageCts = new CancellationTokenSource();

            Children.Clear();
            
            _src = (Text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace('\u00A0', ' ');

            _pos = 0;
            if (_src.Length == 0) return;
            RenderBlocks(Children, stopTag: null, align: TextAlignment.Left);
        }

        private void RenderBlocks(Controls target, string? stopTag, TextAlignment align)
        {
            var literal = new StringBuilder();
            var ct = _imageCts?.Token ?? CancellationToken.None;

            while (_pos < _src.Length)
            {
                var m = s_tagRegex.Match(_src, _pos);
                if (!m.Success)
                {
                    literal.Append(_src.Substring(_pos));
                    _pos = _src.Length;
                    break;
                }

                var posAfterTag = m.Index + m.Length;
                var isClose = m.Groups[1].Value == "/";
                var tag = m.Groups[2].Value.ToLowerInvariant();
                var param = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;

                if (isClose)
                {
                    if (m.Index > _pos) literal.Append(_src, _pos, m.Index - _pos);
                    _pos = m.Index;

                    if (tag is "hr" or "*" or "img" or "previewimg" or "previewicon")
                    {
                        _pos = posAfterTag;
                        continue;
                    }

                    if (stopTag != null && tag.Equals(stopTag, StringComparison.OrdinalIgnoreCase))
                    {
                        FlushLiteral(target, literal, align);
                        _pos = posAfterTag;
                        return;
                    }

                    literal.Append(m.Value);
                    _pos = posAfterTag;
                    continue;
                }

                if (m.Index > _pos) literal.Append(_src, _pos, m.Index - _pos);
                _pos = posAfterTag;

                switch (tag)
                {
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "h6":
                    {
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: false);
                        var heading = BuildParagraph(content, new BBStyle(), align);
                        if (heading != null)
                        {
                            heading.Classes.Add(tag);
                            target.Add(heading);
                        }
                        break;
                    }
                    case "img":
                    case "previewimg":
                    case "previewicon":
                    {
                        FlushLiteral(target, literal, align);
                        string content = string.Empty;

                        if (string.IsNullOrWhiteSpace(param))
                        {
                            content = ConsumeImageBody(tag);
                        }
                        else
                        {
                            var closeMatch = Regex.Match(_src.Substring(_pos), @"^\s*\[/\s*" + Regex.Escape(tag) + @"\s*\]", RegexOptions.IgnoreCase);
                            if (closeMatch.Success)
                            {
                                _pos += closeMatch.Length;
                            }
                        }

                        var url = ExtractImageUrl(tag, param, content);
                        if (!string.IsNullOrWhiteSpace(url) && IsSafeUrl(url))
                        {
                            AddImageBlock(target, BuildImageControl(url, align, linkUrl: null, ct));
                        }
                        break;
                    }
                    case "url":
                    {
                        var savedPos = _pos;
                        var content = ConsumeUntilClose("url", balanced: false);
                        var trimmedContent = content.Trim();
                        var rawUrl = !string.IsNullOrWhiteSpace(param) ? param : "";
                        var linkUrl = rawUrl.Trim().TrimStart('=').Trim('"', '\'', ' ');

                        // 1. Caso seja imagem única: [url=...][img]...[/img][/url]
                        var singleImgUrl = TryExtractImageUrlFromBBCode(trimmedContent);
                        if (!string.IsNullOrWhiteSpace(singleImgUrl))
                        {
                            FlushLiteral(target, literal, align);
                            AddImageBlock(target, BuildImageControl(singleImgUrl, align, linkUrl: linkUrl, ct));
                            break;
                        }

                        // 2. Caso contenha elementos de bloco (h1..h6 e/ou imagens):
                        // Ex: [url=...][h1]Mod Discord[/h1][img]...[/img][/url]
                        // ou [url=...][h2]Vanilla Genes Rebalanced[/h2][/url]
                        if (Regex.IsMatch(trimmedContent, @"\[(h[1-6]|img|previewimg|previewicon)\b", RegexOptions.IgnoreCase))
                        {
                            FlushLiteral(target, literal, align);
                            RenderLinkedBlockContent(target, trimmedContent, linkUrl, align, ct);
                            break;
                        }

                        // 3. Caso contrário, é um link inline normal dentro de texto corrido
                        _pos = savedPos;
                        literal.Append(m.Value);
                        break;
                    }
                    case "hr":
                        FlushLiteral(target, literal, align);
                        target.Add(new Separator { Classes = { "BbHr" } });
                        break;
                    case "list":
                    case "olist":
                    {
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: true);
                        var isOrdered = tag == "olist" || (!string.IsNullOrWhiteSpace(param) && param.Trim().StartsWith("1"));
                        target.Add(BuildList(isOrdered, content, align));
                        break;
                    }
                    case "quote":
                    {
                        FlushLiteral(target, literal, align);
                        target.Add(BuildQuote(param, align));
                        break;
                    }
                    case "code":
                    case "pre":
                    {
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: false).TrimEnd('\n');
                        target.Add(new Border
                        {
                            Classes = { "BbCodeBlock" },
                            Child = new SelectableTextBlock
                            {
                                Classes = { "BbCode" },
                                Text = content
                            }
                        });
                        break;
                    }
                    case "spoiler":
                    {
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: true);
                        var stack = new StackPanel();
                        RenderSubstringInto(stack.Children, content, null, align);

                        var title = !string.IsNullOrWhiteSpace(param)
                            ? $"Spoiler: {param.Trim().TrimStart('=').Trim('"', '\'', ' ')}"
                            : "Spoiler";

                        target.Add(new Expander
                        {
                            Classes = { "BbSpoiler" },
                            Header = title,
                            Content = stack
                        });
                        break;
                    }
                    case "table":
                    {
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: true);
                        target.Add(BuildTable(content, align));
                        break;
                    }
                    case "center":
                    case "right":
                    case "left":
                    {
                        FlushLiteral(target, literal, align);
                        var wrap = new StackPanel();
                        var childAlign = tag == "center" ? TextAlignment.Center
                            : tag == "right" ? TextAlignment.Right
                            : TextAlignment.Left;
                        RenderBlocks(wrap.Children, stopTag: tag, align: childAlign);
                        target.Add(wrap);
                        break;
                    }
                    case "noparse":
                    {
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: false);
                        target.Add(BuildRawParagraphs(content, align));
                        break;
                    }
                    case "previewyoutube":
                    {
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: false);
                        var rawId = !string.IsNullOrWhiteSpace(param) ? param : content;
                        var videoId = rawId.Trim().Trim('=', '"', '\'', ' ').Split(';')[0];
                        if (!string.IsNullOrWhiteSpace(videoId))
                        {
                            target.Add(BuildLink(
                                $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}",
                                "▶ YouTube preview"));
                        }
                        break;
                    }
                    default:
                        literal.Append(m.Value);
                        break;
                }
            }
            FlushLiteral(target, literal, align);
        }

        private void RenderLinkedBlockContent(Controls target, string content, string linkUrl, TextAlignment align, CancellationToken ct)
        {
            var pos = 0;
            while (pos < content.Length)
            {
                var m = s_tagRegex.Match(content, pos);
                if (!m.Success)
                {
                    var remaining = content.Substring(pos).Trim();
                    if (remaining.Length > 0)
                    {
                        target.Add(BuildLink(linkUrl, remaining));
                    }
                    break;
                }

                if (m.Index > pos)
                {
                    var textBefore = content.Substring(pos, m.Index - pos).Trim();
                    if (textBefore.Length > 0)
                    {
                        target.Add(BuildLink(linkUrl, textBefore));
                    }
                }

                pos = m.Index + m.Length;
                var tag = m.Groups[2].Value.ToLowerInvariant();
                var isClose = m.Groups[1].Value == "/";
                var param = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;

                if (isClose) continue;

                switch (tag)
                {
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "h6":
                    {
                        var closeTag = "[/" + tag + "]";
                        var closeIdx = content.IndexOf(closeTag, pos, StringComparison.OrdinalIgnoreCase);
                        var inner = closeIdx >= 0 ? content.Substring(pos, closeIdx - pos) : content.Substring(pos);
                        pos = closeIdx >= 0 ? closeIdx + closeTag.Length : content.Length;

                        var heading = BuildLinkedHeading(tag, inner.Trim(), linkUrl, align);
                        if (heading != null) target.Add(heading);
                        break;
                    }
                    case "img":
                    case "previewimg":
                    case "previewicon":
                    {
                        string inner = string.Empty;
                        if (string.IsNullOrWhiteSpace(param))
                        {
                            var closeTag = "[/" + tag + "]";
                            var closeIdx = content.IndexOf(closeTag, pos, StringComparison.OrdinalIgnoreCase);
                            inner = closeIdx >= 0 ? content.Substring(pos, closeIdx - pos) : content.Substring(pos);
                            pos = closeIdx >= 0 ? closeIdx + closeTag.Length : content.Length;
                        }
                        else
                        {
                            var closeMatch = Regex.Match(content.Substring(pos), @"^\s*\[/\s*" + Regex.Escape(tag) + @"\s*\]", RegexOptions.IgnoreCase);
                            if (closeMatch.Success) pos += closeMatch.Length;
                        }

                        var imgUrl = ExtractImageUrl(tag, param, inner);
                        if (!string.IsNullOrWhiteSpace(imgUrl) && IsSafeUrl(imgUrl))
                        {
                            AddImageBlock(target, BuildImageControl(imgUrl, align, linkUrl: linkUrl, ct));
                        }
                        break;
                    }
                }
            }
        }

        private static Control BuildLinkedHeading(string tag, string text, string url, TextAlignment align)
        {
            var label = new TextBlock
            {
                Classes = { "BbParagraph", tag, "BbLink" },
                Text = text,
                TextAlignment = align,
            };

            var button = new Button
            {
                Classes = { "BbLink" },
                Content = label,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            button.Click += (_, _) => OpenExternal(url);
            ToolTip.SetTip(button, url);
            return button;
        }

        private static void AddImageBlock(Controls target, Control imageControl)
        {
            // Se o último item for um WrapPanel de imagens (sem quebra de linha entre elas),
            // posiciona lado a lado na mesma linha horizontal (como os badges de Discord e Ko-fi).
            if (target.Count > 0 && target[target.Count - 1] is WrapPanel existingWrap)
            {
                existingWrap.Children.Add(imageControl);
                return;
            }

            var wrap = new WrapPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
            };
            wrap.Children.Add(imageControl);
            target.Add(wrap);
        }

        private string ConsumeImageBody(string tag)
        {
            var closeTag = "[/" + tag + "]";
            var nextClose = _src.IndexOf(closeTag, _pos, StringComparison.OrdinalIgnoreCase);
            if (nextClose >= 0)
            {
                var candidate = _src.Substring(_pos, nextClose - _pos);
                if (!candidate.Contains("\n\n") && !candidate.Contains("[h1") && !candidate.Contains("[h2") && !candidate.Contains("[table"))
                {
                    _pos = nextClose + closeTag.Length;
                    return candidate;
                }
            }

            var newlineIdx = _src.IndexOf('\n', _pos);
            if (newlineIdx >= 0)
            {
                var line = _src.Substring(_pos, newlineIdx - _pos);
                _pos = newlineIdx;
                return line.Trim();
            }

            var rest = _src.Substring(_pos);
            _pos = _src.Length;
            return rest.Trim();
        }

        private string ConsumeUntilClose(string tag, bool balanced)
        {
            int search = _pos, depth = 1;
            int contentStart = _pos;
            while (true)
            {
                var m = s_tagRegex.Match(_src, search);
                if (!m.Success)
                {
                    var content = _src.Substring(contentStart);
                    _pos = _src.Length;
                    return content;
                }
                var after = m.Index + m.Length;
                if (m.Groups[2].Value.Equals(tag, StringComparison.OrdinalIgnoreCase))
                {
                    if (m.Groups[1].Value == "/")
                    {
                        if (--depth == 0)
                        {
                            var content = _src.Substring(contentStart, m.Index - contentStart);
                            _pos = after;
                            return content;
                        }
                    }
                    else if (balanced) depth++;
                }
                search = after;
            }
        }

        private Control BuildQuote(string param, TextAlignment align)
        {
            var stack = new StackPanel();
            var author = param.Trim().TrimStart('=').Trim('"', '\'', ' ');
            if (!string.IsNullOrWhiteSpace(author))
            {
                stack.Children.Add(new SelectableTextBlock
                {
                    Classes = { "BbQuoteAuthor" },
                    Text = $"– {author}",
                });
            }
            RenderBlocks(stack.Children, stopTag: "quote", align: align);
            return new Border { Classes = { "BbQuote" }, Child = stack };
        }

        private Control BuildList(bool ordered, string content, TextAlignment align)
        {
            var listPanel = new StackPanel();

            var firstBullet = content.IndexOf("[*]", StringComparison.OrdinalIgnoreCase);
            if (firstBullet > 0)
            {
                var preamble = content.Substring(0, firstBullet).Trim('\n', ' ');
                if (preamble.Length > 0)
                {
                    RenderSubstringInto(listPanel.Children, preamble, null, align);
                }
                content = content.Substring(firstBullet);
            }

            var items = SplitTopLevel(content, "[*]");
            var counter = 0;
            foreach (var rawItem in items)
            {
                var item = rawItem.Replace("[/*]", string.Empty, StringComparison.OrdinalIgnoreCase)
                                  .Trim('\n', ' ', '\t');
                if (item.Length == 0) continue;

                if (ordered) counter++;
                var row = new Grid();

                var bullet = new TextBlock
                {
                    Text = ordered ? $"{counter}." : "•",
                    Classes = { "BbBullet" },
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                };

                var contentPanel = new StackPanel();
                RenderSubstringInto(contentPanel.Children, item, null, align);

                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                Grid.SetColumn(bullet, 0);
                Grid.SetColumn(contentPanel, 1);
                row.Children.Add(bullet);
                row.Children.Add(contentPanel);
                listPanel.Children.Add(row);
            }
            return listPanel;
        }

        private Control BuildTable(string content, TextAlignment align)
        {
            var grid = new Grid { Classes = { "BbTable" } };
            var maxCols = 0;

            var trMatches = Regex.Matches(content, @"\[tr\](.*?)\[/tr\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var rows = trMatches.Count > 0
                ? (IEnumerable<string>)trMatches.Cast<Match>().Select(m => m.Groups[1].Value)
                : SplitTopLevel(content, "[tr]");

            var rowList = rows.Where(r => !string.IsNullOrWhiteSpace(r.Replace("[/tr]", string.Empty, StringComparison.OrdinalIgnoreCase))).ToList();

            for (var rowIndex = 0; rowIndex < rowList.Count; rowIndex++)
            {
                var rowText = rowList[rowIndex].Replace("[/tr]", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('\n');
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var cellMatches = Regex.Matches(rowText, @"\[(td|th)\](.*?)\[/\1\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var col = 0;

                if (cellMatches.Count > 0)
                {
                    foreach (Match cellMatch in cellMatches)
                    {
                        var isHeader = cellMatch.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
                        var cellContent = cellMatch.Groups[2].Value.Trim('\n');

                        var cellBorder = CreateCellBorder(cellContent, isHeader, align);
                        Grid.SetRow(cellBorder, rowIndex);
                        Grid.SetColumn(cellBorder, col++);
                        grid.Children.Add(cellBorder);
                    }
                }
                else
                {
                    foreach (var rawCell in SplitTopLevel(rowText, "[td]"))
                    {
                        var cell = rawCell.Trim('\n');
                        if (cell.Length == 0) continue;

                        var cellBorder = CreateCellBorder(cell, isHeader: false, align);
                        Grid.SetRow(cellBorder, rowIndex);
                        Grid.SetColumn(cellBorder, col++);
                        grid.Children.Add(cellBorder);
                    }
                }

                maxCols = Math.Max(maxCols, col);
            }

            if (maxCols == 0) return new Panel();

            var colMaxChars = new int[maxCols];
            foreach (var rowText in rowList)
            {
                var cellMatches = Regex.Matches(rowText, @"\[(td|th)\](.*?)\[/\1\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                for (var colIdx = 0; colIdx < cellMatches.Count && colIdx < maxCols; colIdx++)
                {
                    var len = cellMatches[colIdx].Groups[2].Value.Trim().Length;
                    colMaxChars[colIdx] = Math.Max(colMaxChars[colIdx], len);
                }
            }

            for (var c = 0; c < maxCols; c++)
            {
                if (maxCols <= 2)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                }
                else if (maxCols < 6)
                {
                    var isShortCol = colMaxChars[c] > 0 && colMaxChars[c] <= 9;
                    grid.ColumnDefinitions.Add(new ColumnDefinition(isShortCol ? GridLength.Auto : GridLength.Star));
                }
                else
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                }
            }

            foreach (var child in grid.Children)
            {
                if (child is Border b)
                {
                    var r = Grid.GetRow(b);
                    var c = Grid.GetColumn(b);
                    var rightBorder = (c == maxCols - 1) ? 0 : 1;
                    var bottomBorder = (r == rowList.Count - 1) ? 0 : 1;
                    b.BorderThickness = new Thickness(0, 0, rightBorder, bottomBorder);
                }
            }

            var tableContainer = new Border
            {
                Classes = { "BbTableContainer" },
                Child = grid
            };

            if (maxCols >= 6)
            {
                return new ScrollViewer
                {
                    Classes = { "BbTableScroll" },
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = tableContainer
                };
            }

            return tableContainer;
        }

        private Border CreateCellBorder(string content, bool isHeader, TextAlignment align)
        {
            var cellPanel = new StackPanel();
            RenderSubstringInto(cellPanel.Children, content, null, align);

            foreach (var child in cellPanel.Children)
            {
                if (child is TextBlock tb)
                {
                    if (isHeader) tb.FontWeight = FontWeight.Bold;
                    tb.TextWrapping = TextWrapping.Wrap;
                }
            }

            return new Border
            {
                Classes = { isHeader ? "BbTableHeader" : "BbTableCell" },
                Child = cellPanel
            };
        }

        private void RenderSubstringInto(Controls target, string content, string? stopTag, TextAlignment align)
        {
            var savedSrc = _src;
            var savedPos = _pos;
            _src = content;
            _pos = 0;
            try
            {
                RenderBlocks(target, stopTag, align);
            }
            finally
            {
                _src = savedSrc;
                _pos = savedPos;
            }
        }

        private Control BuildRawParagraphs(string content, TextAlignment align)
        {
            var stack = new StackPanel();
            foreach (var line in content.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                stack.Children.Add(new TextBlock
                {
                    Classes = { "BbParagraph" },
                    Text = line.Trim(),
                    TextAlignment = align,
                });
            }
            return stack;
        }

        private void FlushLiteral(Controls target, StringBuilder literal, TextAlignment align)
        {
            if (literal.Length == 0) return;
            var raw = literal.ToString();
            literal.Clear();

            var paragraphs = Regex.Split(raw, @"\n{2,}");
            foreach (var p in paragraphs)
            {
                var trimmed = p.Trim('\n');
                if (trimmed.Length == 0) continue;

                var block = BuildParagraph(trimmed, new BBStyle(), align);
                if (block != null) target.Add(block);
            }
        }

        private TextBlock? BuildParagraph(string text, BBStyle style, TextAlignment align)
        {
            var trimmed = text.Trim('\n');
            if (trimmed.Length == 0) return null;

            var inlines = ParseInline(trimmed, style, align);
            if (inlines.Count == 0) return null;

            var block = new TextBlock
            {
                Classes = { "BbParagraph" },
                TextAlignment = align,
            };
            var collection = new InlineCollection();
            foreach (var inline in inlines) collection.Add(inline);
            block.Inlines = collection;
            return block;
        }

        // ────────────────────────── Inline Parsing ──────────────────────────

        private List<Inline> ParseInline(string text, BBStyle style, TextAlignment align)
        {
            var inlines = new List<Inline>();
            var pending = new StringBuilder();
            var i = 0;
            var ct = _imageCts?.Token ?? CancellationToken.None;

            while (i < text.Length)
            {
                var m = s_tagRegex.Match(text, i);
                if (!m.Success)
                {
                    pending.Append(text.Substring(i));
                    i = text.Length;
                    break;
                }

                if (m.Index > i) pending.Append(text, i, m.Index - i);
                i = m.Index + m.Length;

                var isClose = m.Groups[1].Value == "/";
                var tag = m.Groups[2].Value.ToLowerInvariant();
                var param = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;

                if (isClose || !KnownInlineTags.Contains(tag))
                {
                    pending.Append(m.Value);
                    continue;
                }

                switch (tag)
                {
                    case "b":
                    case "i":
                    case "u":
                    case "s":
                    case "strike":
                    {
                        if (pending.Length > 0)
                        {
                            AddRun(inlines, pending.ToString(), style);
                            pending.Clear();
                        }

                        var (inner, next) = FindInlineClose(text, i, tag);
                        var innerStyle = style.Clone();
                        switch (tag)
                        {
                            case "b": innerStyle.Bold = true; break;
                            case "i": innerStyle.Italic = true; break;
                            case "u": innerStyle.Underline = true; break;
                            default: innerStyle.Strike = true; break;
                        }
                        foreach (var inline in ParseInline(inner, innerStyle, align)) inlines.Add(inline);
                        i = next;
                        break;
                    }
                    case "color":
                    {
                        if (pending.Length > 0)
                        {
                            AddRun(inlines, pending.ToString(), style);
                            pending.Clear();
                        }

                        var (inner, next) = FindInlineClose(text, i, tag);
                        var innerStyle = style.Clone();

                        if (!string.IsNullOrWhiteSpace(param))
                        {
                            var colorStr = param.Trim().TrimStart('=').Trim('"', '\'', ' ');
                            if (!colorStr.StartsWith("#") && (colorStr.Length == 6 || colorStr.Length == 8) && IsHexString(colorStr))
                            {
                                colorStr = "#" + colorStr;
                            }

                            if (Color.TryParse(colorStr, out var parsedColor))
                            {
                                innerStyle.Foreground = new SolidColorBrush(parsedColor);
                            }
                        }

                        foreach (var inline in ParseInline(inner, innerStyle, align)) inlines.Add(inline);
                        i = next;
                        break;
                    }
                    case "spoiler":
                    {
                        if (pending.Length > 0)
                        {
                            AddRun(inlines, pending.ToString(), style);
                            pending.Clear();
                        }

                        var (inner, next) = FindInlineClose(text, i, tag);
                        inlines.Add(new InlineUIContainer
                        {
                            Child = BuildInlineSpoiler(inner, style),
                            BaselineAlignment = BaselineAlignment.Center
                        });
                        i = next;
                        break;
                    }
                    case "url":
                    {
                        if (pending.Length > 0)
                        {
                            AddRun(inlines, pending.ToString(), style);
                            pending.Clear();
                        }

                        var (inner, next) = FindInlineClose(text, i, tag);
                        var rawUrl = !string.IsNullOrWhiteSpace(param) ? param : inner;
                        var url = rawUrl.Trim().TrimStart('=').Trim('"', '\'', ' ');

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            inlines.Add(new InlineUIContainer
                            {
                                Child = BuildLinkedUrlContent(url, inner, align, style, ct),
                                BaselineAlignment = BaselineAlignment.TextBottom,
                            });
                        }
                        else
                        {
                            pending.Append(inner.Length > 0 ? inner : m.Value);
                        }
                        i = next;
                        break;
                    }
                    case "img":
                    case "previewimg":
                    case "previewicon":
                    {
                        if (pending.Length > 0)
                        {
                            AddRun(inlines, pending.ToString(), style);
                            pending.Clear();
                        }

                        string inner = string.Empty;
                        if (string.IsNullOrWhiteSpace(param))
                        {
                            var (closeInner, next) = FindInlineClose(text, i, tag);
                            inner = closeInner;
                            i = next;
                        }
                        else
                        {
                            var closeMatch = Regex.Match(text.Substring(i), @"^\s*\[/\s*" + Regex.Escape(tag) + @"\s*\]", RegexOptions.IgnoreCase);
                            if (closeMatch.Success)
                            {
                                i += closeMatch.Length;
                            }
                        }

                        var url = ExtractImageUrl(tag, param, inner);

                        if (!string.IsNullOrWhiteSpace(url) && IsSafeUrl(url))
                        {
                            inlines.Add(new InlineUIContainer
                            {
                                Child = BuildImageControl(url, align, linkUrl: null, ct)
                            });
                        }
                        else if (!string.IsNullOrEmpty(inner))
                        {
                            pending.Append(inner);
                        }
                        break;
                    }
                    case "emoticon":
                    {
                        var (_, next) = FindInlineClose(text, i, tag);
                        i = next;
                        break;
                    }
                }
            }

            if (pending.Length > 0) AddRun(inlines, pending.ToString(), style);
            return inlines;
        }

        private static Control BuildInlineSpoiler(string text, BBStyle style)
        {
            var tb = new TextBlock
            {
                Text = text,
                Classes = { "BbSpoilerText" },
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            if (style.Bold) tb.FontWeight = FontWeight.Bold;
            if (style.Italic) tb.FontStyle = FontStyle.Italic;
            if (style.Foreground != null) tb.Foreground = style.Foreground;

            var border = new Border
            {
                Classes = { "BbSpoilerInline" },
                Child = tb,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var isRevealed = false;
            border.PointerPressed += (_, e) =>
            {
                isRevealed = !isRevealed;
                if (isRevealed) border.Classes.Add("revealed");
                else border.Classes.Remove("revealed");
                e.Handled = true;
            };

            ToolTip.SetTip(border, "Click to reveal spoiler");
            return border;
        }

        private static bool IsHexString(string text)
        {
            foreach (var c in text)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string ExtractImageUrl(string tag, string param, string inner)
        {
            var raw = !string.IsNullOrWhiteSpace(param) ? param : inner;
            raw = raw.Trim().TrimStart('=').Trim('"', '\'', ' ');

            if (tag.Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                return EnsureHttpUrl(raw);
            }

            var parts = raw.Split(';');
            var target = parts.Length > 2 ? parts[2] : parts[0];
            return EnsureHttpUrl(target.Trim('=', '"', '\'', ' '));
        }

        private static string? TryExtractImageUrlFromBBCode(string text)
        {
            text = text.Trim();
            var match = Regex.Match(
                text,
                @"^\[(img|previewimg|previewicon)(?:=([^\]]*)|(\s+[^\]]*))?\](.*?)(\[/\1\])?$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return null;

            var tag = match.Groups[1].Value;
            var param = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            var inner = match.Groups[4].Value;
            var url = ExtractImageUrl(tag, param, inner);
            return IsSafeUrl(url) ? url : null;
        }

        private static (string content, int next) FindInlineClose(string text, int start, string tag)
        {
            int search = start;
            while (true)
            {
                var m = s_tagRegex.Match(text, search);
                if (!m.Success)
                {
                    return (text.Substring(start), text.Length);
                }
                var after = m.Index + m.Length;
                if (m.Groups[1].Value == "/" &&
                    m.Groups[2].Value.Equals(tag, StringComparison.OrdinalIgnoreCase))
                {
                    return (text.Substring(start, m.Index - start), after);
                }
                search = after;
            }
        }

        private static void AddRun(List<Inline> inlines, string text, BBStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length > 0) AddTextWithAutoLinks(inlines, part, style);
                if (i < parts.Length - 1) inlines.Add(new LineBreak());
            }
        }

        private static void AddTextWithAutoLinks(List<Inline> inlines, string part, BBStyle style)
        {
            var masked = Regex.Replace(part, @"\[(img|previewimg|previewicon|url)\b[^\]]*\].*?\[/\1\]", (m) => new string(' ', m.Value.Length));

            var last = 0;
            foreach (Match m in s_bareUrl.Matches(masked))
            {
                var raw = TrimUrlTail(m.Value);
                var url = EnsureHttpUrl(raw);
                if (raw.Length == 0 || !IsSafeUrl(url)) continue;

                if (m.Index > last) inlines.Add(CreateRun(part.Substring(last, m.Index - last), style));
                inlines.Add(new InlineUIContainer
                {
                    Child = BuildLink(url, raw, style),
                    BaselineAlignment = BaselineAlignment.TextBottom,
                });
                last = m.Index + raw.Length;
            }
            if (last < part.Length) inlines.Add(CreateRun(part.Substring(last), style));
        }

        private static string TrimUrlTail(string url)
        {
            while (url.Length > 0)
            {
                var c = url[url.Length - 1];
                if (".,;:!?'\"*\u2026".IndexOf(c) >= 0) { url = url.Substring(0, url.Length - 1); continue; }
                if (c == ')' && url.Count(ch => ch == '(') < url.Count(ch => ch == ')')) { url = url.Substring(0, url.Length - 1); continue; }
                break;
            }
            return url;
        }

        private static Run CreateRun(string text, BBStyle style)
        {
            var run = new Run(text);
            if (style.Bold) run.FontWeight = FontWeight.Bold;
            if (style.Italic) run.FontStyle = FontStyle.Italic;
            if (style.Foreground != null) run.Foreground = style.Foreground;
            if (style.Underline || style.Strike)
            {
                var decorations = new TextDecorationCollection();
                if (style.Underline) foreach (var d in TextDecorations.Underline) decorations.Add(d);
                if (style.Strike) foreach (var d in TextDecorations.Strikethrough) decorations.Add(d);
                run.TextDecorations = decorations;
            }
            return run;
        }

        private static bool IsSafeUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        private sealed class BBStyle
        {
            public bool Bold;
            public bool Italic;
            public bool Underline;
            public bool Strike;
            public IBrush? Foreground;
            public BBStyle Clone() => (BBStyle)MemberwiseClone();
        }

        private static readonly Regex s_bareUrl = new Regex("\\b(?:https?://|www\\.)[^\\s\\[\\]<>\"\"]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ────────────────────────── Link & Imagem ──────────────────────────

        private static Control BuildLink(string url, string displayText, BBStyle? style = null)
        {
            var label = new TextBlock
            {
                Text = displayText,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 440,
                TextDecorations = TextDecorations.Underline,
            };

            if (style != null)
            {
                if (style.Bold) label.FontWeight = FontWeight.Bold;
                if (style.Italic) label.FontStyle = FontStyle.Italic;
                if (style.Foreground != null) label.Foreground = style.Foreground;
            }

            var button = new Button
            {
                Classes = { "BbLink" },
                Content = label,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            button.Click += (_, _) => OpenExternal(url);
            ToolTip.SetTip(button, url);
            return button;
        }

        private static Control BuildLinkedUrlContent(string url, string innerContent, TextAlignment align, BBStyle style, CancellationToken ct)
        {
            var finalUrl = EnsureHttpUrl(url);

            var imgUrl = TryExtractImageUrlFromBBCode(innerContent);
            if (!string.IsNullOrWhiteSpace(imgUrl))
            {
                return BuildImageControl(imgUrl, align, linkUrl: finalUrl, ct: ct);
            }

            var display = Regex
                .Replace(innerContent, @"\[/?[A-Za-z0-9_*]+\]", string.Empty)
                .Trim();
            if (display.Length == 0) display = finalUrl;
            return BuildLink(finalUrl, display, style);
        }

        private static string EnsureHttpUrl(string url)
        {
            url = url.Trim('"', '\'', ' ');
            if (!Uri.TryCreate(url, UriKind.Absolute, out _) &&
                url.IndexOf(':') < 0 &&
                url.IndexOf('.') > 0)
            {
                var candidate = "https://" + url.TrimStart('/');
                if (Uri.TryCreate(candidate, UriKind.Absolute, out _)) return candidate;
            }
            return url;
        }

        private static Control BuildImageControl(string url, TextAlignment align, string? linkUrl = null, CancellationToken ct = default)
        {
            var panel = new Panel
            {
                Margin = new Thickness(0, 4, 8, 8),
                HorizontalAlignment = align == TextAlignment.Center ? Avalonia.Layout.HorizontalAlignment.Center
                    : align == TextAlignment.Right ? Avalonia.Layout.HorizontalAlignment.Right
                    : Avalonia.Layout.HorizontalAlignment.Left,
            };

            var img = new Image
            {
                Stretch = Stretch.Uniform,
                MaxHeight = 420,
                MaxWidth = 440,
                HorizontalAlignment = align == TextAlignment.Center ? Avalonia.Layout.HorizontalAlignment.Center
                    : align == TextAlignment.Right ? Avalonia.Layout.HorizontalAlignment.Right
                    : Avalonia.Layout.HorizontalAlignment.Left,
                IsVisible = false,
            };

            RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);

            var placeholder = new TextBlock
            {
                Classes = { "BbImagePlaceholder" },
                Text = "[image] " + url,
            };

            panel.Children.Add(placeholder);
            panel.Children.Add(img);

            if (linkUrl != null)
            {
                var button = new Button
                {
                    Classes = { "BbLink" },
                    Content = panel,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                button.Click += (_, _) => OpenExternal(linkUrl);
                ToolTip.SetTip(button, linkUrl);
                _ = LoadImageAsync(img, placeholder, url, ct);
                return button;
            }

            _ = LoadImageAsync(img, placeholder, url, ct);
            return panel;
        }

        private static async Task LoadImageAsync(Image target, Control placeholder, string url, CancellationToken ct)
        {
            byte[] bytes;
            try
            {
                bytes = await s_http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (ct.IsCancellationRequested) return;

            var animatedFrames = await Task.Run(() => DecodeAnimatedFrames(bytes), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            if (animatedFrames != null && animatedFrames.Count > 1)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;

                    var firstFrame = animatedFrames[0].Frame;
                    var size = firstFrame.Size;
                    if (size.Width > 0 && size.Height > 0)
                    {
                        target.MaxWidth = Math.Min(size.Width, 440.0);
                        target.MaxHeight = Math.Min(size.Height, 420.0);
                    }

                    target.Source = firstFrame;
                    target.IsVisible = true;
                    placeholder.IsVisible = false;

                    _ = PlayAnimationAsync(target, animatedFrames, ct);
                });
                return;
            }

            IImage? source = null;
            if (!LooksLikeSvg(bytes))
            {
                using (var ms = new MemoryStream(bytes))
                {
                    try
                    {
                        ms.Position = 0;
                        source = new Bitmap(ms);
                    }
                    catch (Exception bmpEx)
                    {
                        Debug.WriteLine($"[SteamBBCode] Raster decode failed: {bmpEx.Message}");
                    }
                }

                if (source is Bitmap bmp && bmp.PixelSize.Width > 1600)
                {
                    using var ms2 = new MemoryStream(bytes);
                    try
                    {
                        ms2.Position = 0;
                        var scaled = Bitmap.DecodeToWidth(ms2, 800);
                        bmp.Dispose();
                        source = scaled;
                    }
                    catch { }
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;

                if (source == null)
                {
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        source = new Avalonia.Svg.Skia.SvgImage
                        {
                            Source = Avalonia.Svg.Skia.SvgSource.LoadFromStream(ms)
                        };
                    }
                    catch (Exception svgEx)
                    {
                        Debug.WriteLine($"[SteamBBCode] SVG decode failed: {svgEx.Message}");
                    }
                }
                if (source == null) return;

                var size = source.Size;
                if (size.Width > 0 && size.Height > 0)
                {
                    target.MaxWidth = Math.Min(size.Width, 440.0);
                    target.MaxHeight = Math.Min(size.Height, 420.0);
                }

                target.Source = source;
                target.IsVisible = true;
                placeholder.IsVisible = false;
            });
        }

        private static List<(Bitmap Frame, int DelayMs)>? DecodeAnimatedFrames(byte[] bytes)
        {
            try
            {
                using var skData = SKData.CreateCopy(bytes);
                using var codec = SKCodec.Create(skData);
                if (codec == null || codec.FrameCount <= 1) return null;

                var count = Math.Min(codec.FrameCount, 120);
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                var frames = new List<(Bitmap Frame, int DelayMs)>(count);
                var frameInfos = codec.FrameInfo;

                for (var i = 0; i < count; i++)
                {
                    using var frameBmp = new SKBitmap(info);
                    frameBmp.Erase(SKColors.Empty);

                    var res = codec.GetPixels(info, frameBmp.GetPixels(), new SKCodecOptions(i));
                    if (res != SKCodecResult.Success && res != SKCodecResult.IncompleteInput)
                    {
                        continue;
                    }

                    using var skImage = SKImage.FromBitmap(frameBmp);
                    using var pngData = skImage.Encode(SKEncodedImageFormat.Png, 100);
                    if (pngData == null) continue;

                    using var ms = new MemoryStream(pngData.ToArray());
                    var avaloniaBmp = new Bitmap(ms);

                    var fInfo = frameInfos != null && i < frameInfos.Length ? frameInfos[i] : default;
                    var delay = fInfo.Duration > 10 ? fInfo.Duration : 100;
                    frames.Add((avaloniaBmp, delay));
                }

                return frames.Count > 1 ? frames : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamBBCode] Animated decode error: {ex.Message}");
                return null;
            }
        }

        private static async Task PlayAnimationAsync(Image target, List<(Bitmap Frame, int DelayMs)> frames, CancellationToken ct)
        {
            var index = 0;
            while (!ct.IsCancellationRequested)
            {
                var (frame, delayMs) = frames[index];

                try
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (ct.IsCancellationRequested) break;

                var shouldContinue = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested || !target.IsAttachedToVisualTree())
                        return false;

                    target.Source = frame;
                    return true;
                }, DispatcherPriority.Normal);

                if (!shouldContinue) break;

                index = (index + 1) % frames.Count;
            }
        }

        private static bool LooksLikeSvg(byte[] bytes)
        {
            if (bytes.Length < 4) return false;
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
            return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }

        private static void OpenExternal(string url)
        {
            try
            {
                url = UnwrapSteamLinkFilter(EnsureHttpUrl(url));
                if (!IsSafeUrl(url)) return;

                RimSharp.Core.Services.SystemLauncher.OpenPathOrUrl(url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamBBCode] Failed to open URL '{url}': {ex.Message}");
            }
        }

        private static string UnwrapSteamLinkFilter(string url)
        {
            if (url.Contains("steamcommunity.com/linkfilter/", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"[?&](?:url|u)=([^&]+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var raw = Uri.UnescapeDataString(m.Groups[1].Value);
                    return EnsureHttpUrl(raw);
                }
            }
            return url;
        }

        private static IEnumerable<string> SplitTopLevel(string text, string marker)
        {
            var parts = new List<string>();
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                if (text.Trim().Length > 0) parts.Add(text);
                return parts;
            }
            while (true)
            {
                var start = idx + marker.Length;
                var next = text.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase);
                if (next < 0) { parts.Add(text.Substring(start)); break; }
                parts.Add(text.Substring(start, next - start));
                idx = next;
            }
            return parts;
        }
    }
}