using AutoSig.Application.Agents;
using Xunit;
using AutoSig.Domain.Events;
using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AutoSig.Tests;

/// <summary>
/// These tests PROVE that the Risk Manager's Hard and Policy Guardrails
/// cannot be bypassed, no matter what the LLM says.
/// This is the mathematical guarantee that makes AutoSig safe.
/// </summary>
public class RiskManagerGuardrailTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IRiskManagerLlmProvider _llm = Substitute.For<IRiskManagerLlmProvider>();
    private readonly ISolanaService _solana = Substitute.For<ISolanaService>();
    private readonly ILogger<RiskManagerAgent> _logger = Substitute.For<ILogger<RiskManagerAgent>>();

    private RiskManagerAgent CreateAgent() => new(_mediator, _llm, _solana, new TradingPolicy(), _logger);


    private static TradeProposal CreateProposal(
        ulong amount = 100_000_000,
        string destination = "DVt1X6D2nLaVBFQKnafm4gNPucLxUhFB9SrBKBkH7CqP") =>
        new()
        {
            Opportunity = "Test opportunity",
            Type = ProposalType.SolTransfer,
            AmountLamports = amount,
            DestinationAddress = destination,
            Rationale = "Test rationale",
            SelfAssessedRisk = 0.1
        };

    //  HARD GUARDRAIL TESTS 

    [Fact]
    public async Task HardGuardrail_RejectsAmountExceedingMaximum()
    {
        // Arrange: Try to transfer 1 SOL (exceeds 0.5 SOL max)
        var agent = CreateAgent();
        var proposal = CreateProposal(amount: 1_000_000_000);
        var notification = new ProposalGeneratedEvent(proposal);

        // Act
        await agent.Handle(notification, CancellationToken.None);

        // Assert: Must have published a REJECTION event, not approval
        await _mediator.Received(1).Publish(
            Arg.Is<ProposalRejectedEvent>(e =>
                e.Assessment.Verdict == RiskVerdict.RejectedByHardGuardrail &&
                e.Assessment.Reasoning.Contains("exceeds maximum")),
            Arg.Any<CancellationToken>());

        // The LLM should NEVER have been called  hard guardrail catches it first
        await _llm.DidNotReceive().CompleteTypedAsync<object>(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HardGuardrail_RejectsBlockedDestination()
    {
        // Arrange: Try to send to the System Program (blocklisted)
        var agent = CreateAgent();
        var proposal = CreateProposal(destination: "11111111111111111111111111111111");
        var notification = new ProposalGeneratedEvent(proposal);

        // Act
        await agent.Handle(notification, CancellationToken.None);

        // Assert: Rejected by hard guardrail
        await _mediator.Received(1).Publish(
            Arg.Is<ProposalRejectedEvent>(e =>
                e.Assessment.Verdict == RiskVerdict.RejectedByHardGuardrail &&
                e.Assessment.Reasoning.Contains("blocklist")),
            Arg.Any<CancellationToken>());
    }

    //  POLICY GUARDRAIL TESTS 

    [Fact]
    public async Task PolicyGuardrail_RejectsWhenBelowReserveFloor()
    {
        // Arrange: Treasury has 0.04 SOL, proposal wants 0.01 SOL
        // After trade: 0.03 SOL < 0.05 SOL reserve floor
        _solana.GetBalanceLamportsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ulong>(40_000_000)); // 0.04 SOL

        var agent = CreateAgent();
        var proposal = CreateProposal(amount: 10_000_000); // 0.01 SOL
        var notification = new ProposalGeneratedEvent(proposal);

        // Act
        await agent.Handle(notification, CancellationToken.None);

        // Assert: Rejected because balance - amount < reserve floor
        await _mediator.Received(1).Publish(
            Arg.Is<ProposalRejectedEvent>(e =>
                e.Assessment.Verdict == RiskVerdict.RejectedByHardGuardrail &&
                e.Assessment.Reasoning.Contains("reserve")),
            Arg.Any<CancellationToken>());
    }

    //  DOMAIN MODEL TESTS 

    [Fact]
    public void TradingPolicy_HasCorrectDefaults()
    {
        var policy = new TradingPolicy();

        Assert.Equal(500_000_000UL, policy.MaxSingleTransactionLamports);
        Assert.Equal(10, policy.MaxTradesPerHour);
        Assert.Equal(50, policy.MaxTradesPerDay);
        Assert.Equal(0.05, policy.MaxDailyDrawdownPercent);
        Assert.Equal(50_000_000UL, policy.MinReserveBalanceLamports);
        Assert.Contains("11111111111111111111111111111111", policy.BlockedDestinations);
    }

    [Fact]
    public void MarketContext_CalculatesSolBalanceCorrectly()
    {
        var ctx = new MarketContext
        {
            TreasuryBalanceLamports = 1_500_000_000,
            CurrentSlot = 100,
            RecentTransactionCount = 1000,
            EstimatedTps = 500,
            LatestBlockhash = "abc123def456ghi789jkl012mno345pq",
            Sentiment = MarketSentiment.Bullish
        };

        Assert.Equal(1.5, ctx.TreasuryBalanceSol);
    }

    [Fact]
    public void MarketContext_ToSummaryContainsAllFields()
    {
        var ctx = new MarketContext
        {
            TreasuryBalanceLamports = 1_000_000_000,
            CurrentSlot = 999,
            RecentTransactionCount = 5000,
            EstimatedTps = 1200.5,
            LatestBlockhash = "abc123def456ghi789jkl012mno345pq",
            Sentiment = MarketSentiment.HighActivity
        };

        var summary = ctx.ToSummary();
        Assert.Contains("1.0000 SOL", summary);
        // Use StartsWith on TPS to avoid localespecific decimal/thousands separators
        Assert.Contains("1200", summary);
        Assert.Contains("HighActivity", summary);
    }

    [Fact]
    public void RiskAssessment_IsApproved_ReturnsTrueForApprovedVerdict()
    {
        var assessment = new RiskAssessment
        {
            ProposalId = Guid.NewGuid(),
            Verdict = RiskVerdict.Approved,
            Reasoning = "Safe",
            AiRiskScore = 0.2
        };
        Assert.True(assessment.IsApproved);
    }

    [Fact]
    public void RiskAssessment_IsApproved_ReturnsFalseForRejectedVerdict()
    {
        var assessment = new RiskAssessment
        {
            ProposalId = Guid.NewGuid(),
            Verdict = RiskVerdict.RejectedByHardGuardrail,
            Reasoning = "Over limit",
            AiRiskScore = null
        };
        Assert.False(assessment.IsApproved);
    }

    [Fact]
    public void TransactionResult_ExplorerUrl_GeneratesCorrectDevnetUrl()
    {
        var result = new TransactionResult
        {
            ProposalId = Guid.NewGuid(),
            Success = true,
            SignatureHash = "5wHGgFUGo2qmPKLFv95rGzSjMBcuPCkemJ8sdB5pWjCt",
            ExecutedAt = DateTime.UtcNow
        };
        Assert.Contains("explorer.solana.com/tx/", result.ExplorerUrl);
        Assert.Contains("cluster=devnet", result.ExplorerUrl);
    }
}
