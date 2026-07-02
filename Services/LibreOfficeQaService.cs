using System.Diagnostics;

namespace HtmlToSlidesPro.Services;

public sealed class LibreOfficeQaService
{
    public string? ResolveSofficePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "LibreOffice", "program", "soffice.exe");
        if (File.Exists(bundled)) return bundled;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "soffice.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public async Task<List<string>> ConvertPptxToPngAsync(string pptxPath, string? sofficePath, CancellationToken ct = default)
    {
        var results = new List<string>();
        if (sofficePath is null || !File.Exists(sofficePath))
            return results;

        var outDir = Path.Combine(Path.GetTempPath(), "HtmlToSlidesPro", "qa", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var psi = new ProcessStartInfo
        {
            FileName = sofficePath,
            Arguments = $"--headless --convert-to png --outdir \"{outDir}\" \"{pptxPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start LibreOffice.");
        await proc.WaitForExitAsync(ct);

        results.AddRange(Directory.GetFiles(outDir, "*.png").OrderBy(f => f));
        return results;
    }
}
