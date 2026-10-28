using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MelamuHoldings.Application.Trading;
using Microsoft.Extensions.Options;

namespace MelamuHoldings.Infrastructure.Ai;

// Calls an OpenAI-compatible chat completions endpoint ({BaseUrl}/chat/completions): a hosted provider or a local
// server. One system and one user message, no tools. Prompts and replies are never logged; errors carry only the status.
public class OpenAiCompatibleProvider : IAiReviewProvider
{
    private readonly HttpClient _http;
    private readonly AiReviewOptions _options;

    public OpenAiCompatibleProvider(HttpClient http, IOptions<AiReviewOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 300));
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl!.TrimEnd('/')}/chat/completions")
        {
            // Buffered with a Content-Length rather than chunked: some small OpenAI-compatible servers only read the former
            Content = new StringContent(JsonSerializer.Serialize(new ChatRequest(
                _options.Model!,
                [new ChatMessage("system", systemPrompt), new ChatMessage("user", userPrompt)],
                Temperature: 0.2,
                MaxTokens: 1200,
                ResponseFormat: _options.JsonMode ? new ResponseFormat("json_object") : null)), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamException($"The AI provider did not answer within {_http.Timeout.TotalSeconds:0} seconds. Nothing was saved.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpstreamException("The AI provider could not be reached. Nothing was saved.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new UpstreamException($"The AI provider refused the request (HTTP {(int)response.StatusCode}). Nothing was saved.");
            try
            {
                var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
                return body?.Choices?.FirstOrDefault()?.Message?.Content
                    ?? throw new UpstreamException("The AI provider returned no answer. Nothing was saved.");
            }
            catch (JsonException ex)
            {
                throw new UpstreamException("The AI provider's reply was not in the expected format. Nothing was saved.", ex);
            }
        }
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResponseFormat? ResponseFormat);

    private sealed record ChatMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public Message? Message { get; set; }
    }

    private sealed class Message
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
