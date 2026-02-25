using AutoSig.Domain.Models;

namespace AutoSig.Domain.Interfaces;

/// <summary>
/// Contract for all Solana blockchain interactions.
/// The Executor Agent uses this to build and submit transactions.
/// The Signer Enclave logic lives entirely behind this interface.
/// </summary>
public interface ISolanaService
{
    /// <summary>Returns the Base58 public key of the treasury wallet.</summary>
    string GetPublicKey();

    /// <summary>Fetches the SOL balance of the treasury wallet in lamports.</summary>
    Task<ulong> GetBalanceLamportsAsync(CancellationToken ct = default);

    /// <summary>Requests an airdrop of the specified lamports on Devnet (for demo purposes).</summary>
    Task<string> RequestAirdropAsync(ulong lamports, CancellationToken ct = default);

    /// <summary>
    /// Executes an approved trade proposal on-chain.
    /// This is the only method that triggers actual transaction signing.
    /// The private key never leaves this service.
    /// </summary>
    Task<TransactionResult> ExecuteProposalAsync(TradeProposal proposal, CancellationToken ct = default);
}
