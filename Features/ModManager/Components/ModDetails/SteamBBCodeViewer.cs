#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// Renders Steam Workshop description text (Steam-flavored BBCode:
    /// [h1..h3], [b], [i], [u], [s]/[strike], [list]/[olist] with [*],
    /// [quote=author], [url=...], [img], [spoiler], [code]/[pre], [hr],
    /// [table], [previewyoutube]) as native Avalonia controls. Plain text
    /// without tags renders as simple wrapped paragraphs — descriptions
    /// cached without BB markup keep working unchanged. Unknown tags stay
    /// literal so nothing is silently dropped.
    /// </summary>
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

        // Steam tag names only (letters/digits/underscore) — plain text like
        // "[Dotgae's mod]" does not match and stays literal text.
        private static readonly Regex s_tagRegex =
            new(@"\[(/?)([A-Za-z0-9_]+)(=[^\]]*)?\s*\]", RegexOptions.Compiled);

        private static readonly HashSet<string> KnownInlineTags = new(StringComparer.OrdinalIgnoreCase)
            { "b", "i", "u", "s", "strike", "url", "img", "emoticon" };

        private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private string _src = string.Empty;
        private int _pos;

        private void Rebuild()
        {
            Children.Clear();
            _src = Text ?? string.Empty;
            _pos = 0;
            if (_src.Length == 0) return;
            RenderBlocks(Children, stopTag: null, align: TextAlignment.Left);
        }

        /// <summary>
        /// Consumes _src until the matching close of <paramref name="stopTag"/>
        /// (stopped content NOT consumed) or end of text, appending controls.
        /// </summary>
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
                var param = m.Groups[3].Value;

                if (isClose)
                {
                    // Include the text since the last consumed position — the
                    // gap before a close tag. Without this, stray closes (like
                    // the [/url] of an inline link flowing at block scope)
                    // silently DROPPED the text in between (the anchor label).
                    if (m.Index > _pos) literal.Append(_src, _pos, m.Index - _pos);
                    _pos = m.Index;

                    if (stopTag != null && tag.Equals(stopTag, StringComparison.OrdinalIgnoreCase))
                    {
                        FlushLiteral(target, literal, align);
                        _pos = posAfterTag;
                        return;
                    }
                    literal.Append(m.Value); // stray close → literal text
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
                            heading.Classes.Add(tag); // styled as TextBlock.h1/.h2/.h3
                            target.Add(heading);
                        }
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
                        var content = ConsumeUntilClose(tag, balanced: false)
                            .TrimEnd('\n', '\r');
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
                    {
                        FlushLiteral(target, literal, align);
                        var wrap = new StackPanel();
                        RenderBlocks(wrap.Children, stopTag: tag,
                            align: tag == "center" ? TextAlignment.Center : TextAlignment.Right);
                        target.Add(wrap);
                        break;
                    }
                    case "left":
                    {
                        FlushLiteral(target, literal, align);
                        var wrap = new StackPanel();
                        RenderBlocks(wrap.Children, stopTag: tag, align: TextAlignment.Left);
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
                        var rawId = param.Length > 0 ? param.Substring(1) : content;
                        var videoId = rawId.Trim().Split(';')[0].Trim('"');
                        if (!string.IsNullOrWhiteSpace(videoId))
                        {
                            target.Add(BuildLink(
                                $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}",
                                "▶ YouTube preview"));
                        }
                        break;
                    }
                    default:
                        // Inline tags (b/i/u/s/url/img/…) and unknown tags are
                        // accumulated into the pending paragraph; ParseInline
                        // resolves them as inlines when it is flushed, so text
                        // like "Use [url=..]RimSort[/url] to sort" renders as
                        // ONE flowing paragraph instead of three blocks.
                        literal.Append(m.Value);
                        break;
                }
            }
            FlushLiteral(target, literal, align);
        }

        /// <summary>Consumes content up to the close of <paramref name="tag"/> and advances _pos past it.</summary>
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
            var author = param.Length > 0 ? param.Substring(1).Trim() : string.Empty;
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
            var items = SplitTopLevel(content, "[*]");
            var counter = 0;
            foreach (var rawItem in items)
            {
                var item = rawItem.Trim('\n', '\r', ' ', '\t');
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
            foreach (var rawRow in SplitTopLevel(content, "[tr]"))
            {
                var rowText = rawRow
                    .Replace("[/tr]", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Trim('\n', '\r');
                if (rowText.Length == 0) continue;

                var rowIndex = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var cells = SplitTopLevel(rowText, "[td]");
                var col = 0;
                foreach (var rawCell in cells)
                {
                    var cell = rawCell
                        .Replace("[/td]", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Trim('\n', '\r');
                    if (cell.Length == 0) { col++; continue; }

                    var cellPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 4) };
                    RenderSubstringInto(cellPanel.Children, cell, null, align);
                    Grid.SetRow(cellPanel, rowIndex);
                    Grid.SetColumn(cellPanel, col);
                    grid.Children.Add(cellPanel);
                    col++;
                }
                maxCols = Math.Max(maxCols, col);
            }

            for (var c = 0; c < maxCols; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }
            return grid;
        }

        /// <summary>Renders a standalone substring with full recursion (stream state temporarily swapped).</summary>
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
            var paragraph = BuildParagraph(literal.ToString(), new BBStyle(), align);
            if (paragraph != null) target.Add(paragraph);
            literal.Clear();
        }

        private TextBlock? BuildParagraph(string text, BBStyle style, TextAlignment align)
        {
            var trimmed = text.Trim('\n', '\r');
            if (trimmed.Length == 0) return null;

            var inlines = ParseInline(trimmed, style);
            if (inlines.Count == 0) return null;

            // IMPORTANT: plain TextBlock, NOT SelectableTextBlock. The selectable
            // variant captures pointer input for text selection, which swallows
            // Tapped on inline links/images and made clicks never open the browser.
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

        // ────────────────────────── inline parsing ──────────────────────────

        private List<Inline> ParseInline(string text, BBStyle style)
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
                var param = m.Groups[3].Value;

                if (isClose || !KnownInlineTags.Contains(tag))
                {
                    pending.Append(m.Value); // literal bracket text
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
                        var (inner, next) = FindInlineClose(text, i, tag);
                        var innerStyle = style.Clone();
                        switch (tag)
                        {
                            case "b": innerStyle.Bold = true; break;
                            case "i": innerStyle.Italic = true; break;
                            case "u": innerStyle.Underline = true; break;
                            default: innerStyle.Strike = true; break;
                        }
                        foreach (var inline in ParseInline(inner, innerStyle)) inlines.Add(inline);
                        i = next;
                        break;
                    }
                    case "url":
                    {
                        var (inner, next) = FindInlineClose(text, i, tag);
                        var url = param.Length > 0 ? param.Substring(1).Trim() : inner.Trim();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            // Flush the pending text FIRST so the link inline
                            // keeps its position between the surrounding runs.
                            if (pending.Length > 0)
                            {
                                AddRun(inlines, pending.ToString(), style);
                                pending.Clear();
                            }
                            inlines.Add(new InlineUIContainer
                            {
                                Child = BuildLinkedUrlContent(url, inner, TextAlignment.Left)
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
                    {
                        var (inner, next) = FindInlineClose(text, i, tag);
                        var url = EnsureHttpUrl(inner.Trim());
                        if (!string.IsNullOrWhiteSpace(url) && IsSafeUrl(url))
                        {
                            // Flush pending first to keep ordering (see [url]).
                            if (pending.Length > 0)
                            {
                                AddRun(inlines, pending.ToString(), style);
                                pending.Clear();
                            }
                            inlines.Add(new InlineUIContainer
                            {
                                Child = BuildImageControl(url, TextAlignment.Left)
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
                        i = next; // emoticons: dropped entirely
                        break;
                    }
                }
            }

            if (pending.Length > 0) AddRun(inlines, pending.ToString(), style);
            return inlines;
        }

        private static (string content, int next) FindInlineClose(string text, int start, string tag)
        {
            // Scan forward until the CLOSE of THIS tag — intermediate tags
            // (e.g. [img] inside [url]) are content, not the terminator.
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
                var part = parts[i].Replace("\r", string.Empty);
                if (part.Length > 0)
                {
                    var run = new Run(part);
                    if (style.Bold) run.FontWeight = FontWeight.Bold;
                    if (style.Italic) run.FontStyle = FontStyle.Italic;
                    TextDecorationCollection? decorations = null;
                    if (style.Underline && style.Strike)
                    {
                        decorations = new TextDecorationCollection();
                        foreach (var d in TextDecorations.Underline) decorations.Add(d);
                        foreach (var d in TextDecorations.Strikethrough) decorations.Add(d);
                    }
                    else if (style.Underline) decorations = TextDecorations.Underline;
                    else if (style.Strike) decorations = TextDecorations.Strikethrough;
                    if (decorations != null) run.TextDecorations = decorations;
                    inlines.Add(run);
                }
                if (i < parts.Length - 1) inlines.Add(new LineBreak());
            }
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

        // ────────────────────────── link & image ──────────────────────────

        private static Control BuildLink(string url, string displayText)
        {
            var label = new TextBlock
            {
                Text = displayText,
                // NoWrap: a wrapping link would measure as a full line and push
                // the surrounding text onto its own row. Long displays are
                // trimmed; the click still opens the full URL.
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 440,
                TextDecorations = TextDecorations.Underline,
            };

            // REAL Button instead of Tapped-on-TextBlock: buttons own the
            // pointer interaction, so Click always fires regardless of any
            // ancestor capture/swallowing that made Tapped unreliable.
            var button = new Button
            {
                Classes = { "BbLink" },
                Content = label,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            button.Click += (_, _) =>
            {
                Debug.WriteLine($"[SteamBBCode] Link clicked: {url}");
                OpenExternal(url);
            };
            ToolTip.SetTip(button, url);
            return button;
        }

        /// <summary>
        /// Builds the visual for one [url] region: a linked image when the
        /// inner content is an [img] (Steam's standard banner-link pattern),
        /// otherwise a plain text link with residual BB tags stripped.
        /// </summary>
        private static Control BuildLinkedUrlContent(string url, string innerContent, TextAlignment align)
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
                .Replace(innerContent, @"\[/?[A-Za-z0-9_]+\]", string.Empty)
                .Trim();
            if (display.Length == 0) display = finalUrl;
            return BuildLink(finalUrl, display);
        }

        /// <summary>Adds https:// to domain-shaped URLs that lack a scheme.</summary>
        private static string EnsureHttpUrl(string url)
        {
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
            var panel = new Panel();
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
                // Images wrapped in [url=..][img]..[/img][/url] open the link.
                var button = new Button
                {
                    Classes = { "BbLink" },
                    Content = panel,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                button.Click += (_, _) =>
                {
                    Debug.WriteLine($"[SteamBBCode] Linked image clicked: {linkUrl}");
                    OpenExternal(linkUrl);
                };
                ToolTip.SetTip(button, linkUrl);
                // The image must ALSO load when it is a link banner — this is
                // Steam's standard [url][img] pattern. Miss: placeholder
                // forever (the image never downloaded).
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
                return; // keep the textual placeholder
            }

            IImage? source = null;
            if (!LooksLikeSvg(bytes))
            {
                // Natural-size decode: DecodeToWidth would also UPSCALE small
                // images to the requested width (a 1×1 became 800×800). Only
                // re-decode when the source is genuinely huge.
                using (var ms = new MemoryStream(bytes))
                {
                    try
                    {
                        ms.Position = 0;
                        source = new Bitmap(ms);
                    }
                    catch (Exception bmpEx)
                    {
                        Debug.WriteLine($"[SteamBBCode] Raster decode failed for {url}: {bmpEx.Message}");
                    }
                }

                if (source is Bitmap bmp && bmp.PixelSize.Width > 1600)
                {
                    // Huge screenshots: downscale for memory/display sanity.
                    using var ms2 = new MemoryStream(bytes);
                    try
                    {
                        ms2.Position = 0;
                        var scaled = Bitmap.DecodeToWidth(ms2, 800);
                        bmp.Dispose();
                        source = scaled;
                    }
                    catch
                    {
                        // keep the natural-size bitmap on re-decode failure
                    }
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (source == null)
                {
                    // SVG content (img.shields.io badges etc.) or a failed
                    // raster decode. SvgImage/SvgSource are Avalonia objects:
                    // decode on the UI thread.
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
                        Debug.WriteLine($"[SteamBBCode] SVG decode failed for {url}: {svgEx.Message}");
                    }
                }
                if (source == null) return; // keep the textual placeholder

                // Never UPSCALE and never become rigid: cap the maximum at
                // the intrinsic size (Uniform cannot grow past Max*), while
                // leaving measure free so the image SHRINKS to fit tight
                // containers (table cells ≈214px each). Explicit Width/Height
                // would clip inside Star cells — the table image bug.
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
            var head = System.Text.Encoding.ASCII.GetString(
                bytes, 0, Math.Min(bytes.Length, 1024));
            return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }

        private static void OpenExternal(string url)
        {
            try
            {
                url = EnsureHttpUrl(url);
                if (IsSafeUrl(url))
                {
                    // Same pattern the Open Workshop context menu command uses.
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else
                {
                    Debug.WriteLine($"[SteamBBCode] Refused to open unsafe URL: {url}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamBBCode] Failed to open '{url}': {ex.Message}");
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
