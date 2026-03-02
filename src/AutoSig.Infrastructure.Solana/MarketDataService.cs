using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;

namespace AutoSig.Infrastructure.Solana;

/// <summary>
/// Gathers real-time market context from the Solana Devnet using RPC calls.
/// Implements the IMarketDataService interface for the Scout Agent.
/// Uses 'confirmed' commitment for speed as recommended by Solana RPC docs.
/// </summary>
public sealed class MarketDataService : IMarketDataService
{
    private readonly IRpcClient _rpc;
    private readonly ISolanaService _solana;
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(IRpcClient rpc, ISolanaService solana, ILogger<MarketDataService> logger)
    {
        _rpc = rpc;
        _solana = solana;
        _logger = logger;
    }

    public async Task<MarketContext> GetMarketContextAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[MarketData] Fetching live Solana Devnet state...");

        // Parallel RPC calls for speed — all using 'confirmed' commitment
        var balanceTask = _solana.GetBalanceLamportsAsync(ct);
        var slotTask = GetCurrentSlotAsync();
        var blockHashTask = GetLatestBlockhashAsync();
        var perfTask = GetRecentPerformanceAsync();

        await Task.WhenAll(balanceTask, slotTask, blockHashTask, perfTask);

        var balance = await balanceTask;
        var slot = await slotTask;
        var blockHash = await blockHashTask;
        var (txCount, tps) = await perfTask;

        // Derive sentiment from on-chain activity
        var sentiment = DeriveSentiment(tps, balance);

        var context = new MarketContext
        {
            TreasuryBalanceLamports = balance,
            CurrentSlot = slot,
            RecentTransactionCount = txCount,
            EstimatedTps = tps,
            LatestBlockhash = blockHash,
            Sentiment = sentiment
        };

        _logger.LogInformation("[MarketData] Context captured: {Balance:F4} SOL, Slot {Slot}, TPS {Tps:F1}, Sentiment: {Sentiment}",
            context.TreasuryBalanceSol, slot, tps, sentiment);

        return context;
    }

    private async Task<ulong> GetCurrentSlotAsync()
    {
        try
        {
            var response = await _rpc.GetSlotAsync();
            return response.WasSuccessful ? response.Result : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MarketData] Failed to fetch slot. Defaulting to 0.");
            return 0;
        }
    }

    private async Task<string> GetLatestBlockhashAsync()
    {
        try
        {
            var response = await _rpc.GetLatestBlockHashAsync();
            return response.WasSuccessful ? response.Result.Value.Blockhash : "unknown";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MarketData] Failed to fetch blockhash.");
            return "unknown";
        }
    }

    private async Task<(long txCount, double tps)> GetRecentPerformanceAsync()
    {
        try
        {
            var response = await _rpc.GetRecentPerformanceSamplesAsync(5);
            if (response.WasSuccessful && response.Result?.Count > 0)
            {
                long totalTx = 0;
                double totalSeconds = 0;
                foreach (var sample in response.Result)
                {
                    totalTx += (long)sample.NumTransactions;
                    totalSeconds += sample.SamplePeriodSecs;
                }
                var tps = totalSeconds > 0 ? totalTx / totalSeconds : 0;
                return (totalTx, tps);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MarketData] Failed to fetch performance samples.");
        }
        return (0, 0);
    }

    /// <summary>
    /// Derives market sentiment from on-chain activity patterns.
    /// In production this would use real price feeds; for the hackathon we
    /// derive it from TPS and treasury balance patterns.
    /// </summary>
    private static MarketSentiment DeriveSentiment(double tps, ulong balanceLamports)
    {
        // High TPS = network is busy = potential opportunity
        if (tps > 3000) return MarketSentiment.HighActivity;
        if (tps > 1500) return MarketSentiment.Bullish;

        // If treasury is very healthy, sentiment is neutral-to-bullish
        if (balanceLamports > 2_000_000_000) return MarketSentiment.Bullish; // >2 SOL
        if (balanceLamports > 500_000_000) return MarketSentiment.Neutral;   // >0.5 SOL

        // Low balance = conservative
        return MarketSentiment.Bearish;
    }
}
