using DocumentFormat.OpenXml.Packaging;
using HtmlToSlidesPro.Models;
using PuppeteerSharp;
using ShapeCrawler;
using ShapeCrawler.Texts;
using P = DocumentFormat.OpenXml.Presentation;

namespace HtmlToSlidesPro.Services;

public sealed class PptxGeneratorService(
    CssParser cssParser,
    ImageResolver imageResolver,
    ElementRasterizer rasterizer)
{
    private readonly CssParser _css = cssParser;
    private readonly ImageResolver _images = imageResolver;
    private readonly ElementRasterizer _rasterizer = rasterizer;

    public async Task<(string OutputPath, List<ConversionDiagnostic> Diagnostics)> GenerateAsync(
        DomExtractionResult extraction,
        IPage page,
        ConversionOptions options,
        string? htmlBaseDirectory,
        IReadOnlyList<byte[]>? slideScreenshots = null,
        IProgress<ConversionDiagnostic>? progress = null,
        CancellationToken ct = default)
    {
        var outputPath = options.OutputPath
                         ?? Path.Combine(Path.GetTempPath(), "HtmlToSlidesPro", $"output_{DateTime.Now:yyyyMMdd_HHmmss}.pptx");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var diagnostics = new List<ConversionDiagnostic>();
        using var pres = new Presentation(p =>
        {
            for (var i = 0; i < extraction.SlideCount; i++)
                p.Slide();
        });

        for (var slideIdx = 0; slideIdx < extraction.Slides.Count; slideIdx++)
        {
            ct.ThrowIfCancellationRequested();
            var slideData = extraction.Slides[slideIdx];
            var slide = pres.Slide(slideIdx + 1);
            var shapes = slide.Shapes;

            if (options.HighFidelityMode && slideScreenshots is not null && slideIdx < slideScreenshots.Count)
            {
                await AddSlideBackgroundAsync(shapes, slideScreenshots[slideIdx], options, slideIdx, slideData.SlideHeight, diagnostics, progress,
                    "High-fidelity mode", ShapeOutputType.SlideRaster);
                continue;
            }

            foreach (var element in slideData.Elements)
            {
                var diag = new ConversionDiagnostic
                {
                    ElementPath = element.Path,
                    Tag = element.Tag,
                    SlideIndex = slideIdx
                };

                try
                {
                    await ProcessElementAsync(page, shapes, slide, element, slideData, options, htmlBaseDirectory, diag, ct);
                }
                catch (Exception ex)
                {
                    diag.ShapeType = ShapeOutputType.Skipped;
                    diag.Error = ex.Message;
                    diag.Rasterized = true;
                    diag.RasterizeReason = "Style parsing or shape creation failed";

                    var png = await _rasterizer.RasterizeByPathAsync(page, element.Path, element.HtsId, ct);
                    if (png is not null)
                    {
                        await AddPictureShapeAsync(shapes, png, element.Rect, options.ViewportWidth, slideData.SlideHeight);
                        diag.ShapeType = ShapeOutputType.RasterizedImage;
                    }
                }

                diagnostics.Add(diag);
                progress?.Report(diag);
            }
        }

        pres.Save(outputPath);
        ApplyPendingOpenXmlMutations(outputPath, pres);
        EnsureSlideSize(outputPath, pres);

        if (options.EmbedFonts)
            FontEmbedder.EmbedFontFiles(outputPath, Path.Combine(AppContext.BaseDirectory, "tools", "fonts"));

        return (outputPath, diagnostics);
    }

    private void EnsureSlideSize(string outputPath, Presentation pres)
    {
        pres.Save(outputPath);
        using var doc = PresentationDocument.Open(outputPath, true);
        var presPart = doc.PresentationPart;
        if (presPart?.Presentation is null) return;

        presPart.Presentation.SlideSize = new P.SlideSize
        {
            Cx = (int)(CoordinateMapper.SlideWidthInches * CoordinateMapper.EmuPerInch),
            Cy = (int)(CoordinateMapper.SlideHeightInches * CoordinateMapper.EmuPerInch),
            Type = P.SlideSizeValues.Custom
        };
        presPart.Presentation.NotesSize = new P.NotesSize
        {
            Cx = (int)(CoordinateMapper.SlideWidthInches * CoordinateMapper.EmuPerInch),
            Cy = (int)(CoordinateMapper.SlideHeightInches * CoordinateMapper.EmuPerInch)
        };
        presPart.Presentation.Save();
    }

    private static async Task AddSlideBackgroundAsync(
        IUserSlideShapeCollection shapes,
        byte[] screenshot,
        ConversionOptions options,
        int slideIdx,
        int slideHeightPx,
        List<ConversionDiagnostic> diagnostics,
        IProgress<ConversionDiagnostic>? progress,
        string notes,
        ShapeOutputType shapeType)
    {
        var slideDiag = new ConversionDiagnostic
        {
            ElementPath = $"(slide {slideIdx + 1})",
            Tag = "slide",
            SlideIndex = slideIdx,
            ShapeType = shapeType,
            Rasterized = true,
            RasterizeReason = "Slide screenshot",
            Notes = notes
        };

        var fullSlide = new RectData
        {
            X = 0,
            Y = 0,
            Width = options.ViewportWidth,
            Height = slideHeightPx
        };
        await AddPictureShapeAsync(shapes, screenshot, fullSlide, options.ViewportWidth, slideHeightPx, fillSlide: true);
        diagnostics.Add(slideDiag);
        progress?.Report(slideDiag);
    }

    private async Task ProcessElementAsync(
        IPage page,
        IUserSlideShapeCollection shapes,
        IUserSlide slide,
        DomElementData element,
        SlideDomData slideData,
        ConversionOptions options,
        string? htmlBaseDirectory,
        ConversionDiagnostic diag,
        CancellationToken ct)
    {
        var (x, y, w, h) = CoordinateMapper.MapRectToSlide(element.Rect, options.ViewportWidth, slideData.SlideHeight);
        if (w <= 0 || h <= 0)
        {
            diag.ShapeType = ShapeOutputType.Skipped;
            diag.Notes = "Zero-size element";
            return;
        }

        if (element.Flags.NeedsRasterize)
        {
            await AddRasterizedAsync(page, shapes, element, options, slideData.SlideHeight, diag, ct);
            return;
        }

        if (element.Flags.IsImage && element.Image is not null)
        {
            await AddImageAsync(shapes, element, options, htmlBaseDirectory, slideData.SlideHeight, diag, ct);
            return;
        }

        if (element.Flags.IsCheckbox)
        {
            AddCheckbox(shapes, element, options, slideData.SlideHeight, diag);
            return;
        }

        var bgColor = _css.ParseColor(element.Style.BackgroundColor);
        var linear = _css.ParseLinearGradient(element.Style.BackgroundImage);
        var radialMid = _css.ParseRadialMidpointColor(element.Style.BackgroundImage);
        var borderColor = _css.ParseColor(element.Style.BorderColor);
        var borderWidth = _css.ParsePx(element.Style.BorderWidth);
        var radius = _css.ParsePx(element.Style.BorderRadius);
        var shadow = _css.ParseBoxShadow(element.Style.BoxShadow);

        var hasBackground = _css.IsVisibleFill(bgColor) || linear is not null || radialMid is not null;
        if (hasBackground)
        {
            var geom = Geometry.Rectangle;
            shapes.AddShape((int)x, (int)y, (int)Math.Max(1, w), (int)Math.Max(1, h), geom);
            var bgShape = shapes.Last();
            bgShape.TextBox?.SetText("");

            if (linear is not null)
            {
                diag.ShapeType = ShapeOutputType.GradientFill;
                ApplyOpenXmlShape(slide, bgShape, s =>
                {
                    OoxmlStyleHelper.ApplyLinearGradient(s, linear, _css);
                    if (radius > 0) OoxmlStyleHelper.ApplyRoundedCorners(s, radius, element.Rect.Width, element.Rect.Height);
                });
            }
            else
            {
                diag.ShapeType = radius > 0 ? ShapeOutputType.RoundedRectangle : ShapeOutputType.Rectangle;
                var fillColor = bgColor ?? radialMid;
                if (fillColor is not null)
                    bgShape.Fill?.SetColor(_css.ToHex(fillColor));
            }

            if (borderColor is not null && borderWidth > 0)
            {
                ApplyOpenXmlShape(slide, bgShape, s =>
                    OoxmlStyleHelper.ApplyOutline(s, borderColor, borderWidth, options.ViewportWidth, _css));
            }

            if (shadow is not null)
            {
                ApplyOpenXmlShape(slide, bgShape, s =>
                    OoxmlStyleHelper.ApplyOuterShadow(s, shadow, options.ViewportWidth, _css));
            }
        }

        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            AddText(shapes, element, options, slideData.SlideHeight, diag);
        }

        foreach (var pseudo in element.PseudoElements)
        {
            diag.Notes = (diag.Notes ?? "") + $" Pseudo {pseudo.Pseudo}: {pseudo.Content};";
        }

        if (diag.ShapeType == default && !string.IsNullOrWhiteSpace(element.Text))
            diag.ShapeType = ShapeOutputType.TextBox;
        else if (diag.ShapeType == default)
            diag.ShapeType = ShapeOutputType.Skipped;
    }

    private void AddText(IUserSlideShapeCollection shapes, DomElementData element, ConversionOptions options, int slideHeightPx, ConversionDiagnostic diag)
    {
        var usePerLine = ShouldUsePerLineBoxes(element);
        if (usePerLine)
        {
            foreach (var line in element.LineRects.Where(l => !string.IsNullOrWhiteSpace(l.Text)))
            {
                var (lx, ly, lw, lh) = CoordinateMapper.MapRectToSlide(new RectData
                {
                    X = line.X,
                    Y = line.Y,
                    Width = line.Width,
                    Height = line.Height
                }, options.ViewportWidth, slideHeightPx);
                shapes.AddTextBox((int)lx, (int)ly, (int)Math.Max(1, lw), (int)Math.Max(1, lh), line.Text);
                StyleTextBox(shapes.Last(), element, options);
            }

            diag.ShapeType = ShapeOutputType.TextBox;
            diag.Notes = "Per-line text boxes for pixel-accurate wrapping";
            return;
        }

        var (x, y, w, h) = CoordinateMapper.MapRectToSlide(element.Rect, options.ViewportWidth, slideHeightPx);
        shapes.AddTextBox((int)x, (int)y, (int)Math.Max(1, w), (int)Math.Max(1, h), element.Text);
        StyleTextBox(shapes.Last(), element, options);
        diag.ShapeType = ShapeOutputType.TextBox;
    }

    private static bool ShouldUsePerLineBoxes(DomElementData element)
    {
        if (element.LineRects.Count <= 1) return false;
        var fullText = string.Join("", element.LineRects.Select(l => l.Text)).Replace(" ", "");
        var direct = element.Text.Replace(" ", "");
        if (fullText.Length < direct.Length * 0.8) return true;

        var totalLineHeight = element.LineRects.Sum(l => l.Height);
        if (totalLineHeight > element.Rect.Height * 1.25) return true;
        return false;
    }

    private void StyleTextBox(IShape shape, DomElementData element, ConversionOptions options)
    {
        var tb = shape.TextBox;
        if (tb is null) return;

        tb.AutofitType = AutofitType.None;
        var fontName = FontMapper.Resolve(element.Style.FontFamily);
        var fontSize = _css.ParseFontSizePt(element.Style.FontSize, options.ViewportWidth);
        var color = _css.ParseColor(element.Style.Color);

        if (tb.Paragraphs.Count > 0)
        {
            var para = tb.Paragraphs[0];
            para.SetFontName(fontName);
            para.SetFontSize((int)Math.Max(6, Math.Round(fontSize)));
            if (color is not null)
                para.SetFontColor(_css.ToHex(color));

            para.HorizontalAlignment = element.Style.TextAlign switch
            {
                "center" => TextHorizontalAlignment.Center,
                "right" or "end" => TextHorizontalAlignment.Right,
                "justify" => TextHorizontalAlignment.Justify,
                _ => TextHorizontalAlignment.Left
            };

            if (tb.Paragraphs[0].Portions.Count > 0)
                tb.Paragraphs[0].Portions[0].Font!.IsBold = _css.IsBold(element.Style.FontWeight);
        }
    }

    private async Task AddImageAsync(
        IUserSlideShapeCollection shapes,
        DomElementData element,
        ConversionOptions options,
        string? htmlBaseDirectory,
        int slideHeightPx,
        ConversionDiagnostic diag,
        CancellationToken ct)
    {
        var bytes = await _images.ResolveAsync(element.Image!.Src, htmlBaseDirectory, ct);
        if (bytes is null)
        {
            diag.ShapeType = ShapeOutputType.Skipped;
            diag.Notes = "Could not resolve image bytes";
            return;
        }

        await AddPictureShapeAsync(shapes, bytes, element.Rect, options.ViewportWidth, slideHeightPx);
        diag.ShapeType = ShapeOutputType.Picture;
    }

    private static async Task AddPictureShapeAsync(
        IUserSlideShapeCollection shapes,
        byte[] bytes,
        RectData rect,
        int viewportWidth,
        int slideHeightPx,
        bool fillSlide = false)
    {
        using var ms = new MemoryStream(bytes);
        shapes.AddPicture(ms);
        var pic = shapes.Last();

        if (fillSlide)
        {
            var (fx, fy, fw, fh) = CoordinateMapper.FullSlideBoundsPoints();
            pic.X = fx;
            pic.Y = fy;
            pic.Width = fw;
            pic.Height = fh;
        }
        else
        {
            var (x, y, w, h) = CoordinateMapper.MapRectToSlide(rect, viewportWidth, slideHeightPx);
            pic.X = (int)x;
            pic.Y = (int)y;
            pic.Width = (int)Math.Max(1, w);
            pic.Height = (int)Math.Max(1, h);
        }

        await Task.CompletedTask;
    }

    private void AddCheckbox(IUserSlideShapeCollection shapes, DomElementData element, ConversionOptions options, int slideHeightPx, ConversionDiagnostic diag)
    {
        var boxSize = Math.Min(element.Rect.Width, element.Rect.Height);
        var boxRect = new RectData
        {
            X = element.Rect.X,
            Y = element.Rect.Y + (element.Rect.Height - boxSize) / 2,
            Width = boxSize,
            Height = boxSize
        };
        var (bx, by, bw, bh) = CoordinateMapper.MapRectToSlide(boxRect, options.ViewportWidth, slideHeightPx);
        shapes.AddShape((int)bx, (int)by, (int)Math.Max(4, bw), (int)Math.Max(4, bh), Geometry.Rectangle);
        var box = shapes.Last();
        box.TextBox?.SetText("");
        if (element.Flags.Checked)
            box.Fill?.SetColor("2563EB");
        else
            box.Fill?.SetColor("FFFFFF");

        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            var labelRect = new RectData
            {
                X = element.Rect.X + boxSize + 4,
                Y = element.Rect.Y,
                Width = Math.Max(1, element.Rect.Width - boxSize - 4),
                Height = element.Rect.Height
            };
            var (lx, ly, lw, lh) = CoordinateMapper.MapRectToSlide(labelRect, options.ViewportWidth, slideHeightPx);
            shapes.AddTextBox((int)lx, (int)ly, (int)lw, (int)lh, element.Text);
            StyleTextBox(shapes.Last(), element, options);
        }

        diag.ShapeType = ShapeOutputType.Checkbox;
    }

    private async Task AddRasterizedAsync(
        IPage page,
        IUserSlideShapeCollection shapes,
        DomElementData element,
        ConversionOptions options,
        int slideHeightPx,
        ConversionDiagnostic diag,
        CancellationToken ct)
    {
        var png = await _rasterizer.RasterizeByPathAsync(page, element.Path, element.HtsId, ct);
        if (png is null)
        {
            diag.ShapeType = ShapeOutputType.Skipped;
            diag.Rasterized = true;
            diag.RasterizeReason = element.Flags.RasterizeReason ?? "Rasterization failed";
            return;
        }

        await AddPictureShapeAsync(shapes, png, element.Rect, options.ViewportWidth, slideHeightPx);
        diag.ShapeType = ShapeOutputType.RasterizedImage;
        diag.Rasterized = true;
        diag.RasterizeReason = element.Flags.RasterizeReason;
    }

    private static void ApplyOpenXmlShape(IUserSlide slide, IShape shape, Action<P.Shape> mutate)
    {
        var slideNumber = slide.Number;
        // ShapeCrawler does not expose OpenXml shape directly; reopen after save is handled per-shape via presentation part.
        // Mutation is applied on next save pass through cached path index — see post-save hook in pipeline.
        _pendingMutations.Add((slideNumber, shape.Id, mutate));
    }

    private static readonly List<(int SlideNumber, int ShapeId, Action<P.Shape> Mutate)> _pendingMutations = [];

    public static void ApplyPendingOpenXmlMutations(string pptxPath, Presentation pres)
    {
        if (_pendingMutations.Count == 0) return;
        using var doc = PresentationDocument.Open(pptxPath, true);
        foreach (var group in _pendingMutations.GroupBy(m => m.SlideNumber))
        {
            var slidePart = doc.PresentationPart?.SlideParts.ElementAtOrDefault(group.Key - 1);
            if (slidePart is null) continue;

            foreach (var item in group)
            {
                var shape = slidePart.Slide?.CommonSlideData?.ShapeTree?.Elements<P.Shape>()
                    .FirstOrDefault(s => s.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value == (uint)item.ShapeId);
                if (shape is not null)
                    item.Mutate(shape);
            }
        }

        _pendingMutations.Clear();
        doc.Save();
    }
}
