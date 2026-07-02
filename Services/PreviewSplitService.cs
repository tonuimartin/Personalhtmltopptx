using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HtmlToSlidesPro.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HtmlToSlidesPro.Services;

public static class PreviewSplitService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void Attach(WebView2 webView, Action<IReadOnlyList<int>, int> onSplitsChanged)
    {
        if (webView.CoreWebView2 is null) return;

        webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        webView.CoreWebView2.WebMessageReceived += (_, args) =>
        {
            try
            {
                var message = ParseWebMessage<SplitMessage>(args);
                if (message?.Type == "splits" && message.Ys is not null)
                    onSplitsChanged(message.Ys, message.TotalHeight ?? 0);
            }
            catch
            {
                // Ignore malformed preview messages.
            }
        };
    }

    public static async Task InstallOverlayAsync(
        WebView2 webView,
        IReadOnlyList<int>? splitYs = null,
        int viewportWidth = ConversionOptions.DefaultViewportWidth,
        int slideHeight = ConversionOptions.DefaultViewportWidth * 9 / 16)
    {
        if (webView.CoreWebView2 is null) return;

        var script = LoadOverlayScript();
        await webView.CoreWebView2.ExecuteScriptAsync(script);

        var splitsJson = splitYs is { Count: > 0 }
            ? JsonSerializer.Serialize(splitYs)
            : "null";

        await webView.CoreWebView2.ExecuteScriptAsync(
            $"window.__htsInstallSplitOverlay({{ viewportWidth: {viewportWidth}, slideHeight: {slideHeight}, splitYs: {splitsJson} }})");
    }

    public static async Task<IReadOnlyList<int>> GetSplitsAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 is null) return [];

        var raw = await webView.CoreWebView2.ExecuteScriptAsync(
            "window.__htsGetSplits ? window.__htsGetSplits() : []");

        return ParseScriptJsonArray(raw);
    }

    public static async Task<int> GetTotalHeightAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 is null) return 0;

        var raw = await webView.CoreWebView2.ExecuteScriptAsync(
            "window.__htsGetTotalHeight ? window.__htsGetTotalHeight() : Math.max(document.body.scrollHeight, document.documentElement.scrollHeight, 1)");

        if (string.IsNullOrWhiteSpace(raw) || raw == "null")
            return 0;

        if (int.TryParse(raw.Trim('"'), out var direct))
            return direct;

        if (double.TryParse(raw, out var numeric))
            return (int)Math.Round(numeric);

        return 0;
    }

    public static async Task ResetSplitsAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 is null) return;
        await webView.CoreWebView2.ExecuteScriptAsync("window.__htsResetSplits && window.__htsResetSplits()");
    }

    public static async Task RefreshScaleAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 is null) return;
        await webView.CoreWebView2.ExecuteScriptAsync(
            "if (window.__htsRefreshScale) window.__htsRefreshScale();");
    }

    private static List<int> ParseScriptJsonArray(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null")
            return [];

        try
        {
            var direct = JsonSerializer.Deserialize<List<int>>(raw, JsonOptions);
            if (direct is not null)
                return direct;
        }
        catch
        {
            // Fall through for double-encoded JSON strings.
        }

        try
        {
            var inner = JsonSerializer.Deserialize<string>(raw);
            if (inner is not null)
                return JsonSerializer.Deserialize<List<int>>(inner, JsonOptions) ?? [];
        }
        catch
        {
            // Ignore parse errors.
        }

        return [];
    }

    private static T? ParseWebMessage<T>(CoreWebView2WebMessageReceivedEventArgs args)
    {
        var json = args.WebMessageAsJson;
        if (string.IsNullOrWhiteSpace(json))
            return default;

        if (json.StartsWith('"'))
        {
            var inner = JsonSerializer.Deserialize<string>(json);
            if (inner is not null)
                return JsonSerializer.Deserialize<T>(inner, JsonOptions);
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static string LoadOverlayScript()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("PreviewSplitOverlay.js", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)
                         ?? throw new InvalidOperationException("PreviewSplitOverlay.js embedded resource not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class SplitMessage
    {
        public string? Type { get; set; }

        [JsonPropertyName("ys")]
        public List<int>? Ys { get; set; }

        [JsonPropertyName("totalHeight")]
        public int? TotalHeight { get; set; }
    }
}
