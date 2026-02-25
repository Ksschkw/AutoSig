using AutoSig.Domain.Events;
using AutoSig.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AutoSig.Application.Agents;

/// <summary>
/// The Executor Agent — the only agent that actually touches the blockchain.
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

        logger.LogInformation(
            "[Executor] CONSENSUS REACHED ✅ | Proposal {Id} approved with risk score {Score:F2}. Submitting to Solana Devnet...",
            proposal.Id.ToString()[..8], assessment.AiRiskScore);

        var result = await solana.ExecuteProposalAsync(proposal, cancellationToken);

        if (result.Success)
        {
            logger.LogInformation(
                "[Executor] 🚀 Transaction CONFIRMED! Signature: {Sig}\n  View on Explorer: {Url}",
                result.SignatureHash, result.ExplorerUrl);
        }
        else
        {
            logger.LogError("[Executor] ❌ Transaction FAILED: {Error}", result.ErrorMessage);
        }

        await mediator.Publish(new TransactionCompletedEvent(result), cancellationToken);
    }
}
