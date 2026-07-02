using HtmlToSlidesPro.Models;

namespace HtmlToSlidesPro.Services;

public static class CoordinateMapper
{
    public const double SlideWidthInches = 13.333;
    public const double SlideHeightInches = 7.5;
    public const double PointsPerInch = 72.0;
    public const double SlideWidthPoints = SlideWidthInches * PointsPerInch;
    public const double SlideHeightPoints = SlideHeightInches * PointsPerInch;
    public const long EmuPerInch = 914400;

    public static double PxToPoints(double px, int viewportWidthPx)
    {
        var scale = SlideWidthPoints / viewportWidthPx;
        return px * scale;
    }

    public static (double x, double y, double w, double h) MapRect(RectData rect, int viewportWidthPx)
    {
        return MapRectToSlide(rect, viewportWidthPx, (int)(viewportWidthPx * 9.0 / 16.0));
    }

    /// <summary>
    /// Maps HTML pixel coordinates into the fixed slide canvas (960×540 pt),
    /// stretching each slide region to fill the full slide height.
    /// </summary>
    public static (double x, double y, double w, double h) MapRectToSlide(
        RectData rect,
        int viewportWidthPx,
        int slideHeightPx)
    {
        var scaleX = SlideWidthPoints / viewportWidthPx;
        var scaleY = SlideHeightPoints / Math.Max(1, slideHeightPx);
        return (
            rect.X * scaleX,
            rect.Y * scaleY,
            rect.Width * scaleX,
            rect.Height * scaleY
        );
    }

    public static (int x, int y, int w, int h) FullSlideBoundsPoints()
    {
        return (
            0,
            0,
            (int)Math.Round(SlideWidthPoints),
            (int)Math.Round(SlideHeightPoints)
        );
    }
}
