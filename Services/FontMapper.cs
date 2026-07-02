namespace HtmlToSlidesPro.Services;

public static class FontMapper
{
    private static readonly Dictionary<string, string> GoogleFontMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inter"] = "Inter",
        ["jetbrains mono"] = "JetBrains Mono",
        ["roboto"] = "Roboto",
        ["open sans"] = "Open Sans",
        ["lato"] = "Lato",
        ["montserrat"] = "Montserrat",
        ["poppins"] = "Poppins",
        ["source sans pro"] = "Source Sans Pro",
        ["source sans 3"] = "Source Sans 3",
        ["nunito"] = "Nunito",
        ["raleway"] = "Raleway",
        ["ubuntu"] = "Ubuntu",
        ["merriweather"] = "Merriweather",
        ["playfair display"] = "Playfair Display",
        ["oswald"] = "Oswald",
        ["fira sans"] = "Fira Sans",
        ["work sans"] = "Work Sans",
        ["dm sans"] = "DM Sans",
        ["space grotesk"] = "Space Grotesk",
        ["manrope"] = "Manrope",
        ["arial"] = "Arial",
        ["helvetica"] = "Helvetica",
        ["times new roman"] = "Times New Roman",
        ["georgia"] = "Georgia",
        ["courier new"] = "Courier New",
        ["segoe ui"] = "Segoe UI",
        ["calibri"] = "Calibri",
        ["cambria"] = "Cambria",
        ["verdana"] = "Verdana",
        ["tahoma"] = "Tahoma"
    };

    public static string Resolve(string? cssFontFamily)
    {
        if (string.IsNullOrWhiteSpace(cssFontFamily)) return "Calibri";

        var first = cssFontFamily.Split(',')[0].Trim().Trim('"', '\'');
        foreach (var kv in GoogleFontMap)
        {
            if (first.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return first;
    }

    public static bool IsInstalled(string fontName)
    {
        try
        {
            return System.Windows.Media.Fonts.SystemFontFamilies
                .Any(f => f.Source.Equals(fontName, StringComparison.OrdinalIgnoreCase)
                          || f.FamilyNames.Values.Any(n => n.Equals(fontName, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> CollectWarnings(IEnumerable<string?> fontFamilies)
    {
        var bundled = new HashSet<string>(LocalFontInjector.ListBundledFamilies(), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fam in fontFamilies.Select(Resolve))
        {
            if (!seen.Add(fam)) continue;
            if (IsInstalled(fam)) continue;

            if (bundled.Contains(fam))
            {
                yield return $"Font '{fam}' is bundled in tools/fonts and injected for Puppeteer; install it on Windows or enable font sidecar export for PowerPoint text.";
                continue;
            }

            yield return $"Font '{fam}' is not installed. Add {fam}.ttf to tools/fonts for offline rendering, or install the font on this machine.";
        }
    }
}
