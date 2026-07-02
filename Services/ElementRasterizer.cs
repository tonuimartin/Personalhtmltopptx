using PuppeteerSharp;

namespace HtmlToSlidesPro.Services;

public sealed class ElementRasterizer
{
    public async Task<byte[]?> RasterizeElementAsync(IPage page, string cssSelector, CancellationToken ct = default)
    {
        try
        {
            var handle = await page.QuerySelectorAsync(cssSelector);
            if (handle is null) return null;

            return await handle.ScreenshotDataAsync(new ElementScreenshotOptions
            {
                Type = ScreenshotType.Png,
                OmitBackground = true
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> RasterizeByPathAsync(IPage page, string domPath, int htsId = -1, CancellationToken ct = default)
    {
        if (htsId >= 0)
        {
            var byId = await RasterizeElementAsync(page, $"[data-hts-id=\"{htsId}\"]", ct);
            if (byId is not null) return byId;
        }

        var selector = DomPathToSelector(domPath);
        if (selector is null) return null;
        return await RasterizeElementAsync(page, selector, ct);
    }

    public static string? DomPathToSelector(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var segments = path.Split(" > ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;

        var last = segments[^1];
        if (last.Contains('#'))
        {
            var parts = last.Split('#', 2);
            return $"#{parts[1].Split('.')[0]}";
        }

        if (last.Contains('.'))
        {
            var tag = last.Split('.')[0];
            var cls = last.Split('.')[1];
            return $"{tag}.{cls}";
        }

        return last;
    }
}
