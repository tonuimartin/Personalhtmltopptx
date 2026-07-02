using PuppeteerSharp;

namespace HtmlToSlidesPro.Services;

public static class LocalFontInjector
{
    public static string BundledFontsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "tools", "fonts");

    public static async Task InjectBundledFontsAsync(IPage page)
    {
        if (!Directory.Exists(BundledFontsDirectory)) return;

        var faces = new System.Text.StringBuilder();
        foreach (var ttf in Directory.GetFiles(BundledFontsDirectory, "*.ttf"))
        {
            var family = Path.GetFileNameWithoutExtension(ttf);
            var uri = new Uri(Path.GetFullPath(ttf)).AbsoluteUri;
            faces.AppendLine(
                "@font-face { font-family: '" + family + "'; src: url('" + uri + "') format('truetype'); font-weight: 100 900; font-style: normal; }");
        }

        if (faces.Length == 0) return;

        var css = faces.ToString();
        await page.EvaluateFunctionAsync(
            """(css) => { const s = document.createElement('style'); s.textContent = css; document.head.appendChild(s); }""",
            css);
        await page.EvaluateExpressionAsync("document.fonts.ready");
    }

    public static IEnumerable<string> ListBundledFamilies()
    {
        if (!Directory.Exists(BundledFontsDirectory)) yield break;
        foreach (var ttf in Directory.GetFiles(BundledFontsDirectory, "*.ttf"))
            yield return Path.GetFileNameWithoutExtension(ttf);
    }
}
