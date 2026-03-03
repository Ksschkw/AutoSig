using AutoSig.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoSig.Infrastructure.AI;

/// <summary>
/// Registers AI infrastructure services.
/// Two separate OpenRouter clients — one per agent — each with their own
/// primary model and a fallback chain that auto-rotates on 429 / 404 / 503.
/// </summary>
public static class AiServiceRegistration
{
    public static IServiceCollection AddAiServices(
        this IServiceCollection services,
        string openRouterApiKey,
        string strategistModel,
        string riskModel)
    {
        // Fallback chains: primary model first, then alternatives tried on 429/404/503.
        // These span different upstream providers so at least one should be available.
        var strategistFallbacks = new[]
        {
            "nvidia/nemotron-3-nano-30b-a3b:free",
            "openai/gpt-oss-120b:free",
            "openrouter/auto", 
        };

        var riskFallbacks = new[]
        {
            "nvidia/nemotron-3-nano-30b-a3b:free",
            "openai/gpt-oss-120b:free",
            "openrouter/auto",
        };

        // ── Strategist LLM ────────────────────────────────────────────────────
        services.AddHttpClient<IStrategistLlmProvider, StrategistLlmProvider>(client =>
            ConfigureOpenRouterClient(client, openRouterApiKey))
        .AddTypedClient<IStrategistLlmProvider>((client, sp) =>
        {
            var logger = sp.GetRequiredService<ILogger<OpenRouterLlmProvider>>();
            return new StrategistLlmProvider(client, logger, strategistModel, strategistFallbacks);
        });

        // ── Risk Manager LLM ──────────────────────────────────────────────────
        services.AddHttpClient<IRiskManagerLlmProvider, RiskManagerLlmProvider>(client =>
            ConfigureOpenRouterClient(client, openRouterApiKey))
        .AddTypedClient<IRiskManagerLlmProvider>((client, sp) =>
        {
            var logger = sp.GetRequiredService<ILogger<OpenRouterLlmProvider>>();
            return new RiskManagerLlmProvider(client, logger, riskModel, riskFallbacks);
        });

        return services;
    }

    private static void ConfigureOpenRouterClient(HttpClient client, string apiKey)
    {
        client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/Ksschkw/AutoSig");
        client.DefaultRequestHeaders.Add("X-Title", "AutoSig Agentic Wallet");
        client.Timeout = TimeSpan.FromSeconds(60);
    }
}
