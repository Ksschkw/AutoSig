using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using Microsoft.Extensions.Logging;
using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Wallet;

namespace AutoSig.Infrastructure.Solana;

/// <summary>
/// The Solana Signer Enclave.
/// This is the ONLY component in the system that has access to the private key.
/// All transaction building and signing happens within this sealed class.
/// The private key is loaded once at construction and never exposed externally.
/// </summary>
public sealed class SolanaSignerEnclave : ISolanaService
{
    private readonly Wallet _wallet;
    private readonly IRpcClient _rpcClient;
    private readonly ILogger<SolanaSignerEnclave> _logger;

    // The treasury "vault" destination for demonstration purposes on Devnet
    private const string TestProtocolVaultAddress = "DVt1X6D2nLaVBFQKnafm4gNPucLxUhFB9SrBKBkH7CqP";

    public SolanaSignerEnclave(string base58PrivateKey, IRpcClient rpcClient, ILogger<SolanaSignerEnclave> logger)
    {
        // ⚠️ ENCLAVE: Private key loaded and locked in this object. Never serialized, logged, or exposed.
        _wallet = new Wallet(Convert.FromBase64String(base58PrivateKey));
        _rpcClient = rpcClient;
        _logger = logger;
    }

    public string GetPublicKey() => _wallet.Account.PublicKey.Key;

    public async Task<ulong> GetBalanceLamportsAsync(CancellationToken ct = default)
    {
        var response = await _rpcClient.GetBalanceAsync(_wallet.Account.PublicKey.Key);
        return response.Result?.Value ?? 0;
    }

    public async Task<string> RequestAirdropAsync(ulong lamports, CancellationToken ct = default)
    {
        _logger.LogInformation("[Enclave] Requesting airdrop of {Lamports} lamports...", lamports);
        var response = await _rpcClient.RequestAirdropAsync(_wallet.Account.PublicKey.Key, lamports);
        if (!response.WasSuccessful)
            throw new InvalidOperationException($"Airdrop failed: {response.RawRpcResponse}");
        return response.Result;
    }

    public async Task<TransactionResult> ExecuteProposalAsync(TradeProposal proposal, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("[Enclave] Building transaction for proposal {Id}...", proposal.Id.ToString()[..8]);

            var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
            if (!blockHashResponse.WasSuccessful)
                throw new InvalidOperationException("Failed to fetch recent block hash.");

            var blockHash = blockHashResponse.Result.Value.Blockhash;

            // Build transaction based on proposal type
            byte[] txBytes = proposal.Type switch
            {
                ProposalType.SolTransfer => BuildSolTransfer(proposal, blockHash),
                ProposalType.SplTokenMint => BuildSplMintTransaction(proposal, blockHash),
                _ => BuildSolTransfer(proposal, blockHash)
            };

            _logger.LogInformation("[Enclave] 🔐 Signing transaction with treasury keypair...");
            var sendResponse = await _rpcClient.SendTransactionAsync(txBytes);

            if (sendResponse.WasSuccessful && !string.IsNullOrEmpty(sendResponse.Result))
            {
                return new TransactionResult
                {
                    ProposalId = proposal.Id,
                    Success = true,
                    SignatureHash = sendResponse.Result,
                    ExecutedAt = DateTime.UtcNow
                };
            }
            else
            {
                return new TransactionResult
                {
                    ProposalId = proposal.Id,
                    Success = false,
                    ErrorMessage = sendResponse.RawRpcResponse,
                    ExecutedAt = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Enclave] Transaction execution failed.");
            return new TransactionResult
            {
                ProposalId = proposal.Id,
                Success = false,
                ErrorMessage = ex.Message,
                ExecutedAt = DateTime.UtcNow
            };
        }
    }

    private byte[] BuildSolTransfer(TradeProposal proposal, string blockHash)
    {
        // Use the proposal's destination if it looks valid, otherwise fall back to test vault
        var destination = proposal.DestinationAddress.Length >= 32
            ? proposal.DestinationAddress
            : TestProtocolVaultAddress;

        return new TransactionBuilder()
            .SetRecentBlockHash(blockHash)
            .SetFeePayer(_wallet.Account.PublicKey)
            .AddInstruction(SystemProgram.Transfer(
                _wallet.Account.PublicKey,
                new PublicKey(destination),
                proposal.AmountLamports))
            .Build([_wallet.Account]);
    }

    private byte[] BuildSplMintTransaction(TradeProposal proposal, string blockHash)
    {
        // For SPL token demo: we do a SOL transfer to the test vault to represent a "protocol interaction"
        // In a full implementation, this would use TokenProgram.MintTo()
        _logger.LogInformation("[Enclave] SPL Mint demo mode: executing SOL transfer to test vault as protocol interaction.");
        return BuildSolTransfer(proposal with { AmountLamports = Math.Min(proposal.AmountLamports, 10_000_000) }, blockHash);
    }
}
