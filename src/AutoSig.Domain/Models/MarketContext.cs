using System.Globalization;

namespace AutoSig.Domain.Models;

/// <summary>
/// Real-time market context gathered by the Scout Agent from on-chain data.
/// This replaces hardcoded opportunity strings with actual Solana network state.
/// </summary>
public sealed record MarketContext
{
    /// <summary>Current SOL balance of the treasury wallet in lamports.</summary>
    public required ulong TreasuryBalanceLamports { get; init; }

    /// <summary>Current SOL balance formatted as SOL (human-readable).</summary>
    public double TreasuryBalanceSol => TreasuryBalanceLamports / 1_000_000_000.0;

    /// <summary>Current Solana network slot height — indicates chain liveness.</summary>
    public required ulong CurrentSlot { get; init; }

    /// <summary>Recent number of transactions in the last epoch — network activity indicator.</summary>
    public required long RecentTransactionCount { get; init; }

    /// <summary>Current estimated network TPS (transactions per second).</summary>
    public required double EstimatedTps { get; init; }

    /// <summary>Current blockhash — proves data freshness.</summary>
    public required string LatestBlockhash { get; init; }

    /// <summary>Timestamp when this context was captured.</summary>
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Simulated market sentiment derived from on-chain activity patterns.</summary>
    public required MarketSentiment Sentiment { get; init; }

    /// <summary>Human-readable summary of the current market state for the LLM.</summary>
    public string ToSummary() =>
        FormattableString.Invariant($"""
        === LIVE SOLANA DEVNET STATE (captured {CapturedAt:HH:mm:ss} UTC) ===
        Treasury Balance : {TreasuryBalanceSol:F4} SOL ({TreasuryBalanceLamports:N0} lamports)
        Network Slot     : {CurrentSlot:N0}
        Recent Tx Count  : {RecentTransactionCount:N0}
        Estimated TPS    : {EstimatedTps:F1}
        Latest Blockhash : {LatestBlockhash[..16]}...
        Market Sentiment : {Sentiment}
        """);
}

/// <summary>Market sentiment derived from on-chain activity analysis.</summary>
public enum MarketSentiment
{
    Bearish,
    Neutral,
    Bullish,
    HighActivity
}
