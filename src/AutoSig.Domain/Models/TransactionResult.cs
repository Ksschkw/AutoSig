namespace AutoSig.Domain.Models;

/// <summary>
/// The final result after the Executor signs and broadcasts a transaction.
/// </summary>
public sealed record TransactionResult
{
    public Guid ProposalId { get; init; }
    public required bool Success { get; init; }

    /// <summary>Solana transaction signature hash (Base58). Null on failure.</summary>
    public string? SignatureHash { get; init; }

    /// <summary>Human-readable error message if the transaction failed.</summary>
    public string? ErrorMessage { get; init; }

    public required DateTime ExecutedAt { get; init; }

    /// <summary>Returns the Solana Explorer URL for this transaction on devnet.</summary>
    public string? ExplorerUrl => SignatureHash is not null
        ? $"https://explorer.solana.com/tx/{SignatureHash}?cluster=devnet"
        : null;
}
