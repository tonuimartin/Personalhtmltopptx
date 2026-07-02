using HtmlToSlidesPro.Models;
using PuppeteerSharp;

namespace HtmlToSlidesPro.Services;

public sealed class ConversionPipeline
{
    private readonly PuppeteerRenderService _renderer = new();
    private readonly CssParser _css = new();
    private readonly ImageResolver _images;
    private readonly ElementRasterizer _rasterizer = new();
    private readonly PptxGeneratorService _pptx;
    private readonly SlidePlanRendererService _planRenderer = new();
    private readonly LibreOfficeQaService _qa = new();

    public ConversionPipeline()
    {
        _images = new ImageResolver(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
        _pptx = new PptxGeneratorService(_css, _images, _rasterizer);
    }

    public async Task<ConversionResult> ConvertAsync(
        string htmlContent,
        string? htmlFilePath,
        ConversionOptions options,
        IProgress<ConversionDiagnostic>? progress = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        var result = new ConversionResult();
        IPage? page = null;

        try
        {
            var (loadedPage, tempPath) = await _renderer.LoadHtmlAsync(htmlContent, htmlFilePath, options, ct);
            page = loadedPage;
            var baseDir = htmlFilePath is not null ? Path.GetDirectoryName(Path.GetFullPath(htmlFilePath)) : Path.GetDirectoryName(tempPath);

            await LocalFontInjector.InjectBundledFontsAsync(page);
            await _renderer.WaitForFontsAsync(page);
            await _renderer.ApplyPreviewLayoutAsync(page, options);

            var puppeteerHeight = await _renderer.GetTotalHeightAsync(page);
            SlideSplitHelper.AlignPreviewSplitsToTarget(options, puppeteerHeight);

            var qaDir = Path.Combine(Path.GetTempPath(), "HtmlToSlidesPro", "qa", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(qaDir);

            if (options.Mode == ConversionMode.LlmSemantic)
            {
                await RunLlmSemanticAsync(page, options, baseDir, qaDir, result, progress, status, ct);
            }
            else
            {
                await RunStandardAsync(page, options, baseDir, qaDir, result, progress, ct);
            }

            await RunQaAsync(result, options, ct);
        }
        finally
        {
            if (page is not null)
                await page.CloseAsync();
        }

        return result;
    }

    private async Task RunLlmSemanticAsync(
        IPage page,
        ConversionOptions options,
        string? baseDir,
        string qaDir,
        ConversionResult result,
        IProgress<ConversionDiagnostic>? progress,
        IProgress<string>? status,
        CancellationToken ct)
    {
        status?.Report("Extracting semantic structure…");
        progress?.Report(new ConversionDiagnostic
        {
            ElementPath = "(llm)",
            Tag = "extract",
            Notes = "Building semantic document tree"
        });

        var semantic = await _renderer.ExtractSemanticAsync(page, options);
        result.Extraction = new DomExtractionResult
        {
            SlideCount = semantic.EstimatedSlideCount,
            ViewportWidth = options.ViewportWidth,
            ViewportHeight = options.ViewportHeight,
            SlideHeight = options.SlideHeightPx
        };

        status?.Report(options.SkipLlmPlanning
            ? "Building instant layout (LLM skipped)…"
            : $"Waiting for Ollama ({options.OllamaModel}) — first run can take several minutes on CPU…");
        progress?.Report(new ConversionDiagnostic
        {
            ElementPath = "(llm)",
            Tag = "ollama",
            Notes = $"Planning with {options.OllamaModel} at {options.OllamaEndpoint}"
        });

        var ollama = OllamaClient.Create(options.OllamaEndpoint);
        var planner = new LlmSlidePlannerService(ollama);
        var (plan, usedFallback, fallbackReason) = await planner.CreatePlanAsync(semantic, options, ct);

        if (usedFallback && fallbackReason is not null)
            result.FontWarnings.Add(fallbackReason);

        var (outputPath, diagnostics) = await _planRenderer.GenerateAsync(plan, options, progress, ct);
        result.OutputPath = outputPath;
        result.Diagnostics = diagnostics;
        result.FontWarnings.Add(usedFallback
            ? $"Layout: deterministic fallback ({plan.Slides.Count} slides)."
            : $"Layout: planned by {options.OllamaModel} ({plan.Slides.Count} slides).");
        result.FontWarnings.AddRange(FontMapper.CollectWarnings(semantic.FontFamilies));

        var totalHeight = await _renderer.GetTotalHeightAsync(page);
        var qaSlides = SlideSplitHelper.BuildSlideRegions(totalHeight, options);
        await CaptureQaScreenshotsAsync(page, qaSlides, options, qaDir, result, ct);
    }

    private async Task RunStandardAsync(
        IPage page,
        ConversionOptions options,
        string? baseDir,
        string qaDir,
        ConversionResult result,
        IProgress<ConversionDiagnostic>? progress,
        CancellationToken ct)
    {
        var puppeteerHeight = await _renderer.GetTotalHeightAsync(page);

        if (options.HighFidelityMode)
        {
            var slides = SlideSplitHelper.BuildSlideRegions(puppeteerHeight, options);
            result.Extraction = new DomExtractionResult
            {
                TotalHeight = puppeteerHeight,
                SlideCount = slides.Count,
                ViewportWidth = options.ViewportWidth,
                ViewportHeight = options.ViewportHeight,
                SlideHeight = options.SlideHeightPx,
                Slides = slides
            };
        }
        else
        {
            result.Extraction = await _renderer.ExtractDomAsync(page, options);
        }

        var slideScreenshots = new List<byte[]>();
        for (var i = 0; i < result.Extraction.Slides.Count; i++)
        {
            var slide = result.Extraction.Slides[i];
            var png = await _renderer.ScreenshotRegionAsync(page, slide.SlideTop, slide.SlideHeight, ct);
            slideScreenshots.Add(png);
            var puppeteerPath = Path.Combine(qaDir, $"slide_{i + 1}_puppeteer.png");
            await File.WriteAllBytesAsync(puppeteerPath, png, ct);
            result.QaComparisons.Add(new QaComparisonItem { SlideIndex = i, PuppeteerImagePath = puppeteerPath });
        }

        var (outputPath, diagnostics) = await _pptx.GenerateAsync(
            result.Extraction, page, options, baseDir, slideScreenshots, progress, ct);
        result.OutputPath = outputPath;
        result.Diagnostics = diagnostics;

        if (options.Mode == ConversionMode.Editable)
        {
            result.FontWarnings.AddRange(FontMapper.CollectWarnings(
                result.Extraction.Slides.SelectMany(s => s.Elements).Select(e => e.Style.FontFamily)));
        }
        else
        {
            result.FontWarnings.Add("High-fidelity mode: slides are embedded as images (pixel-accurate, not editable as shapes).");
        }
    }

    private async Task CaptureQaScreenshotsAsync(
        IPage page,
        IReadOnlyList<SlideDomData> slides,
        ConversionOptions options,
        string qaDir,
        ConversionResult result,
        CancellationToken ct)
    {
        for (var i = 0; i < slides.Count; i++)
        {
            var slide = slides[i];
            var png = await _renderer.ScreenshotRegionAsync(page, slide.SlideTop, slide.SlideHeight, ct);
            var puppeteerPath = Path.Combine(qaDir, $"slide_{i + 1}_puppeteer.png");
            await File.WriteAllBytesAsync(puppeteerPath, png, ct);
            result.QaComparisons.Add(new QaComparisonItem { SlideIndex = i, PuppeteerImagePath = puppeteerPath });
        }
    }

    private async Task RunQaAsync(ConversionResult result, ConversionOptions options, CancellationToken ct)
    {
        var soffice = _qa.ResolveSofficePath(options.LibreOfficePath);
        if (soffice is not null)
        {
            var loImages = await _qa.ConvertPptxToPngAsync(result.OutputPath, soffice, ct);
            for (var i = 0; i < result.QaComparisons.Count && i < loImages.Count; i++)
                result.QaComparisons[i].LibreOfficeImagePath = loImages[i];
        }
        else
        {
            result.FontWarnings.Add("LibreOffice (soffice) not found; QA visual diff skipped.");
        }
    }

    public async ValueTask DisposeAsync() => await _renderer.DisposeAsync();

    public async Task<FullPagePreviewResult> CaptureFullPagePreviewAsync(
        string htmlContent,
        string? htmlFilePath,
        ConversionOptions options,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        IPage? page = null;
        try
        {
            status?.Report("Capturing Puppeteer page for QA split preview…");
            var (loadedPage, _) = await _renderer.LoadHtmlAsync(htmlContent, htmlFilePath, options, ct);
            page = loadedPage;

            await LocalFontInjector.InjectBundledFontsAsync(page);
            await _renderer.WaitForFontsAsync(page);
            await _renderer.ApplyPreviewLayoutAsync(page, options);

            var totalHeight = await _renderer.GetTotalHeightAsync(page);
            var png = await _renderer.ScreenshotFullPageAsync(page, ct);

            var qaDir = Path.Combine(Path.GetTempPath(), "HtmlToSlidesPro", "full-page-preview", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(qaDir);
            var imagePath = Path.Combine(qaDir, "full_page.png");
            await File.WriteAllBytesAsync(imagePath, png, ct);

            return new FullPagePreviewResult
            {
                ImagePath = imagePath,
                TotalHeight = totalHeight,
                ViewportWidth = options.ViewportWidth,
                DefaultSplitYs = SlideSplitHelper.ComputeDefaultSplits(totalHeight, options.SlideHeightPx)
            };
        }
        finally
        {
            if (page is not null)
                await page.CloseAsync();
        }
    }

    public async Task<IReadOnlyList<QaComparisonItem>> PreviewAppliedSplitsAsync(
        string htmlContent,
        string? htmlFilePath,
        ConversionOptions options,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        IPage? page = null;
        try
        {
            status?.Report("Rendering applied splits with Puppeteer…");
            var (loadedPage, _) = await _renderer.LoadHtmlAsync(htmlContent, htmlFilePath, options, ct);
            page = loadedPage;

            await LocalFontInjector.InjectBundledFontsAsync(page);
            await _renderer.WaitForFontsAsync(page);
            await _renderer.ApplyPreviewLayoutAsync(page, options);

            var puppeteerHeight = await _renderer.GetTotalHeightAsync(page);
            SlideSplitHelper.AlignPreviewSplitsToTarget(options, puppeteerHeight);

            var slides = SlideSplitHelper.BuildSlideRegions(puppeteerHeight, options);
            var qaDir = Path.Combine(Path.GetTempPath(), "HtmlToSlidesPro", "split-preview", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(qaDir);

            var comparisons = new List<QaComparisonItem>();
            for (var i = 0; i < slides.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var slide = slides[i];
                var png = await _renderer.ScreenshotRegionAsync(page, slide.SlideTop, slide.SlideHeight, ct);
                var puppeteerPath = Path.Combine(qaDir, $"slide_{i + 1}_puppeteer.png");
                await File.WriteAllBytesAsync(puppeteerPath, png, ct);

                var bottom = slide.SlideTop + slide.SlideHeight;
                comparisons.Add(new QaComparisonItem
                {
                    SlideIndex = i,
                    PuppeteerImagePath = puppeteerPath,
                    ClipTop = slide.SlideTop,
                    ClipHeight = slide.SlideHeight,
                    Caption = $"Applied split — Y {slide.SlideTop}–{bottom}px ({slide.SlideHeight}px)"
                });
            }

            status?.Report($"Applied {comparisons.Count} slide split(s). See QA Visual Diff.");
            return comparisons;
        }
        finally
        {
            if (page is not null)
                await page.CloseAsync();
        }
    }
}
