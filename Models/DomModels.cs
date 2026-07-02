namespace HtmlToSlidesPro.Models;

public sealed class ConversionOptions
{
    public const int DefaultViewportWidth = 1280;

    public int ViewportWidth { get; set; } = DefaultViewportWidth;
    public int ViewportHeight => (int)(ViewportWidth * 9.0 / 16.0);
    public int SlideHeightPx => ViewportHeight;
    public bool EmbedFonts { get; set; }
    public ConversionMode Mode { get; set; } = ConversionMode.HighFidelity;
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.1:8b";
    public bool SkipLlmPlanning { get; set; }
    public string? OutputPath { get; set; }
    public string? LibreOfficePath { get; set; }
    /// <summary>Document Y positions (px) where slides 2+ begin.</summary>
    public List<int>? SlideSplitYs { get; set; }
    /// <summary>When true, SlideSplitYs from preview are used (empty list = single slide).</summary>
    public bool UsePreviewSlideSplits { get; set; }
    /// <summary>Document height (px) measured in preview when splits were set.</summary>
    public int PreviewTotalHeightPx { get; set; }

    public bool HighFidelityMode => Mode == ConversionMode.HighFidelity;
}

public sealed class DomExtractionResult
{
    public int TotalHeight { get; set; }
    public int SlideCount { get; set; }
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }
    public int SlideHeight { get; set; }
    public List<SlideDomData> Slides { get; set; } = [];
}

public sealed class SlideDomData
{
    public int SlideIndex { get; set; }
    public int SlideTop { get; set; }
    public int SlideHeight { get; set; }
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }
    public List<DomElementData> Elements { get; set; } = [];
}

public sealed class DomElementData
{
    public string Path { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Id { get; set; } = "";
    public string Classes { get; set; } = "";
    public int PaintOrder { get; set; }
    public int HtsId { get; set; }
    public int ZIndex { get; set; }
    public RectData Rect { get; set; } = new();
    public RectData AbsoluteRect { get; set; } = new();
    public ComputedStyleData Style { get; set; } = new();
    public string Text { get; set; } = "";
    public List<LineRectData> LineRects { get; set; } = [];
    public ElementFlags Flags { get; set; } = new();
    public ImageData? Image { get; set; }
    public List<PseudoElementData> PseudoElements { get; set; } = [];
    public string? RasterSelector { get; set; }
}

public sealed class RectData
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class LineRectData
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Text { get; set; } = "";
}

public sealed class ComputedStyleData
{
    public string BackgroundColor { get; set; } = "";
    public string BackgroundImage { get; set; } = "";
    public string Color { get; set; } = "";
    public string FontFamily { get; set; } = "";
    public string FontSize { get; set; } = "";
    public string FontWeight { get; set; } = "";
    public string LineHeight { get; set; } = "";
    public string LetterSpacing { get; set; } = "";
    public string TextAlign { get; set; } = "";
    public string BorderRadius { get; set; } = "";
    public string BorderColor { get; set; } = "";
    public string BorderWidth { get; set; } = "";
    public string BorderStyle { get; set; } = "";
    public string BoxShadow { get; set; } = "";
    public string Opacity { get; set; } = "";
    public string ZIndex { get; set; } = "";
    public string Position { get; set; } = "";
    public string Filter { get; set; } = "";
    public string BackdropFilter { get; set; } = "";
    public string ClipPath { get; set; } = "";
    public string TextDecoration { get; set; } = "";
    public string WhiteSpace { get; set; } = "";
}

public sealed class ElementFlags
{
    public bool IsImage { get; set; }
    public bool IsInlineSvg { get; set; }
    public bool IsIconFont { get; set; }
    public bool IsCheckbox { get; set; }
    public bool IsFormControl { get; set; }
    public bool HasFilter { get; set; }
    public bool HasBackdropFilter { get; set; }
    public bool HasClipPath { get; set; }
    public bool NeedsRasterize { get; set; }
    public string? RasterizeReason { get; set; }
    public bool Checked { get; set; }
}

public sealed class ImageData
{
    public string Src { get; set; } = "";
    public int NaturalWidth { get; set; }
    public int NaturalHeight { get; set; }
}

public sealed class PseudoElementData
{
    public string Pseudo { get; set; } = "";
    public string Content { get; set; } = "";
    public RectData Rect { get; set; } = new();
    public ComputedStyleData Style { get; set; } = new();
}

public enum ShapeOutputType
{
    Rectangle,
    RoundedRectangle,
    TextBox,
    Picture,
    Checkbox,
    RasterizedImage,
    SlideRaster,
    GradientFill,
    Skipped,
    LlmPlanned
}

public sealed class ConversionDiagnostic
{
    public string ElementPath { get; set; } = "";
    public string Tag { get; set; } = "";
    public int SlideIndex { get; set; }
    public ShapeOutputType ShapeType { get; set; }
    public bool Rasterized { get; set; }
    public string? RasterizeReason { get; set; }
    public string? Notes { get; set; }
    public string? Error { get; set; }
}

public sealed class QaComparisonItem
{
    public int SlideIndex { get; set; }
    public string? PuppeteerImagePath { get; set; }
    public string? LibreOfficeImagePath { get; set; }
    public int? ClipTop { get; set; }
    public int? ClipHeight { get; set; }
    public string? Caption { get; set; }
}

public sealed class FullPagePreviewResult
{
    public string ImagePath { get; set; } = "";
    public int TotalHeight { get; set; }
    public int ViewportWidth { get; set; }
    public List<int> DefaultSplitYs { get; set; } = [];
}

public sealed class ConversionResult
{
    public string OutputPath { get; set; } = "";
    public DomExtractionResult Extraction { get; set; } = new();
    public List<ConversionDiagnostic> Diagnostics { get; set; } = [];
    public List<QaComparisonItem> QaComparisons { get; set; } = [];
    public List<string> FontWarnings { get; set; } = [];
}
