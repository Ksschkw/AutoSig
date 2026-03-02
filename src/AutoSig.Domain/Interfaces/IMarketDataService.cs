using AutoSig.Domain.Models;

namespace AutoSig.Domain.Interfaces;

/// <summary>
/// Contract for gathering real-time market data from the Solana blockchain.
/// Used by the Scout Agent to provide actual on-chain context to the swarm.
/// </summary>
public interface IMarketDataService
{
    /// <summary>
    /// Gathers a complete snapshot of the current Solana network state
    /// and treasury wallet status using RPC calls.
    /// </summary>
    Task<MarketContext> GetMarketContextAsync(CancellationToken ct = default);
}
