using AutoSig.Domain.Models;
using MediatR;

namespace AutoSig.Domain.Events;

/// <summary>Emitted by the Scout Agent when it detects a potential opportunity.</summary>
public sealed record MarketOpportunityFoundEvent(
    string OpportunityDescription,
    double ConfidenceScore
) : INotification;

/// <summary>Emitted by the Strategist Agent after generating a concrete proposal.</summary>
public sealed record ProposalGeneratedEvent(
    TradeProposal Proposal
) : INotification;

/// <summary>Emitted by the Risk Manager when a proposal passes all checks.</summary>
public sealed record ProposalApprovedEvent(
    TradeProposal Proposal,
    RiskAssessment Assessment
) : INotification;

/// <summary>Emitted by the Risk Manager when a proposal fails evaluation.</summary>
public sealed record ProposalRejectedEvent(
    TradeProposal Proposal,
    RiskAssessment Assessment
) : INotification;

/// <summary>Emitted by the Executor once a transaction is signed and broadcast.</summary>
public sealed record TransactionCompletedEvent(
    TransactionResult Result
) : INotification;
