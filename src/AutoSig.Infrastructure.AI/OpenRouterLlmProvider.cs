using AutoSig.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoSig.Infrastructure.AI;

// ── OpenRouter API request/response contracts ──────────────────────────────────

internal sealed record OpenRouterMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

internal sealed record OpenRouterRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<OpenRouterMessage> Messages
);

internal sealed record OpenRouterChoice(
    [property: JsonPropertyName("message")] OpenRouterMessage Message
);

internal sealed record OpenRouterResponse(
    [property: JsonPropertyName("choices")] List<OpenRouterChoice> Choices
);

// ── LLM Provider implementation ────────────────────────────────────────────────

/// <summary>
/// OpenRouter LLM provider with Polly-powered self-healing retry logic.
/// When JSON deserialization fails, the error is fed back to the LLM
/// so it can correct its own output — up to 3 attempts.
/// </summary>
public sealed class OpenRouterLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouterLlmProvider> _logger;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public OpenRouterLlmProvider(HttpClient httpClient, ILogger<OpenRouterLlmProvider> logger, string model)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = model;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new OpenRouterRequest(_model,
        [
            new OpenRouterMessage("system", systemPrompt),
            new OpenRouterMessage("user", userMessage)
        ]);

        var response = await _httpClient.PostAsJsonAsync("chat/completions", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(JsonOpts, ct)
                     ?? throw new InvalidOperationException("LLM returned an empty response.");

        var text = result.Choices.FirstOrDefault()?.Message.Content
                   ?? throw new InvalidOperationException("LLM response contained no choices.");

        return text.Trim();
    }

    public async Task<T> CompleteTypedAsync<T>(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        // Build a self-correcting Polly retry pipeline (up to 3 attempts).
        // On each JSON parse failure, we append the error to the user message
        // and re-prompt the LLM so it can fix its own output.
        var currentUserMessage = userMessage;

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder().Handle<JsonException>().Handle<InvalidOperationException>()
            })
            .Build();

        T? result = default;
        Exception? lastEx = null;

        await pipeline.ExecuteAsync(async token =>
        {
            try
            {
                var rawText = await CompleteAsync(systemPrompt, currentUserMessage, token);

                // Strip any accidental markdown code fences the model might add
                var json = rawText
                    .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("```", "")
                    .Trim();

                _logger.LogDebug("[LLM] Raw response: {Json}", json);

                result = JsonSerializer.Deserialize<T>(json, JsonOpts)
                    ?? throw new JsonException("Deserialized result was null.");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                lastEx = ex;
                _logger.LogWarning("[LLM] JSON parse error: {Error}. Requesting self-correction from model...", ex.Message);
                // Self-healing: tell the LLM what went wrong so it can fix itself
                currentUserMessage = userMessage +
                    $"\n\n[SYSTEM ERROR - SELF CORRECTION REQUIRED]\nYour previous response failed JSON parsing with this error: {ex.Message}\nPlease respond ONLY with a perfectly valid JSON object matching the requested schema.";
                throw; // Let Polly retry
            }
        }, ct);

        return result ?? throw (lastEx ?? new InvalidOperationException("Failed to get a valid LLM response."));
    }
}
