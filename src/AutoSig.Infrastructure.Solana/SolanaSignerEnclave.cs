using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using Microsoft.Extensions.Logging;
using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
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
                ProposalType.SolTransfer    => BuildSolTransfer(proposal, blockHash),
                ProposalType.SplTokenMint   => await BuildSplMintTransactionAsync(proposal, blockHash),
                _                           => BuildSolTransfer(proposal, blockHash)
            };

            _logger.LogInformation("[Enclave] 🔐 Signing transaction with treasury keypair...");
            var sendResponse = await _rpcClient.SendTransactionAsync(txBytes);

            if (sendResponse.WasSuccessful && !string.IsNullOrEmpty(sendResponse.Result))
            {
                return new TransactionResult
                {
                    ProposalId    = proposal.Id,
                    Success       = true,
                    SignatureHash = sendResponse.Result,
                    ExecutedAt    = DateTime.UtcNow
                };
            }
            else
            {
                return new TransactionResult
                {
                    ProposalId    = proposal.Id,
                    Success       = false,
                    ErrorMessage  = sendResponse.RawRpcResponse,
                    ExecutedAt    = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Enclave] Transaction execution failed.");
            return new TransactionResult
            {
                ProposalId   = proposal.Id,
                Success      = false,
                ErrorMessage = ex.Message,
                ExecutedAt   = DateTime.UtcNow
            };
        }
    }

    private byte[] BuildSolTransfer(TradeProposal proposal, string blockHash)
    {
        // Validate destination — must be a real Solana address (>= 32 chars Base58)
        var destination = proposal.DestinationAddress.Length >= 32
            ? proposal.DestinationAddress
            : _wallet.Account.PublicKey.Key; // failsafe: send back to self

        return new TransactionBuilder()
            .SetRecentBlockHash(blockHash)
            .SetFeePayer(_wallet.Account.PublicKey)
            .AddInstruction(SystemProgram.Transfer(
                _wallet.Account.PublicKey,
                new PublicKey(destination),
                proposal.AmountLamports))
            .Build([_wallet.Account]);
    }

    /// <summary>
    /// Builds a real SPL Token creation + mint transaction using Solnet's TokenProgram.
    /// This creates a brand-new token on-chain, then mints a supply to the destination wallet.
    /// 4 instructions:
    ///   1. SystemProgram.CreateAccount  → allocate the mint account on chain
    ///   2. TokenProgram.InitializeMint  → configure decimals + mint authority
    ///   3. AssociatedTokenAccountProgram.CreateAssociatedTokenAccount → receiver's ATA
    ///   4. TokenProgram.MintTo          → mint 1000 tokens (with 6 decimals = 1,000,000,000 raw units)
    /// The mint Account is ephemeral — a fresh keypair generated per proposal.
    /// Treasury wallet pays all fees and holds mint authority.
    /// </summary>
    private async Task<byte[]> BuildSplMintTransactionAsync(TradeProposal proposal, string blockHash)
    {
        _logger.LogInformation("[Enclave] Building real SPL Token creation + mint transaction...");

        // Generate a fresh keypair for the new token mint account
        var mintAccount = new Account();
        _logger.LogInformation("[Enclave] New token mint address: {Mint}", mintAccount.PublicKey.Key);

        // Fetch minimum rent-exempt balance for a mint account
        var rentResponse = await _rpcClient.GetMinimumBalanceForRentExemptionAsync(TokenProgram.MintAccountDataSize);
        var rentLamports = rentResponse.Result;

        // Resolve the destination wallet — fall back to treasury if invalid
        var receiverWallet = proposal.DestinationAddress.Length >= 32
            ? new PublicKey(proposal.DestinationAddress)
            : _wallet.Account.PublicKey;

        // Derive the Associated Token Account (ATA) address for the receiver
        PublicKey.TryFindProgramAddress(
            new[]
            {
                receiverWallet.KeyBytes,
                TokenProgram.ProgramIdKey.KeyBytes,
                mintAccount.PublicKey.KeyBytes
            },
            AssociatedTokenAccountProgram.ProgramIdKey,
            out PublicKey ataAddress, out _);

        _logger.LogInformation("[Enclave] Receiver ATA: {Ata}", ataAddress.Key);

        // 1000 tokens with 6 decimal places = 1_000 * 10^6 raw units
        const byte  decimals      = 6;
        const ulong mintAmount    = 1_000UL * 1_000_000UL;

        var tx = new TransactionBuilder()
            .SetRecentBlockHash(blockHash)
            .SetFeePayer(_wallet.Account.PublicKey)
            // Instruction 1: allocate mint account storage on chain
            .AddInstruction(SystemProgram.CreateAccount(
                fromAccount:         _wallet.Account.PublicKey,
                newAccountPublicKey: mintAccount.PublicKey,
                lamports:            rentLamports,
                space:               TokenProgram.MintAccountDataSize,
                programId:           TokenProgram.ProgramIdKey))
            // Instruction 2: initialise the mint (treasury = mintAuthority = freezeAuthority)
            .AddInstruction(TokenProgram.InitializeMint(
                mint:             mintAccount.PublicKey,
                decimals:         decimals,
                mintAuthority:    _wallet.Account.PublicKey,
                freezeAuthority:  _wallet.Account.PublicKey))
            // Instruction 3: create the receiver's Associated Token Account
            .AddInstruction(AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(
                _wallet.Account.PublicKey,
                receiverWallet,
                mintAccount.PublicKey))
            // Instruction 4: mint the token supply to the receiver's ATA
            // Solnet MintTo signature: (mint, destination, amount, authority)
            .AddInstruction(TokenProgram.MintTo(
                mintAccount.PublicKey,
                ataAddress,
                mintAmount,
                _wallet.Account.PublicKey))
            // Both treasury + mint account must sign (mint account authorises its own creation)
            .Build([_wallet.Account, mintAccount]);

        _logger.LogInformation("[Enclave] ✅ SPL Token mint TX built: {Decimals} decimals, {Amount} raw units → {Ata}",
            decimals, mintAmount, ataAddress.Key[..12]);

        return tx;
    }
}

