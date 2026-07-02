using System.Net.Http;
using System.Text.RegularExpressions;

namespace HtmlToSlidesPro.Services;

public sealed class ImageResolver(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<byte[]?> ResolveAsync(string src, string? baseDirectory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        if (_cache.TryGetValue(src, out var cached)) return cached;

        byte[]? bytes = null;
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            bytes = ParseDataUri(src);
        else if (Uri.TryCreate(src, UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            bytes = await DownloadAsync(absolute, ct);
        else if (baseDirectory is not null)
        {
            var localPath = Path.IsPathRooted(src) ? src : Path.Combine(baseDirectory, src.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
                bytes = await File.ReadAllBytesAsync(localPath, ct);
        }

        if (bytes is not null)
            _cache[src] = bytes;
        return bytes;
    }

    private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            return await _httpClient.GetByteArrayAsync(uri, ct);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ParseDataUri(string dataUri)
    {
        var match = Regex.Match(dataUri, @"^data:(?<mime>[^;]+);base64,(?<data>.+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        try
        {
            return Convert.FromBase64String(match.Groups["data"].Value);
        }
        catch
        {
            return null;
        }
    }
}
