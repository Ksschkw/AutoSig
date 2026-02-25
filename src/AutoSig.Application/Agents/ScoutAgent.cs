using AutoSig.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoSig.Application.Agents;

/// <summary>
/// The Scout Agent.
/// Runs on a timer and simulates detecting market conditions / DeFi opportunities.
/// In a production system, this would poll Jupiter API, Birdeye, or on-chain feeds.
/// Emits a MarketOpportunityFoundEvent to kick off the swarm pipeline.
/// </summary>
public sealed class ScoutAgent(IMediator mediator, ILogger<ScoutAgent> logger)
{
    private static readonly string[] Opportunities =
    [
        "Devnet SOL/USDC liquidity pool showing 2.1% yield imbalance — arb opportunity detected.",
        "Treasury idle for 60+ seconds. Opportunity: mint test SPL token to demonstrate asset creation.",
        "Mock sentiment feed: 'BULLISH' signal for SOL. Recommend small liquidity deployment.",
        "Devnet faucet reserve sufficient. Opportunity: Transfer SOL to a test protocol vault.",
    ];

    /// <summary>Called periodically by the ConsensusLoop to trigger the swarm pipeline.</summary>
    public async Task ScanAsync(CancellationToken ct = default)
    {
        // Simulates scanning market conditions — picks from realistic example opportunities
        var opportunity = Opportunities[Random.Shared.Next(Opportunities.Length)];
        var confidence = 0.60 + Random.Shared.NextDouble() * 0.35; // 60-95% confidence

        logger.LogInformation("[Scout] Opportunity detected: {Opportunity} (confidence: {Confidence:P0})", opportunity, confidence);

        await mediator.Publish(new MarketOpportunityFoundEvent(opportunity, confidence), ct);
    }
}
