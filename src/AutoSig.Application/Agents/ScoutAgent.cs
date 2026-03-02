using AutoSig.Domain.Events;
using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoSig.Application.Agents;

/// <summary>
/// The Scout Agent — the EYES of the swarm.
/// Queries the Solana blockchain via RPC to gather real-time market data,
/// analyzes on-chain activity patterns, and emits opportunities for the Strategist.
/// NO hardcoded fake data — everything comes from the live Devnet.
/// </summary>
public sealed class ScoutAgent(
    IMediator mediator,
    IMarketDataService marketData,
    ILogger<ScoutAgent> logger)
{
    /// <summary>Called periodically by the ConsensusLoop to trigger the swarm pipeline.</summary>
    public async Task ScanAsync(CancellationToken ct = default)
    {
        logger.LogInformation("[Scout] 🔭 Scanning Solana Devnet for real-time opportunities...");

        // Fetch actual on-chain data via RPC
        var context = await marketData.GetMarketContextAsync(ct);

        // Analyze the real market context to generate an opportunity description
        var (opportunity, confidence) = AnalyzeMarket(context);

        logger.LogInformation("[Scout] Opportunity identified: {Opportunity} (confidence: {Confidence:P0})",
            opportunity, confidence);

        await mediator.Publish(new MarketOpportunityFoundEvent(opportunity, confidence, context), ct);
    }

    /// <summary>
    /// Analyzes live on-chain data to determine the best trading opportunity.
    /// This is deterministic logic based on actual network state — not random strings.
    /// </summary>
    private static (string opportunity, double confidence) AnalyzeMarket(MarketContext ctx)
    {
        // Strategy 1: Low treasury balance — conservative, small test transfer
        if (ctx.TreasuryBalanceLamports < 100_000_000) // < 0.1 SOL
        {
            return (
                $"Treasury balance critically low at {ctx.TreasuryBalanceSol:F4} SOL. " +
                "Recommending minimal test transfer to demonstrate autonomous capability while preserving capital.",
                0.55
            );
        }

        // Strategy 2: High network activity — the chain is buzzing, good time to act
        if (ctx.Sentiment == MarketSentiment.HighActivity)
        {
            return (
                $"Network showing high activity ({ctx.EstimatedTps:F0} TPS, slot {ctx.CurrentSlot:N0}). " +
                $"Treasury holds {ctx.TreasuryBalanceSol:F4} SOL. " +
                "Recommending strategic SOL deployment to test protocol vault during peak network conditions.",
                0.82
            );
        }

        // Strategy 3: Bullish sentiment — moderate deployment
        if (ctx.Sentiment == MarketSentiment.Bullish)
        {
            return (
                $"Bullish on-chain indicators: {ctx.EstimatedTps:F0} TPS across {ctx.RecentTransactionCount:N0} recent transactions. " +
                $"Treasury healthy at {ctx.TreasuryBalanceSol:F4} SOL. " +
                "Recommending moderate liquidity deployment to Devnet test vault.",
                0.75
            );
        }

        // Strategy 4: Neutral conditions — small exploratory trade
        if (ctx.Sentiment == MarketSentiment.Neutral)
        {
            return (
                $"Market conditions neutral (TPS: {ctx.EstimatedTps:F0}, Balance: {ctx.TreasuryBalanceSol:F4} SOL). " +
                "Recommending small exploratory transfer to maintain agent activity and demonstrate autonomous execution.",
                0.65
            );
        }

        // Strategy 5: Bearish — minimal activity, demonstrate caution
        return (
            $"Bearish market indicators detected. Network TPS low at {ctx.EstimatedTps:F0}. " +
            $"Treasury at {ctx.TreasuryBalanceSol:F4} SOL. " +
            "Recommending minimal-value test transaction only. Capital preservation is priority.",
            0.50
        );
    }
}
