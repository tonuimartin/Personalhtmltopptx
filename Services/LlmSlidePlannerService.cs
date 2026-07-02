using System.Text.Json;
using HtmlToSlidesPro.Models;

namespace HtmlToSlidesPro.Services;

public sealed class LlmSlidePlannerService(OllamaClient ollama)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OllamaClient _ollama = ollama;

    public async Task<(SlidePlan Plan, bool UsedFallback, string? FallbackReason)> CreatePlanAsync(
        SemanticDocument semantic,
        ConversionOptions options,
        CancellationToken ct = default)
    {
        if (options.SkipLlmPlanning)
            return (BuildFallbackPlan(semantic), true, "LLM skipped by user — instant deterministic layout.");

        if (!await _ollama.IsAvailableAsync(ct))
        {
            return (BuildFallbackPlan(semantic), true,
                "Ollama not reachable — using deterministic layout. Install from https://ollama.com and run `ollama serve`.");
        }

        var compact = SemanticCompactor.Compact(semantic);
        var semanticJson = JsonSerializer.Serialize(compact, JsonOptions);
        var prompt = BuildPrompt(semanticJson);

        try
        {
            var raw = await _ollama.GenerateJsonAsync(options.OllamaModel, prompt, numPredict: 2048, ct);
            var plan = ParsePlan(raw);
            if (plan.Slides.Count == 0)
                return (Normalize(BuildFallbackPlan(semantic)), true, "LLM returned empty plan — used fallback layout.");

            return (Normalize(plan), false, null);
        }
        catch (JsonException ex)
        {
            return (Normalize(BuildFallbackPlan(semantic)), true, "LLM JSON invalid — used fallback: " + ex.Message);
        }
        catch (Exception ex)
        {
            return (Normalize(BuildFallbackPlan(semantic)), true, "LLM error — used fallback: " + ex.Message);
        }
    }

    internal static string BuildPrompt(string semanticJson) =>
        """
        Convert this HTML semantic document into a PowerPoint slide plan JSON.
        Slide: 13.333 x 7.5 inches. Coordinates in INCHES (x,y,w,h).
        Shape types only: rect, roundedRect, text.
        Preserve text verbatim. Use hex colors from colorTokens.
        fontSize in points. align: left|center|right.
        Output JSON: {"slides":[{"backgroundColor":"#fff","shapes":[{"type":"text","x":0.6,"y":0.6,"w":12,"h":0.7,"text":"...","fontSize":28,"bold":true,"color":"#0f172a"}]}]}

        Semantic document:
        """ + semanticJson;

    private static SlidePlan ParsePlan(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("```"))
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start >= 0 && end > start)
                raw = raw[start..(end + 1)];
        }

        return JsonSerializer.Deserialize<SlidePlan>(raw, JsonOptions) ?? new SlidePlan();
    }

    public static SlidePlan BuildFallbackPlan(SemanticDocument semantic)
    {
        var plan = new SlidePlan();
        foreach (var section in semantic.Sections)
        {
            var slide = new SlidePlanSlide { BackgroundColor = "#ffffff", Notes = "Deterministic fallback layout" };
            var y = 0.6;
            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                slide.Shapes.Add(new SlidePlanShape
                {
                    Type = "text",
                    X = 0.6,
                    Y = y,
                    W = 12,
                    H = 0.7,
                    Text = section.Heading,
                    FontSize = 30,
                    Bold = true,
                    Color = "#0f172a",
                    Align = "left",
                    FontFamily = "Calibri"
                });
                y += 0.9;
            }

            foreach (var p in section.Paragraphs.Take(6))
            {
                slide.Shapes.Add(new SlidePlanShape
                {
                    Type = "text",
                    X = 0.6,
                    Y = y,
                    W = 12,
                    H = 0.55,
                    Text = p,
                    FontSize = 16,
                    Color = "#334155",
                    Align = "left",
                    FontFamily = "Calibri"
                });
                y += 0.65;
                if (y > 6.5) break;
            }

            if (slide.Shapes.Count > 0)
                plan.Slides.Add(slide);
        }

        if (plan.Slides.Count == 0)
        {
            plan.Slides.Add(new SlidePlanSlide
            {
                Shapes =
                [
                    new SlidePlanShape
                    {
                        Type = "text",
                        X = 0.6,
                        Y = 0.6,
                        W = 12,
                        H = 1,
                        Text = semantic.PageTitle.Length > 0 ? semantic.PageTitle : "Converted slide",
                        FontSize = 28,
                        Bold = true,
                        Color = "#0f172a"
                    }
                ]
            });
        }

        return plan;
    }

    private static SlidePlan Normalize(SlidePlan plan)
    {
        NormalizePlan(plan);
        return plan;
    }

    private static void NormalizePlan(SlidePlan plan)
    {
        const double maxW = CoordinateMapper.SlideWidthInches;
        const double maxH = CoordinateMapper.SlideHeightInches;

        foreach (var slide in plan.Slides)
        {
            foreach (var shape in slide.Shapes)
            {
                shape.Type = (shape.Type ?? "text").ToLowerInvariant();
                shape.X = Clamp(shape.X, 0, maxW - 0.1);
                shape.Y = Clamp(shape.Y, 0, maxH - 0.1);
                shape.W = Clamp(shape.W, 0.1, maxW - shape.X);
                shape.H = Clamp(shape.H, 0.1, maxH - shape.Y);
                if (shape.FontSize is < 6 or > 72)
                    shape.FontSize = 16;
            }
        }
    }

    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
}
