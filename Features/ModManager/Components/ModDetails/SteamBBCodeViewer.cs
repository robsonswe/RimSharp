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
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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
            { "b", "i", "u", "s", "strike", "url", "img", "previewimg", "previewicon", "emoticon" };

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
            
            // Normaliza quebras de linha Windows/Unix e espaços insecáveis (\u00A0)
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

                    if (tag is "hr" or "*")
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
                        // Imagens no nível raiz são tratadas como blocos para não ficarem inline com o texto
                        FlushLiteral(target, literal, align);
                        var content = ConsumeUntilClose(tag, balanced: false);
                        var url = ExtractImageUrl(tag, param, content);
                        if (!string.IsNullOrWhiteSpace(url) && IsSafeUrl(url))
                        {
                            target.Add(BuildImageControl(url, align));
                        }
                        break;
                    }
                    case "url":
                    {
                        // Se for um link envolvendo uma imagem [url=...][img]...[/img][/url], tratamos como bloco
                        var savedPos = _pos;
                        var content = ConsumeUntilClose("url", balanced: false);
                        var trimmedContent = content.Trim();
                        if (trimmedContent.StartsWith("[img", StringComparison.OrdinalIgnoreCase) &&
                            trimmedContent.EndsWith("[/img]", StringComparison.OrdinalIgnoreCase))
                        {
                            FlushLiteral(target, literal, align);
                            var rawUrl = !string.IsNullOrWhiteSpace(param) ? param : "";
                            var linkUrl = rawUrl.Trim().TrimStart('=').Trim('"', '\'', ' ');
                            var imgMatch = Regex.Match(trimmedContent, @"\[img\s*\](.*?)\[/img\s*\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                            if (imgMatch.Success)
                            {
                                var imgUrl = EnsureHttpUrl(imgMatch.Groups[1].Value.Trim());
                                if (IsSafeUrl(imgUrl))
                                {
                                    target.Add(BuildImageControl(imgUrl, align, linkUrl: linkUrl));
                                    break;
                                }
                            }
                        }

                        // Caso contrário é um link de texto inline normal; continua no literal
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
                        target.Add(BuildList(tag == "olist", content, align));
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
                        target.Add(new Expander
                        {
                            Classes = { "BbSpoiler" },
                            Header = "Spoiler",
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

            // Captura qualquer cabeçalho ou texto que esteja antes do primeiro [*]
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

            foreach (var rawRow in rows)
            {
                var rowText = rawRow.Replace("[/tr]", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('\n');
                if (string.IsNullOrWhiteSpace(rowText)) continue;

                var rowIndex = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var cellMatches = Regex.Matches(rowText, @"\[(td|th)\](.*?)\[/\1\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var col = 0;

                if (cellMatches.Count > 0)
                {
                    foreach (Match cellMatch in cellMatches)
                    {
                        var isHeader = cellMatch.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
                        var cellContent = cellMatch.Groups[2].Value.Trim('\n');

                        var cellPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 4) };
                        RenderSubstringInto(cellPanel.Children, cellContent, null, align);

                        if (isHeader)
                        {
                            foreach (var child in cellPanel.Children)
                            {
                                if (child is TextBlock tb) tb.FontWeight = FontWeight.Bold;
                            }
                        }

                        Grid.SetRow(cellPanel, rowIndex);
                        Grid.SetColumn(cellPanel, col++);
                        grid.Children.Add(cellPanel);
                    }
                }
                else
                {
                    foreach (var rawCell in SplitTopLevel(rowText, "[td]"))
                    {
                        var cell = rawCell.Trim('\n');
                        if (cell.Length == 0) continue;

                        var cellPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 4) };
                        RenderSubstringInto(cellPanel.Children, cell, null, align);
                        Grid.SetRow(cellPanel, rowIndex);
                        Grid.SetColumn(cellPanel, col++);
                        grid.Children.Add(cellPanel);
                    }
                }

                maxCols = Math.Max(maxCols, col);
            }

            for (var c = 0; c < maxCols; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }
            return grid;
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

            // Separa parágrafos por linhas em branco consecutivas (\n\n+)
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
                        // IMPORTANTE: Dá flush no texto acumulado antes da tag formatada!
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
                                Child = BuildLinkedUrlContent(url, inner, align, style),
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

                        var (inner, next) = FindInlineClose(text, i, tag);
                        var url = ExtractImageUrl(tag, param, inner);

                        if (!string.IsNullOrWhiteSpace(url) && IsSafeUrl(url))
                        {
                            inlines.Add(new InlineUIContainer
                            {
                                Child = BuildImageControl(url, align)
                            });
                        }
                        else
                        {
                            pending.Append(inner);
                        }
                        i = next;
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

        private static string ExtractImageUrl(string tag, string param, string inner)
        {
            if (tag == "img") return EnsureHttpUrl(inner.Trim());

            var raw = !string.IsNullOrWhiteSpace(param) ? param : inner;
            var parts = raw.Split(';');
            var target = parts.Length > 2 ? parts[2] : parts[0];
            return EnsureHttpUrl(target.Trim('=', '"', '\'', ' '));
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
            }

            var button = new Button
            {
                Classes = { "BbLink" },
                Content = label,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            button.Click += (_, _) => OpenExternal(button, url);
            ToolTip.SetTip(button, url);
            return button;
        }

        private static Control BuildLinkedUrlContent(string url, string innerContent, TextAlignment align, BBStyle style)
        {
            var finalUrl = EnsureHttpUrl(url);

            var imgMatch = Regex.Match(
                innerContent,
                @"\[img\s*\](.*?)\[/img\s*\]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (imgMatch.Success)
            {
                var imgUrl = EnsureHttpUrl(imgMatch.Groups[1].Value.Trim());
                if (IsSafeUrl(imgUrl))
                {
                    return BuildImageControl(imgUrl, align, linkUrl: finalUrl);
                }
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

        private static Control BuildImageControl(string url, TextAlignment align, string? linkUrl = null)
        {
            var panel = new Panel
            {
                Margin = new Thickness(0, 4, 0, 8),
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
                button.Click += (_, _) => OpenExternal(button, linkUrl);
                ToolTip.SetTip(button, linkUrl);
                _ = LoadImageAsync(img, placeholder, url);
                return button;
            }

            _ = LoadImageAsync(img, placeholder, url);
            return panel;
        }

        private static async Task LoadImageAsync(Image target, Control placeholder, string url)
        {
            byte[] bytes;
            try
            {
                bytes = await s_http.GetByteArrayAsync(url).ConfigureAwait(false);
            }
            catch
            {
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

        private static bool LooksLikeSvg(byte[] bytes)
        {
            if (bytes.Length < 4) return false;
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
            return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }

        private static async void OpenExternal(Visual visual, string url)
        {
            try
            {
                url = EnsureHttpUrl(url);
                if (!IsSafeUrl(url)) return;

                var topLevel = TopLevel.GetTopLevel(visual);
                if (topLevel?.Launcher != null)
                {
                    await topLevel.Launcher.LaunchUriAsync(new Uri(url));
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamBBCode] Failed to open URL '{url}': {ex.Message}");
            }
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