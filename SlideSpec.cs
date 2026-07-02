namespace HtmlToSlidesPro;

/// <summary>Canonical slide dimensions used by HtmlToSlidesPro for HTML capture and PPTX output.</summary>
public static class SlideSpec
{
    public const int ViewportWidthPx = 1280;
    public const int ViewportHeightPx = 720;
    public const int SlideHeightPx = ViewportHeightPx;
    public const double AspectRatio = 16.0 / 9.0;
    public const double SlideWidthInches = 13.333;
    public const double SlideHeightInches = 7.5;

    public const string Summary =
        "Design target: 1280×720 px per slide (16:9). PowerPoint output: 13.333″×7.5″ widescreen. " +
        "Stack content in 720 px-tall sections for pixel-perfect splits.";
}
