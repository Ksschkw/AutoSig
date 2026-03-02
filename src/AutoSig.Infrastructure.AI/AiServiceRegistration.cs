using AutoSig.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AutoSig.Infrastructure.AI;

/// <summary>Registers AI Infrastructure services into the DI container.</summary>
public static class AiServiceRegistration
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, string openRouterApiKey, string model)
    {
        services.AddHttpClient<ILlmProvider, OpenRouterLlmProvider>(client =>
        {
            client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {openRouterApiKey}");
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com//AutoSig");
            client.DefaultRequestHeaders.Add("X-Title", "AutoSig Agentic Wallet");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddTypedClient<ILlmProvider>((client, sp) =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenRouterLlmProvider>>();
            return new OpenRouterLlmProvider(client, logger, model);
        });

        return services;
    }
}
