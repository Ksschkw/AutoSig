using AutoSig.Application.Agents;
using AutoSig.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AutoSig.Application;

/// <summary>
/// Registers all Application layer services (Agents) into the DI container.
/// Called from AutoSig.Console's startup configuration.
/// </summary>
public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register MediatR and scan this assembly for all INotificationHandler implementations
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceRegistration).Assembly);
        });

        // Register the Scout Agent explicitly (it's not a notification handler — it's triggered by the loop)
        services.AddTransient<ScoutAgent>();

        return services;
    }
}
