using AutoSig.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoSig.Infrastructure.AI;

// ── OpenRouter API contracts ────────────────────────────────────────────────

internal sealed record OpenRouterMessage(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content
);

internal sealed record OpenRouterRequest(
    [property: JsonPropertyName("model")]    string Model,
    [property: JsonPropertyName("messages")] List<OpenRouterMessage> Messages
);

internal sealed record OpenRouterChoice(
    [property: JsonPropertyName("message")] OpenRouterMessage Message
);

internal sealed record OpenRouterResponse(
    [property: JsonPropertyName("choices")] List<OpenRouterChoice> Choices
);

// ── Base LLM provider ───────────────────────────────────────────────────────

/// <summary>
/// Core OpenRouter HTTP client. Shared by per-agent typed subclasses.
/// Supports a fallback model chain: if the primary model returns 429 (rate
/// limited) or 404 (unavailable), the provider automatically rotates to the
/// next model in the list and retries — no cycle is wasted.
/// Polly self-correction retries on JSON parse failures.
/// </summary>
public class OpenRouterLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenRouterLlmProvider> _logger;

    // First entry is the primary model; the rest are fallbacks rotated on 429/404.
    private readonly string[] _modelChain;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public OpenRouterLlmProvider(HttpClient http, ILogger<OpenRouterLlmProvider> logger, string primaryModel,
        string[]? fallbackModels = null)
    {
        _http        = http;
        _logger      = logger;
        _modelChain  = [primaryModel, .. (fallbackModels ?? [])];
        _logger.LogInformation("[LLM] Provider ready -- primary: {Model} | {N} fallback(s) configured.",
            primaryModel, _modelChain.Length - 1);
    }

    // ── Raw HTTP call to one specific model ───────────────────────────────

    private async Task<string> CallModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct)
    {
        _logger.LogInformation("[LLM] Calling OpenRouter [{Model}] (system={SLen} chars, user={ULen} chars)...",
            model, systemPrompt.Length, userMessage.Length);

        var request = new OpenRouterRequest(model,
        [
            new OpenRouterMessage("system", systemPrompt),
            new OpenRouterMessage("user",   userMessage)
        ]);

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("chat/completions", request, ct);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError("[LLM] Request to OpenRouter TIMED OUT after {T}s.", _http.Timeout.TotalSeconds);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body   = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            _logger.LogError("[LLM] OpenRouter [{Model}] returned HTTP {Code}. Body: {Body}",
                model, status, body[..Math.Min(200, body.Length)]);
            response.EnsureSuccessStatusCode(); // throws HttpRequestException
        }

        _logger.LogInformation("[LLM] HTTP 200 from [{Model}].", model);

        var result = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(JsonOpts, ct)
                     ?? throw new InvalidOperationException("OpenRouter returned null response body.");

        var text = result.Choices.FirstOrDefault()?.Message.Content
                   ?? throw new InvalidOperationException("OpenRouter response had no choices.");

        _logger.LogInformation("[LLM] Response: {Len} chars.", text.Length);
        return text.Trim();
    }

    // ── With model-chain rotation ─────────────────────────────────────────

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        for (var i = 0; i < _modelChain.Length; i++)
        {
            var model = _modelChain[i];
            try
            {
                return await CallModelAsync(model, systemPrompt, userMessage, ct);
            }
            catch (HttpRequestException httpEx)
                when ((int)(httpEx.StatusCode ?? 0) is 429 or 404 or 401 or 503)
            {
                var code = (int)(httpEx.StatusCode ?? 0);
                if (i < _modelChain.Length - 1)
                {
                    _logger.LogWarning("[LLM] [{Model}] returned {Code} -- rotating to fallback [{Next}]...",
                        model, code, _modelChain[i + 1]);
                    await Task.Delay(code == 429 ? 3000 : 500, ct); // brief back-off on rate limit
                }
                else
                {
                    _logger.LogError("[LLM] All {N} model(s) in chain exhausted (last error: HTTP {Code}). No LLM available.",
                        _modelChain.Length, code);
                    throw;
                }
            }
        }
        throw new InvalidOperationException("Model chain exhausted.");
    }

    // ── Typed JSON completion with self-correction retries ───────────────

    public async Task<T> CompleteTypedAsync<T>(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        _logger.LogInformation("[LLM] Starting typed completion for {Type}...", typeof(T).Name);

        var currentMsg = userMessage;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            _logger.LogInformation("[LLM] Attempt {A}/3...", attempt);
            try
            {
                var raw  = await CompleteAsync(systemPrompt, currentMsg, ct);
                var json = raw
                    .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("```", "")
                    .Trim();

                var result = JsonSerializer.Deserialize<T>(json, JsonOpts)
                    ?? throw new JsonException("Deserialized result was null.");

                _logger.LogInformation("[LLM] Typed completion done ({Type}, attempt {A}).", typeof(T).Name, attempt);
                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("[LLM] Attempt {A} -- JSON error: {E}. Self-correcting...", attempt, ex.Message);
                currentMsg = userMessage +
                    $"\n\n[SYSTEM: Your last response was not valid JSON. Error: {ex.Message}. " +
                    "Reply ONLY with a single valid JSON object. No markdown fences, no text.]";
            }
            // Let HttpRequestException bubble up — these are LLM availability errors,
            // NOT parse errors. The agent (Strategist/RiskManager) handles them.
        }

        throw new InvalidOperationException($"All {typeof(T).Name} completion attempts failed after self-correction retries.");
    }
}

// ── Per-agent typed subclasses ───────────────────────────────────────────────

/// <summary>Strategist LLM — reasoning-heavy model with fallback chain.</summary>
public sealed class StrategistLlmProvider : OpenRouterLlmProvider, IStrategistLlmProvider
{
    public StrategistLlmProvider(HttpClient http, ILogger<OpenRouterLlmProvider> logger,
        string primaryModel, string[]? fallbackModels = null)
        : base(http, logger, primaryModel, fallbackModels) { }
}

/// <summary>Risk Manager LLM — fast evaluation model with fallback chain.</summary>
public sealed class RiskManagerLlmProvider : OpenRouterLlmProvider, IRiskManagerLlmProvider
{
    public RiskManagerLlmProvider(HttpClient http, ILogger<OpenRouterLlmProvider> logger,
        string primaryModel, string[]? fallbackModels = null)
        : base(http, logger, primaryModel, fallbackModels) { }
}
