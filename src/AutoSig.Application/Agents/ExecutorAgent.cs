using AutoSig.Domain.Events;
using AutoSig.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoSig.Application.Agents;

/// <summary>
/// The Executor Agent  the only agent that actually touches the blockchain.
/// Listens ONLY for ProposalApprovedEvent (the consensus signal).
/// Calls the ISolanaService Signer Enclave to build, sign, and submit the transaction.
/// </summary>
public sealed class ExecutorAgent(
    ISolanaService solana,
    IMediator mediator,
    ILogger<ExecutorAgent> logger) : INotificationHandler<ProposalApprovedEvent>
{
    public async Task Handle(ProposalApprovedEvent notification, CancellationToken cancellationToken)
    {
        var (proposal, assessment) = notification;

        logger.LogInformation("[Executor]  CONSENSUS REACHED ");
        logger.LogInformation("[Executor]  Proposal {Id} | AI risk score: {Score:F2} | Type: {Type}",
            proposal.Id.ToString()[..8], assessment.AiRiskScore, proposal.Type);
        logger.LogInformation("[Executor]   Amount: {Amount:N0} lamports | Destination: {Dest}...",
            proposal.AmountLamports, proposal.DestinationAddress[..Math.Min(8, proposal.DestinationAddress.Length)]);
        logger.LogInformation("[Executor]  Calling SolanaSignerEnclave to build + sign transaction...");

        var result = await solana.ExecuteProposalAsync(proposal, cancellationToken);

        if (result.Success)
        {
            logger.LogInformation("[Executor]  TRANSACTION CONFIRMED ON SOLANA DEVNET!");
            logger.LogInformation("[Executor]   Signature: {Sig}", result.SignatureHash);
            logger.LogInformation("[Executor]   Explorer:  {Url}", result.ExplorerUrl);
        }
        else
        {
            logger.LogError("[Executor]  Transaction FAILED.");
            logger.LogError("[Executor]   Error: {Error}", result.ErrorMessage);
        }

        logger.LogInformation("[Executor]  Publishing TransactionCompletedEvent...");
        await mediator.Publish(new TransactionCompletedEvent(result), cancellationToken);
        logger.LogInformation("[Executor]  Cycle complete.");
    }
}
