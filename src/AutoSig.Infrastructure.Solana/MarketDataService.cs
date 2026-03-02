using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AutoSig.Infrastructure.Solana;

// CoinGecko response shape
file sealed class CoinGeckoResponse
{
    [JsonPropertyName("solana")]
    public CoinGeckoSolanaData? Solana { get; init; }
}

file sealed class CoinGeckoSolanaData
{
    [JsonPropertyName("usd")]
    public double Usd { get; init; }

    [JsonPropertyName("usd_24h_change")]
    public double Usd24hChange { get; init; }
}

/// <summary>
/// Gathers real-time market context from the Solana Devnet using RPC calls,
/// augmented with live SOL/USD price data from the CoinGecko public API (free, no key).
/// </summary>
public sealed class MarketDataService : IMarketDataService
{
    private readonly IRpcClient _rpc;
    private readonly ISolanaService _solana;
    private readonly HttpClient _http;
    private readonly ILogger<MarketDataService> _logger;

    private const string CoinGeckoUrl =
        "https://api.coingecko.com/api/v3/simple/price?ids=solana&vs_currencies=usd&include_24hr_change=true";

    public MarketDataService(IRpcClient rpc, ISolanaService solana, HttpClient http, ILogger<MarketDataService> logger)
    {
        _rpc = rpc;
        _solana = solana;
        _http = http;
        _logger = logger;
    }

    public async Task<MarketContext> GetMarketContextAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[MarketData] Fetching live Solana Devnet state + CoinGecko price...");

        // Parallel: RPC calls + price oracle at the same time
        var balanceTask   = _solana.GetBalanceLamportsAsync(ct);
        var slotTask      = GetCurrentSlotAsync();
        var blockHashTask = GetLatestBlockhashAsync();
        var perfTask      = GetRecentPerformanceAsync();
        var priceTask     = GetCoinGeckoPriceAsync(ct);

        await Task.WhenAll(balanceTask, slotTask, blockHashTask, perfTask, priceTask);

        var balance             = await balanceTask;
        var slot                = await slotTask;
        var blockHash           = await blockHashTask;
        var (txCount, tps)      = await perfTask;
        var (solPrice, change)  = await priceTask;

        var sentiment = DeriveSentiment(tps, balance, change);

        var context = new MarketContext
        {
            TreasuryBalanceLamports = balance,
            CurrentSlot             = slot,
            RecentTransactionCount  = txCount,
            EstimatedTps            = tps,
            LatestBlockhash         = blockHash,
            Sentiment               = sentiment,
            SolUsdPrice             = solPrice,
            Sol24hChangePct         = change
        };

        _logger.LogInformation(
            "[MarketData] Context: {Balance:F4} SOL | ${Price:F2} USD ({Change:+0.00;-0.00}%) | TPS {Tps:F1} | {Sentiment}",
            context.TreasuryBalanceSol, solPrice, change, tps, sentiment);

        return context;
    }

    private async Task<(double price, double change)> GetCoinGeckoPriceAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var data = await _http.GetFromJsonAsync<CoinGeckoResponse>(CoinGeckoUrl, cts.Token);
            if (data?.Solana is { } sol)
            {
                _logger.LogInformation("[MarketData] CoinGecko: SOL = ${Price:F2} ({Change:+0.00;-0.00}%)", sol.Usd, sol.Usd24hChange);
                return (sol.Usd, sol.Usd24hChange);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MarketData] CoinGecko request failed. Proceeding without price data.");
        }
        return (0, 0);
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
    /// Derives market sentiment by combining real price action (CoinGecko)
    /// with on-chain activity (TPS) and treasury health.
    /// </summary>
    private static MarketSentiment DeriveSentiment(double tps, ulong balanceLamports, double priceChange24h)
    {
        // Real price data wins when available — this is how real trading systems work
        if (priceChange24h > 5.0)  return MarketSentiment.HighActivity; // SOL pumping > 5%
        if (priceChange24h > 2.0)  return MarketSentiment.Bullish;       // SOL up > 2%
        if (priceChange24h < -2.0) return MarketSentiment.Bearish;       // SOL down > 2%

        // Fall back to on-chain signals when price data unavailable (priceChange24h == 0)
        if (tps > 3000) return MarketSentiment.HighActivity;
        if (tps > 1500) return MarketSentiment.Bullish;

        // Treasury health as a final tiebreaker
        if (balanceLamports > 2_000_000_000) return MarketSentiment.Bullish; // > 2 SOL
        if (balanceLamports > 500_000_000)   return MarketSentiment.Neutral;  // > 0.5 SOL

        return MarketSentiment.Bearish;
    }
}
