using AutoSig.Application.Agents;
using AutoSig.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AutoSig.Application;

/// <summary>
/// Registers all Application layer services (Agents) into the DI container.
/// Called from AutoSig.Console's startup configuration.
/// </summary>
public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        TradingPolicy? policy = null)
    {
        // Register the trading policy  used by RiskManager for all guardrail checks.
        // If no policy is provided, falls back to the safe in-code defaults in TradingPolicy.
        services.AddSingleton(policy ?? new TradingPolicy());

        // MediatR scans this assembly for all INotificationHandler implementations (the agents)
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceRegistration).Assembly);
        });

        // VelocityTracker is a singleton so Scout + RiskManager share the SAME instance.
        // This lets Scout skip the LLM pipeline before hitting the rate limit.
        services.AddSingleton<VelocityTracker>();

        // Scout is triggered externally by the ConsensusLoop  not a notification handler
        services.AddTransient<ScoutAgent>();

        return services;
    }
}
