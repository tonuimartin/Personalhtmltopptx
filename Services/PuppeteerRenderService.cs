using System.Reflection;
using System.Text.Json;
using HtmlToSlidesPro.Models;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace HtmlToSlidesPro.Services;

public sealed class PuppeteerRenderService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IBrowser? _browser;
    private readonly SemaphoreSlim _browserLock = new(1, 1);

    public async Task<IBrowser> GetBrowserAsync(CancellationToken ct = default)
    {
        if (_browser is { IsConnected: true }) return _browser;

        await _browserLock.WaitAsync(ct);
        try
        {
            if (_browser is { IsConnected: true }) return _browser;
            var fetcher = new BrowserFetcher();
            await fetcher.DownloadAsync();
            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-setuid-sandbox", "--font-render-hinting=none"]
            });
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    public async Task<(IPage Page, string TempHtmlPath)> LoadHtmlAsync(
        string htmlContent,
        string? htmlFilePath,
        ConversionOptions options,
        CancellationToken ct = default)
    {
        var browser = await GetBrowserAsync(ct);
        var page = await browser.NewPageAsync();
        await page.SetViewportAsync(new ViewPortOptions
        {
            Width = options.ViewportWidth,
            Height = options.ViewportHeight
        });

        if (!string.IsNullOrWhiteSpace(htmlFilePath) && File.Exists(htmlFilePath))
        {
            var uri = new Uri(Path.GetFullPath(htmlFilePath));
            await page.GoToAsync(uri.AbsoluteUri, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Networkidle0],
                Timeout = 120_000
            });
            return (page, htmlFilePath);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "HtmlToSlidesPro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, "input.html");
        await File.WriteAllTextAsync(tempPath, htmlContent, ct);
        var fileUri = new Uri(tempPath);
        await page.GoToAsync(fileUri.AbsoluteUri, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.Networkidle0],
            Timeout = 120_000
        });
        return (page, tempPath);
    }

    public async Task WaitForFontsAsync(IPage page)
    {
        await page.EvaluateExpressionAsync("document.fonts.ready");
    }

    public async Task<int> GetTotalHeightAsync(IPage page)
    {
        return await page.EvaluateExpressionAsync<int>(
            "Math.max(document.body.scrollHeight, document.documentElement.scrollHeight)");
    }

    public async Task<byte[]> ScreenshotFullPageAsync(IPage page, CancellationToken ct = default)
    {
        var width = page.Viewport.Width;
        var height = Math.Max(1, await GetTotalHeightAsync(page));
        return await page.ScreenshotDataAsync(new ScreenshotOptions
        {
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = 0,
                Y = 0,
                Width = width,
                Height = height
            },
            OmitBackground = false
        });
    }

    public async Task ApplyPreviewLayoutAsync(IPage page, ConversionOptions options)
    {
        var width = options.ViewportWidth;
        await page.EvaluateExpressionAsync($@"
            (() => {{
              const w = {width};
              document.documentElement.style.width = w + 'px';
              document.documentElement.style.maxWidth = w + 'px';
              document.documentElement.style.margin = '0';
              document.documentElement.style.boxSizing = 'border-box';
              document.body.style.width = w + 'px';
              document.body.style.maxWidth = w + 'px';
              document.body.style.margin = '0';
              document.body.style.boxSizing = 'border-box';
            }})()");
    }

    public async Task<DomExtractionResult> ExtractDomAsync(IPage page, ConversionOptions options)
    {
        var script = LoadDomExtractorScript();
        await page.EvaluateExpressionAsync($"window.__extractDomForSlides = {script}");
        var splitsJson = options.UsePreviewSlideSplits
            ? JsonSerializer.Serialize(options.SlideSplitYs ?? [])
            : "null";
        var json = await page.EvaluateExpressionAsync<string>(
            $"JSON.stringify(window.__extractDomForSlides({options.ViewportWidth}, {options.ViewportHeight}, {options.SlideHeightPx}, {splitsJson}))");
        return JsonSerializer.Deserialize<DomExtractionResult>(json, JsonOptions)
               ?? throw new InvalidOperationException("DOM extraction returned null.");
    }

    public async Task<byte[]> ScreenshotRegionAsync(IPage page, int clipY, int clipHeight, CancellationToken ct = default)
    {
        return await page.ScreenshotDataAsync(new ScreenshotOptions
        {
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = 0,
                Y = clipY,
                Width = page.Viewport.Width,
                Height = Math.Max(1, clipHeight)
            },
            OmitBackground = false
        });
    }

    public async Task<SemanticDocument> ExtractSemanticAsync(IPage page, ConversionOptions options)
    {
        var script = LoadEmbeddedScript("SemanticExtractor.js");
        await page.EvaluateExpressionAsync($"window.__extractSemantic = {script}");
        var json = await page.EvaluateExpressionAsync<string>(
            $"JSON.stringify(window.__extractSemantic({options.ViewportWidth}, {options.ViewportHeight}, {options.SlideHeightPx}))");
        return JsonSerializer.Deserialize<SemanticDocument>(json, JsonOptions)
               ?? throw new InvalidOperationException("Semantic extraction returned null.");
    }

    public static string LoadDomExtractorScript() => LoadEmbeddedScript("DomExtractor.js");

    private static string LoadEmbeddedScript(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)
                         ?? throw new InvalidOperationException($"{fileName} embedded resource not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser.Dispose();
        }
        _browserLock.Dispose();
    }
}
