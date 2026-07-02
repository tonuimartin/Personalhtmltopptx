using DocumentFormat.OpenXml.Packaging;
using HtmlToSlidesPro.Models;
using ShapeCrawler;
using ShapeCrawler.Texts;
using P = DocumentFormat.OpenXml.Presentation;

namespace HtmlToSlidesPro.Services;

public sealed class SlidePlanRendererService
{
    private readonly CssParser _css = new();

    public Task<(string OutputPath, List<ConversionDiagnostic> Diagnostics)> GenerateAsync(
        SlidePlan plan,
        ConversionOptions options,
        IProgress<ConversionDiagnostic>? progress = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var outputPath = options.OutputPath
                         ?? Path.Combine(Path.GetTempPath(), "HtmlToSlidesPro", $"llm_{DateTime.Now:yyyyMMdd_HHmmss}.pptx");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var diagnostics = new List<ConversionDiagnostic>();
        var slideCount = Math.Max(1, plan.Slides.Count);

        using var pres = new Presentation(p =>
        {
            for (var i = 0; i < slideCount; i++)
                p.Slide();
        });

        for (var i = 0; i < slideCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var slidePlan = plan.Slides.ElementAtOrDefault(i) ?? new SlidePlanSlide();
            var slide = pres.Slide(i + 1);
            var shapes = slide.Shapes;

            if (!string.IsNullOrWhiteSpace(slidePlan.BackgroundColor))
            {
                var bgColor = _css.ParseColor(slidePlan.BackgroundColor);
                if (bgColor is not null)
                {
                    shapes.AddShape(0, 0, (int)CoordinateMapper.SlideWidthPoints, (int)CoordinateMapper.SlideHeightPoints, Geometry.Rectangle);
                    var bg = shapes.Last();
                    bg.TextBox?.SetText("");
                    bg.Fill?.SetColor(_css.ToHex(bgColor));
                }
            }

            foreach (var shapePlan in slidePlan.Shapes)
            {
                var diag = new ConversionDiagnostic
                {
                    SlideIndex = i,
                    Tag = shapePlan.Type,
                    ElementPath = shapePlan.SourceId ?? shapePlan.Text?[..Math.Min(40, shapePlan.Text?.Length ?? 0)] ?? "(shape)",
                    ShapeType = ShapeOutputType.TextBox,
                    Notes = "LLM semantic layout"
                };

                try
                {
                    RenderShape(shapes, shapePlan);
                    diag.ShapeType = shapePlan.Type switch
                    {
                        "roundedrect" => ShapeOutputType.RoundedRectangle,
                        "rect" => ShapeOutputType.Rectangle,
                        _ => ShapeOutputType.LlmPlanned
                    };
                }
                catch (Exception ex)
                {
                    diag.Error = ex.Message;
                    diag.ShapeType = ShapeOutputType.Skipped;
                }

                diagnostics.Add(diag);
                progress?.Report(diag);
            }
        }

        pres.Save(outputPath);
        EnsureSlideSize(outputPath);

        if (options.EmbedFonts)
            FontEmbedder.EmbedFontFiles(outputPath, Path.Combine(AppContext.BaseDirectory, "tools", "fonts"));

        return Task.FromResult((outputPath, diagnostics));
    }

    private void RenderShape(IUserSlideShapeCollection shapes, SlidePlanShape plan)
    {
        var xPt = plan.X * CoordinateMapper.PointsPerInch;
        var yPt = plan.Y * CoordinateMapper.PointsPerInch;
        var wPt = plan.W * CoordinateMapper.PointsPerInch;
        var hPt = plan.H * CoordinateMapper.PointsPerInch;

        var type = plan.Type.ToLowerInvariant();
        if (type is "rect" or "roundedrect")
        {
            shapes.AddShape((int)xPt, (int)yPt, (int)Math.Max(1, wPt), (int)Math.Max(1, hPt), Geometry.Rectangle);
            var shape = shapes.Last();
            shape.TextBox?.SetText("");
            if (!string.IsNullOrWhiteSpace(plan.Fill))
            {
                var fill = _css.ParseColor(plan.Fill);
                if (fill is not null)
                    shape.Fill?.SetColor(_css.ToHex(fill));
            }

            return;
        }

        shapes.AddTextBox((int)xPt, (int)yPt, (int)Math.Max(1, wPt), (int)Math.Max(1, hPt), plan.Text ?? "");
        var tb = shapes.Last().TextBox;
        if (tb is null) return;

        tb.AutofitType = AutofitType.None;
        if (tb.Paragraphs.Count == 0) return;

        var para = tb.Paragraphs[0];
        para.Text = plan.Text ?? "";
        para.SetFontName(FontMapper.Resolve(plan.FontFamily ?? "Calibri"));
        para.SetFontSize((int)Math.Round(plan.FontSize ?? 16));
        if (!string.IsNullOrWhiteSpace(plan.Color))
            para.SetFontColor(NormalizeHex(plan.Color));

        para.HorizontalAlignment = (plan.Align ?? "left").ToLowerInvariant() switch
        {
            "center" => TextHorizontalAlignment.Center,
            "right" => TextHorizontalAlignment.Right,
            _ => TextHorizontalAlignment.Left
        };

        if (tb.Paragraphs[0].Portions.Count > 0 && plan.Bold == true)
            tb.Paragraphs[0].Portions[0].Font!.IsBold = true;
    }

    private static string NormalizeHex(string color)
    {
        var c = color.Trim().TrimStart('#');
        return c.Length is 6 or 8 ? c : "000000";
    }

    private static void EnsureSlideSize(string outputPath)
    {
        using var doc = PresentationDocument.Open(outputPath, true);
        var presPart = doc.PresentationPart;
        if (presPart?.Presentation is null) return;

        presPart.Presentation.SlideSize = new P.SlideSize
        {
            Cx = (int)(CoordinateMapper.SlideWidthInches * CoordinateMapper.EmuPerInch),
            Cy = (int)(CoordinateMapper.SlideHeightInches * CoordinateMapper.EmuPerInch),
            Type = P.SlideSizeValues.Custom
        };
        presPart.Presentation.Save();
    }
}
