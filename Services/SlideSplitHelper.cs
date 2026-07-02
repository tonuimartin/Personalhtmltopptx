using HtmlToSlidesPro.Models;

namespace HtmlToSlidesPro.Services;

public static class SlideSplitHelper
{
    public const int MinSlideHeightPx = 1;

    public static List<int> ComputeDefaultSplits(int totalHeight, int slideHeight)
    {
        var splits = new List<int>();
        if (totalHeight <= slideHeight)
            return splits;

        for (var y = slideHeight; y < totalHeight; y += slideHeight)
            splits.Add(y);

        return splits;
    }

    public static List<int> DeduplicateOrdered(IReadOnlyList<int> splits)
    {
        var result = new List<int>();
        var last = -1;
        foreach (var y in splits.OrderBy(y => y))
        {
            if (y > last)
            {
                result.Add(y);
                last = y;
            }
        }

        return result;
    }

    public static List<int> MapSplitsToTargetHeight(IReadOnlyList<int> previewSplits, int previewHeight, int targetHeight)
    {
        if (previewSplits.Count == 0)
            return [];

        if (previewHeight <= 0 || previewHeight == targetHeight)
            return DeduplicateOrdered(previewSplits);

        var ratio = (double)targetHeight / previewHeight;
        var mapped = previewSplits
            .Select(y => (int)Math.Round(y * ratio, MidpointRounding.AwayFromZero))
            .Select(y => Math.Clamp(y, 1, Math.Max(1, targetHeight - 1)))
            .ToList();

        return DeduplicateOrdered(mapped);
    }

    public static void AlignPreviewSplitsToTarget(ConversionOptions options, int targetHeight)
    {
        if (!options.UsePreviewSlideSplits)
            return;

        var previewHeight = options.PreviewTotalHeightPx > 0
            ? options.PreviewTotalHeightPx
            : targetHeight;

        options.SlideSplitYs = MapSplitsToTargetHeight(options.SlideSplitYs ?? [], previewHeight, targetHeight);
        options.PreviewTotalHeightPx = targetHeight;
    }

    public static List<int> ResolveSplitPoints(ConversionOptions options, int totalHeight)
    {
        if (options.UsePreviewSlideSplits)
            return DeduplicateOrdered(options.SlideSplitYs ?? []);

        return ComputeDefaultSplits(totalHeight, options.SlideHeightPx);
    }

    public static List<SlideDomData> BuildSlideRegions(int totalHeight, ConversionOptions options)
    {
        var splits = ResolveSplitPoints(options, totalHeight);
        var tops = new List<int> { 0 };
        tops.AddRange(splits);

        var slides = new List<SlideDomData>();
        for (var i = 0; i < tops.Count; i++)
        {
            var top = tops[i];
            var bottom = i < tops.Count - 1 ? tops[i + 1] : totalHeight;
            if (bottom <= top)
                continue;

            slides.Add(new SlideDomData
            {
                SlideIndex = slides.Count,
                SlideTop = top,
                SlideHeight = bottom - top,
                ViewportWidth = options.ViewportWidth,
                ViewportHeight = options.ViewportHeight
            });
        }

        if (slides.Count == 0)
        {
            slides.Add(new SlideDomData
            {
                SlideTop = 0,
                SlideHeight = Math.Max(totalHeight, 1),
                ViewportWidth = options.ViewportWidth,
                ViewportHeight = options.ViewportHeight
            });
        }

        return slides;
    }
}
