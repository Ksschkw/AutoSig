namespace AutoSig.Domain.Models;

/// <summary>
/// Immutable trading policy that enforces velocity limits, drawdown protection,
/// and program whitelisting. These are C# constants — no LLM can override them.
/// </summary>
public sealed class TradingPolicy
{
    // ── Velocity Limits ──────────────────────────────────────────────────────
    /// <summary>Maximum number of trades allowed per rolling hour.</summary>
    public int MaxTradesPerHour { get; init; } = 10;

    /// <summary>Maximum number of trades allowed per rolling 24-hour period.</summary>
    public int MaxTradesPerDay { get; init; } = 50;

    /// <summary>Minimum cooldown between consecutive trades.</summary>
    public TimeSpan MinTimeBetweenTrades { get; init; } = TimeSpan.FromSeconds(15);

    // ── Drawdown Protection ──────────────────────────────────────────────────
    /// <summary>Maximum percentage of starting balance that can be lost before emergency halt (0.0 to 1.0).</summary>
    public double MaxDailyDrawdownPercent { get; init; } = 0.05; // 5%

    /// <summary>Minimum SOL balance (in lamports) that must remain in the treasury at all times.</summary>
    public ulong MinReserveBalanceLamports { get; init; } = 50_000_000; // 0.05 SOL

    // ── Amount Limits ────────────────────────────────────────────────────────
    /// <summary>Maximum amount per single transaction in lamports.</summary>
    public ulong MaxSingleTransactionLamports { get; init; } = 500_000_000; // 0.5 SOL

    // ── Program Whitelisting ─────────────────────────────────────────────────
    /// <summary>Only these Solana program addresses can appear in transaction instructions.</summary>
    public HashSet<string> AllowedProgramIds { get; init; } =
    [
        "11111111111111111111111111111111",   // System Program (SOL transfers)
        "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA", // SPL Token Program
        "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb",  // Token-2022 Program
    ];

    /// <summary>Addresses explicitly banned from receiving funds.</summary>
    public HashSet<string> BlockedDestinations { get; init; } =
    [
        "11111111111111111111111111111111", // System Program — never send SOL here
    ];
}
