using AutoSig.Domain.Events;
using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace AutoSig.Application.Agents;

/// <summary>JSON contract the LLM must return for an AI risk evaluation.</summary>
internal sealed record LlmRiskEvaluationResponse(
    [property: JsonPropertyName("approved")] bool Approved,
    [property: JsonPropertyName("risk_score")] double RiskScore,
    [property: JsonPropertyName("reasoning")] string Reasoning
);

/// <summary>
/// The Risk Manager Agent — the SHIELD of the swarm.
/// Evaluates each proposal in two phases:
///   Phase 1: C# Hard Guardrails (cannot be bypassed by any LLM output).
///   Phase 2: AI Soft Guardrails (independent LLM verifies the Strategist's reasoning).
/// Emits either ProposalApprovedEvent or ProposalRejectedEvent.
/// </summary>
public sealed class RiskManagerAgent(
    IMediator mediator,
    ILlmProvider llm,
    ILogger<RiskManagerAgent> logger) : INotificationHandler<ProposalGeneratedEvent>
{
    // Hard limits — these are IMMUTABLE C# constants. No LLM can override them.
    private const ulong MaxAllowedLamports = 500_000_000; // 0.5 SOL
    private static readonly HashSet<string> BlacklistedAddresses =
    [
        "11111111111111111111111111111111", // System program — never send to this
    ];

    private const string RiskSystemPrompt = """
        You are the Risk Manager Agent of AutoSig, an autonomous multi-agent treasury system.
        Your ONLY job is to evaluate a trade proposal submitted by the Strategist Agent for risks.
        
        Evaluate for: hallucinated addresses, unreasonably large amounts, semantic exploitation, logic flaws.
        
        You MUST respond with a single, valid JSON object. No markdown, no explanation, ONLY raw JSON.
        Schema:
        {
          "approved": true | false,
          "risk_score": <float 0.0 to 1.0>,
          "reasoning": "<1-2 sentences explaining your decision>"
        }
        
        Approve if risk_score < 0.6. Reject if risk_score >= 0.6.
        """;

    public async Task Handle(ProposalGeneratedEvent notification, CancellationToken cancellationToken)
    {
        var proposal = notification.Proposal;
        logger.LogInformation("[RiskManager] Evaluating proposal {Id}...", proposal.Id.ToString()[..8]);

        // ── PHASE 1: HARD GUARDRAILS ──────────────────────────────────────────────
        if (proposal.AmountLamports > MaxAllowedLamports)
        {
            await RejectAsync(proposal, RiskVerdict.RejectedByHardGuardrail,
                $"Hard Guardrail triggered: Amount {proposal.AmountLamports} lamports exceeds max allowed {MaxAllowedLamports}.",
                null, cancellationToken);
            return;
        }

        if (BlacklistedAddresses.Contains(proposal.DestinationAddress))
        {
            await RejectAsync(proposal, RiskVerdict.RejectedByHardGuardrail,
                $"Hard Guardrail triggered: Destination address '{proposal.DestinationAddress}' is blacklisted.",
                null, cancellationToken);
            return;
        }

        logger.LogInformation("[RiskManager] ✅ Hard Guardrails passed. Escalating to AI evaluation...");

        // ── PHASE 2: AI SOFT GUARDRAILS ──────────────────────────────────────────
        try
        {
            var userMessage = $"""
                Evaluate this trade proposal:
                Type: {proposal.Type}
                Amount (lamports): {proposal.AmountLamports}
                Destination: {proposal.DestinationAddress}
                Rationale from Strategist: "{proposal.Rationale}"
                Strategist's self-assessed risk: {proposal.SelfAssessedRisk:F2}
                """;

            var evaluation = await llm.CompleteTypedAsync<LlmRiskEvaluationResponse>(
                RiskSystemPrompt, userMessage, cancellationToken);

            var assessment = new RiskAssessment
            {
                ProposalId = proposal.Id,
                Verdict = evaluation.Approved ? RiskVerdict.Approved : RiskVerdict.RejectedByAiGuardrail,
                Reasoning = evaluation.Reasoning,
                AiRiskScore = evaluation.RiskScore
            };

            if (assessment.IsApproved)
            {
                logger.LogInformation("[RiskManager] ✅ AI Guardrail APPROVED. Score: {Score:F2}. Reason: {Reason}",
                    evaluation.RiskScore, evaluation.Reasoning);
                await mediator.Publish(new ProposalApprovedEvent(proposal, assessment), cancellationToken);
            }
            else
            {
                logger.LogWarning("[RiskManager] ❌ AI Guardrail REJECTED. Score: {Score:F2}. Reason: {Reason}",
                    evaluation.RiskScore, evaluation.Reasoning);
                await mediator.Publish(new ProposalRejectedEvent(proposal, assessment), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[RiskManager] AI evaluation failed. Rejecting proposal as unsafe.");
            await RejectAsync(proposal, RiskVerdict.RejectedByAiGuardrail,
                "AI evaluation failed after retries. Defaulting to REJECT for safety.", null, cancellationToken);
        }
    }

    private async Task RejectAsync(TradeProposal proposal, RiskVerdict verdict, string reason,
        double? aiScore, CancellationToken ct)
    {
        var assessment = new RiskAssessment
        {
            ProposalId = proposal.Id,
            Verdict = verdict,
            Reasoning = reason,
            AiRiskScore = aiScore
        };
        await mediator.Publish(new ProposalRejectedEvent(proposal, assessment), ct);
    }
}
