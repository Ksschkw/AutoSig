using AutoSig.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Solnet.Rpc;

namespace AutoSig.Infrastructure.Solana;

/// <summary>Registers the Solana infrastructure into the DI container.</summary>
public static class SolanaServiceRegistration
{
    public static IServiceCollection AddSolanaServices(this IServiceCollection services, string base58PrivateKey)
    {
        // Register the RPC client pointing to Devnet
        services.AddSingleton<IRpcClient>(_ => ClientFactory.GetClient(Cluster.DevNet));

        // Register the Signer Enclave as a singleton  one keypair, one instance
        services.AddSingleton<ISolanaService>(sp =>
        {
            var rpc    = sp.GetRequiredService<IRpcClient>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SolanaSignerEnclave>>();
            return new SolanaSignerEnclave(base58PrivateKey, rpc, logger);
        });

        // HttpClient for Binance price oracle  shared singleton for connection reuse
        services.AddSingleton<HttpClient>(_ => new HttpClient
        {
            DefaultRequestHeaders = { { "User-Agent", "AutoSig/1.0 (Hackathon)" } }
        });

        // Register the Market Data Service  provides real on-chain + price data to the Scout
        services.AddSingleton<IMarketDataService>(sp =>
        {
            var rpc    = sp.GetRequiredService<IRpcClient>();
            var solana = sp.GetRequiredService<ISolanaService>();
            var http   = sp.GetRequiredService<HttpClient>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MarketDataService>>();
            return new MarketDataService(rpc, solana, http, logger);
        });

        return services;
    }
}
