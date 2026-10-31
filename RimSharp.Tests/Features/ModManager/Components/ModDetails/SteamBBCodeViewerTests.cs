using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using RimSharp.Features.ModManager.Components.ModDetails;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Components.ModDetails
{
    /// <summary>
    /// SteamBBCodeViewer rendering against the REAL cached Steam description
    /// of workshop item 2038940371 (Alpha Muffalo).
    /// </summary>
    public class SteamBBCodeViewerTests
    {
        public SteamBBCodeViewerTests()
        {
            TestApp.InitializeTestApp();
        }

        private const string AnchorLine =
            "[*] Use [url=https://github.com/RimSort/RimSort/releases/latest]RimSort[/url] to sort your mods";

        private const string LinkedBadge =
            "[url=https://steamcommunity.com/sharedfiles/filedetails/changelog/2038940371]" +
            "[img]https://img.shields.io/github/v/release/emipa606/AlphaMuffalo?label=latest%20version&style=plastic&color=9f1111&labelColor=black[/img]" +
            "[/url] | muffalo, texture";

        private static SteamBBCodeViewer CreateViewer(string text)
        {
            var viewer = new SteamBBCodeViewer { Text = text };
            var window = new Window { Width = 450, Height = 800, Content = viewer };
            window.Show();
            viewer.Measure(new Size(450, double.PositiveInfinity));
            viewer.Arrange(new Rect(0, 0, 450, 400));
            viewer.UpdateLayout();
            return viewer;
        }

        private static IEnumerable<string> InlineSequence(SteamBBCodeViewer viewer)
        {
            foreach (var block in viewer.Children.OfType<TextBlock>())
            {
                if (block.Inlines == null) continue;
                foreach (var inline in block.Inlines)
                {
                    if (inline is Run run) yield return $"Run '{run.Text}'";
                    else if (inline is InlineUIContainer ui && ui.Child is Button b)
                    {
                        var label = "";
                        if (b.Content is TextBlock tb) label = tb.Text ?? "";
                        yield return $"Button '{b.Content switch
                        {
                            TextBlock => "text:" + label,
                            _ => "visual:" + b.Content.GetType().Name // image
                        }}'";
                    }
                }
            }
        }

        [AvaloniaFact]
        public void AnchorLink_ShowsAnchorText_InFlowOrder()
        {
            var viewer = CreateViewer(AnchorLine);
            var seq = InlineSequence(viewer).ToList();

            var buttonIndex = seq.FindIndex(s => s.StartsWith("Button 'text:"));
            var label = seq.FirstOrDefault(s => s.StartsWith("Button 'text:")) ?? "";
            var runBefore = seq.Take(buttonIndex).OfType<string>().ToList();

            // Assert (failure message prints the whole inline sequence).
            Assert.True(
                label.Contains("text:RimSort"),
                $"anchor label should be 'RimSort' but got: [{string.Join(" | ", seq)}]");

            var beforeText = string.Join("", runBefore.Where(s => s.StartsWith("Run '")));
            Assert.True(
                beforeText.Contains("Use "),
                $"'Use ' must be BEFORE the link, sequence: [{string.Join(" | ", seq)}]");

            // Anchor text itself must not leak into the surrounding Run.
            Assert.DoesNotContain("RimSort", beforeText);

            var afterText = string.Join("", seq.Skip(buttonIndex + 1).Where(s => s.StartsWith("Run '")));
            Assert.Contains("to sort your mods", afterText);
            Assert.DoesNotContain("RimSort", afterText);
        }

        [AvaloniaFact]
        public void LinkedBadge_RendersImageButtonNotText()
        {
            var viewer = CreateViewer(LinkedBadge);

            // The [url][img] badge must render exactly ONE Image descendant
            // (the badge banner), not a text-label fallback.
            var images = viewer.GetVisualDescendants().OfType<Image>().ToList();
            Assert.True(
                images.Count == 1,
                $"expected one Image for the [url][img] badge, got {images.Count}");

            // Fallback safety: no linked element may show a plain-text label
            // (that is what "fica só o link" looked like).
            var textLabelButtons = viewer.GetVisualDescendants()
                .OfType<Button>()
                .Where(b => b.Content is TextBlock && b.Classes.Contains("BbLink"))
                .Any(b => ((TextBlock)b.Content).Text is { Length: > 0 });
            Assert.False(textLabelButtons,
                "linked badge must not fall back to URL text labels");
        }

        [AvaloniaFact]
        public void TrailingSuffix_StaysAfterTheBadge()
        {
            var viewer = CreateViewer(LinkedBadge);
            var seq = InlineSequence(viewer).ToList();
            var joined = string.Join(" | ", seq);

            // "|" suffix text (outside the badge) must survive, in its own run
            // AFTER the badge/insertion — not injected before it.
            var suffixIndex = seq.FindIndex(s => s.Contains("muffalo"));
            Assert.True(suffixIndex >= 0, $"suffix '| muffalo, texture' missing; seq: {joined}");
        }

        // ─────────────── image download/decode/display path ───────────────

        // Minimal 1×1 PNG (opaque), known seed bytes.
        private const string TinyPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        private const string TinySvg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"20\">" +
            "<rect width=\"100\" height=\"20\" fill=\"#9f1111\"/></svg>";

        /// <summary>
        /// A real HTTP server on localhost that serves ONE byte payload, so the
        /// whole download → decode → visibility-swap pipeline runs end to end
        /// without external network.
        /// </summary>
        private sealed class LocalImageServer : IDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly byte[] _payload;
            private readonly string _contentType;

            public string ContentType => _contentType;
            public string Url { get; }

            public static LocalImageServer Start(byte[] payload, string contentType)
            {
                // Ephemeral port with retries: tests run for two TFMs in parallel.
                for (var attempt = 0; ; attempt++)
                {
                    var server = new LocalImageServer(payload, contentType);
                    try
                    {
                        server._listener.Start();
                        _ = Task.Run(server.ServeLoop);
                        return server;
                    }
                    catch when (attempt < 20)
                    {
                        server._listener.Close();
                    }
                }
            }

            private LocalImageServer(byte[] payload, string contentType)
            {
                _payload = payload;
                _contentType = contentType;
                var port = Random.Shared.Next(49000, 65000);
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/img/");
                Url = $"http://127.0.0.1:{port}/img/pixel";
            }

            private void ServeLoop()
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var ctx = _listener.GetContext();
                        var resp = ctx.Response;
                        resp.ContentType = _contentType;
                        resp.ContentLength64 = _payload.Length;
                        resp.OutputStream.Write(_payload, 0, _payload.Length);
                        resp.OutputStream.Close();
                    }
                    catch
                    {
                        // listener shut down mid-request — ignore
                    }
                }
            }

            public void Dispose() =>
                ((IDisposable)_listener).Dispose();
        }

        private static async Task<Image?> WaitForLoadedImage(
            SteamBBCodeViewer viewer, double waitSeconds)
        {
            // The link banner wraps the Image in a Button → find it in depth.
            var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (DateTime.UtcNow < deadline)
            {
                var img = viewer.GetVisualDescendants().OfType<Image>()
                    .FirstOrDefault(i => i.Source != null);
                if (img != null) return img;
                await Task.Delay(50);
            }
            return viewer.GetVisualDescendants().OfType<Image>().FirstOrDefault();
        }

        private static string ServedBadge(string imgUrl) =>
            $"[url=https://steamcommunity.com/sharedfiles/filedetails/changelog/2038940371]" +
            $"[img]{imgUrl}[/img][/url]";

        [AvaloniaFact]
        public async Task LinkedImage_PngDownloadsAndDisplays()
        {
            // Regression: linked images ([url][img]) previously never started
            // the load — BuildImageControl skipped LoadImageAsync for linked
            // variant. Steam banner-links are the dominant image pattern.
            using var server = LocalImageServer.Start(
                Convert.FromBase64String(TinyPngBase64), "image/png");

            var viewer = CreateViewer(ServedBadge(server.Url));
            var img = await WaitForLoadedImage(viewer, waitSeconds: 5);

            Assert.True(img != null, "no Image created for linked [url][img]");
            Assert.True(img!.Source != null,
                $"Image.Source never populated — placeholder stayed: " +
                $"'{((TextBlock)((img.Parent as Panel)!).Children[0]).Text}'");
            Assert.True(img.IsVisible,
                "Image downloaded and decoded but remains IsVisible=false");

            // No-upscale: a 1×1 raster stays 1×1 (never stretched to fit).
            viewer.UpdateLayout();
            Assert.True(img.Bounds.Width <= 6,
                $"1×1 PNG stretched to {img.Bounds.Width} — upscale guard failed");
        }

        [AvaloniaFact]
        public async Task LinkedImage_SvgFallsBackAndDisplays()
        {
            // img.shields.io style: content-type image/svg+xml — Skia bitmap
            // decode fails, the SVG fallback must decode and display instead.
            var svgBytes = Encoding.UTF8.GetBytes(TinySvg);
            using var server = LocalImageServer.Start(svgBytes, "image/svg+xml");

            var viewer = CreateViewer(ServedBadge(server.Url));
            var img = await WaitForLoadedImage(viewer, waitSeconds: 5);

            Assert.True(img != null, "no Image created for linked [url][img]");
            Assert.True(img!.Source != null,
                "SVG fallback decode failed — Source stays null");
            Assert.True(img.IsVisible,
                "SVG decoded but Image remains IsVisible=false");

            // Natural-size guard: a 100×20 source must NOT be upscaled to the
            // MaxWidth cap (the "badge enorme" bug). The source must also be a
            // real SvgImage — Skia's raster decoder used to silently convert
            // SVG bytes into a container-sized Bitmap.
            viewer.UpdateLayout();
            Assert.True(img.Source is Avalonia.Svg.Skia.SvgImage,
                $"expected SvgImage source for SVG bytes, got {img.Source!.GetType().FullName}");
            Assert.True(img.Bounds.Width <= 120,
                $"100×20 SVG stretched to {img.Bounds.Width}×{img.Bounds.Height} — " +
                $"upscale guard failed (natural {img.Source.Size.Width}×{img.Source.Size.Height})");
        }

        /// <summary>Generates a banner-shaped SVG (313×100 units, same shape
        /// as the img.litet.net logos of the Alpha Muffalo description). Note:
        /// headless uses a DUMMY render interface (Bitmap.Decode → 1×1 fake,
        /// Save → 0 bytes), so raster sizing cannot be tested here; SvgImage
        /// gets its intrinsic size from Svg.Skia's picture bounds and measures
        /// faithfully through the same layout code path.</summary>
        private static byte[] MakeBannerPng(int width = 313, int height = 100)
        {
            return Encoding.ASCII.GetBytes(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\">" +
                "<rect width=\"50%\" height=\"50%\" fill=\"#ffffff\"/>" +
                "<rect x=\"50%\" y=\"50%\" width=\"50%\" height=\"50%\" fill=\"#ffffff\"/>" +
                "</svg>");
        }

        [AvaloniaFact]
        public async Task TableBanners_KeepNaturalSizeAndShrinkToCell()
        {
            // The REAL Alpha Muffalo layout: a 2-column [table] whose cells are
            // linked banners. Star cells give each image ≈214px; the image must
            // shrink to fit (no rigid 313px clipping) but never upscale.
            var banner = MakeBannerPng(); // 313×100, aspect ≈ 3.13
            using var server = LocalImageServer.Start(banner, "image/svg+xml");

            var bb = "[table]\n [tr]\n " +
                     $"[td][url=https://discord.gg/h5TY6DA][img]{server.Url}[/img][/url][/td]\n " +
                     $"[td][img]{server.Url}[/img][/td]\n [/tr]\n[/table]";
            var viewer = CreateViewer(bb);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            Image[] images = Array.Empty<Image>();
            while (DateTime.UtcNow < deadline)
            {
                images = viewer.GetVisualDescendants().OfType<Image>()
                    .Where(i => i.Source != null).ToArray();
                if (images.Length >= 2) break;
                await Task.Delay(50);
            }

            Assert.True(images.Length >= 2, $"expected 2 loaded banner images, got {images.Length}");
            viewer.UpdateLayout();

            foreach (var img in images)
            {
                // Shrunk to the Star cell (~207px content) — not rigid 313.
                var w = img.Bounds.Width;
                var h = img.Bounds.Height;
                var bmpDump = img.Source is Avalonia.Media.Imaging.Bitmap pb
                    ? $"pixel={pb.PixelSize.Width}x{pb.PixelSize.Height} dpi={pb.Dpi.X}"
                    : img.Source!.GetType().Name;
                Assert.True(w is > 100 and < 240,
                    $"banner should shrink to the table cell, got {w}×{h} [{bmpDump}] (clipped/rigid?)");
                // Aspect must be preserved while shrinking (313/100 ≈ 3.13).
                var aspect = w / Math.Max(1, h);
                Assert.True(aspect is > 2.0 and < 4.5,
                    $"aspect distorted: {w}×{h}");
            }
        }

        [AvaloniaFact]
        public async Task StandaloneBanner_MaxNaturalSizeNeverUpscaled()
        {
            var banner = MakeBannerPng(313, 100);
            using var server = LocalImageServer.Start(banner, "image/svg+xml");

            // Standalone [img] in a plain paragraph — must render at natural
            // 313×100 (never stretched to the 440 MaxWidth).
            var viewer = CreateViewer($"[img]{server.Url}[/img]");
            var img = await WaitForLoadedImage(viewer, waitSeconds: 5);

            Assert.True(img != null && img!.Source != null, "banner never loaded");
            viewer.UpdateLayout();
            var w = img!.Bounds.Width;
            var bmpDump = img.Source is Avalonia.Media.Imaging.Bitmap pb
                ? $"pixel={pb.PixelSize.Width}x{pb.PixelSize.Height} dpi={pb.Dpi.X} dip={pb.Size.Width}x{pb.Size.Height}"
                : img.Source!.GetType().Name;
            Assert.True(w is > 280 and <= 320,
                $"313-wide banner rendered at {w} [{bmpDump}] — upscaled or wrong sizing");
        }

		[AvaloniaFact]
		public void VoidImgTag_DoesNotSwallowSubsequentContent()
		{
			// Sintaxe exata do EPOE Retexture: [img=url] sem [/img] no início
			const string epoeBbCode =
				"[img=https://i.imgur.com/tE28cZ7.gif]\r\n\r\n" +
				"[h1][b]EPOE Retexture[/b][/h1]\r\n" +
				"[h3]· Safe to add or remove from an existing save[/h3]\r\n\r\n" +
				"[img=https://seobongzu.com/assets/images/rimworld/EPOE1.webp]\r\n" +
				"[h2]Replaces EPOE and RBSE textures[/h2]\r\n" +
				"[url=https://buymeacoffee.com/seobongzu][img]https://i.imgur.com/t7IGD5Y.png[/img][/url]";

			var viewer = CreateViewer(epoeBbCode);

			// 1. Títulos e cabeçalhos devem existir no visual
			var textBlocks = viewer.GetVisualDescendants().OfType<TextBlock>().ToList();
			Assert.Contains(textBlocks, tb => tb.Text?.Contains("EPOE Retexture") == true || 
											 tb.Inlines?.Any(i => (i as Run)?.Text?.Contains("EPOE Retexture") == true) == true);
			Assert.Contains(textBlocks, tb => tb.Text?.Contains("Replaces EPOE and RBSE") == true ||
											 tb.Inlines?.Any(i => (i as Run)?.Text?.Contains("Replaces EPOE and RBSE") == true) == true);

			// 2. Não pode existir nenhum texto órfão "[/url]"
			var rawText = string.Join(" ", textBlocks.Select(tb => tb.Text ?? string.Join("", tb.Inlines?.OfType<Run>().Select(r => r.Text) ?? Array.Empty<string>())));
			Assert.DoesNotContain("[/url]", rawText);
		}

		[AvaloniaFact]
		public void GravshipStorage_RendersHeadingsTableAndBanners()
		{
			// Sintaxe do Gravship Storage com tabela e múltiplas [img=url]
			const string gravshipBbCode =
				"[img]https://i.imgur.com/tE28cZ7.gif[/img]\r\n\r\n" +
				"[h1][b]Gravship Storage[/b][/h1]\r\n" +
				"[img=https://seobongzu.com/assets/images/rimworld/GravshipStorage1.webp]\r\n" +
				"[table]\r\n[tr][th]Name[/th][th]Cost[/th][/tr]\r\n[tr][td]Shelf[/td][td]Steel 10[/td][/tr]\r\n[/table]\r\n" +
				"[url=https://buymeacoffee.com/seobongzu][img]https://i.imgur.com/t7IGD5Y.png[/img][/url]";

			var viewer = CreateViewer(gravshipBbCode);

			// Tabela deve ser instanciada como Grid com classe BbTable
			var table = viewer.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Classes.Contains("BbTable"));
			Assert.NotNull(table);

			// Título preservado
			var textBlocks = viewer.GetVisualDescendants().OfType<TextBlock>().ToList();
			Assert.Contains(textBlocks, tb => tb.Text?.Contains("Gravship Storage") == true || 
											 tb.Inlines?.Any(i => (i as Run)?.Text?.Contains("Gravship Storage") == true) == true);

			// [/url] não pode aparecer solto
			var rawText = string.Join(" ", textBlocks.Select(tb => tb.Text ?? string.Join("", tb.Inlines?.OfType<Run>().Select(r => r.Text) ?? Array.Empty<string>())));
			Assert.DoesNotContain("[/url]", rawText);
		}

    }
}
