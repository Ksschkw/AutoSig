namespace AutoSig.Domain.Models;

/// <summary>Verdict returned by the Risk Manager after evaluating a TradeProposal.</summary>
public enum RiskVerdict
{
    Approved,
    RejectedByHardGuardrail,
    RejectedByAiGuardrail
}

/// <summary>
/// The output of the Risk Manager Agent's evaluation.
/// Contains both the verdict and the reasoning for full auditability.
/// </summary>
public sealed record RiskAssessment
{
    public Guid ProposalId { get; init; }
    public required RiskVerdict Verdict { get; init; }

    /// <summary>Detailed explanation from the Risk Manager (AI or hard-coded rule).</summary>
    public required string Reasoning { get; init; }

    /// <summary>AI-assessed risk score (0.0 = safe, 1.0 = extremely dangerous). Null if rejected by hard guardrail before AI evaluation.</summary>
    public double? AiRiskScore { get; init; }

    public bool IsApproved => Verdict == RiskVerdict.Approved;
}
