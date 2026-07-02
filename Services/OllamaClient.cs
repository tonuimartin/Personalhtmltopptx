using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HtmlToSlidesPro.Services;

public sealed class OllamaClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http = httpClient;

    public static OllamaClient Create(string baseUrl)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        return new OllamaClient(client);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<OllamaTagsResponse>("api/tags", JsonOptions, ct);
        return response?.Models?.Select(m => m.Name).Where(n => n is not null).Cast<string>().ToList() ?? [];
    }

    public async Task<string> GenerateJsonAsync(string model, string prompt, int numPredict = 2048, CancellationToken ct = default)
    {
        var body = new OllamaChatRequest
        {
            Model = model,
            Stream = false,
            Format = "json",
            Options = new OllamaRunOptions { NumPredict = numPredict, Temperature = 0.15f },
            Messages =
            [
                new OllamaChatMessage
                {
                    Role = "system",
                    Content = "You are a PowerPoint layout engine. Respond with valid JSON only. No markdown fences. Be concise."
                },
                new OllamaChatMessage { Role = "user", Content = prompt }
            ]
        };

        using var response = await _http.PostAsJsonAsync("api/chat", body, JsonOptions, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama error ({(int)response.StatusCode}): {payload}");

        var chat = JsonSerializer.Deserialize<OllamaChatResponse>(payload, JsonOptions)
                   ?? throw new InvalidOperationException("Empty Ollama response.");
        var content = chat.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Ollama returned no content.");

        return content.Trim();
    }

    private sealed class OllamaTagsResponse
    {
        public List<OllamaModelInfo>? Models { get; set; }
    }

    private sealed class OllamaModelInfo
    {
        public string? Name { get; set; }
    }

    private sealed class OllamaChatRequest
    {
        public string Model { get; set; } = "";
        public bool Stream { get; set; }
        public string Format { get; set; } = "json";
        public OllamaRunOptions? Options { get; set; }
        public List<OllamaChatMessage> Messages { get; set; } = [];
    }

    private sealed class OllamaRunOptions
    {
        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }
    }

    private sealed class OllamaChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    private sealed class OllamaChatResponse
    {
        public OllamaChatMessage? Message { get; set; }
    }
}
