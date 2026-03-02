using AutoSig.Domain.Events;
using AutoSig.Domain.Interfaces;
using AutoSig.Domain.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace AutoSig.Application.Agents;

/// <summary>JSON contract the LLM must return for a trade proposal.</summary>
internal sealed record LlmTradeProposalResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("amount_lamports")] ulong AmountLamports,
    [property: JsonPropertyName("destination_address")] string DestinationAddress,
    [property: JsonPropertyName("mint_address")] string? MintAddress,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("self_assessed_risk")] double SelfAssessedRisk
);

/// <summary>
/// The Strategist Agent — the BRAIN of the swarm.
/// Receives REAL market data from the Scout, feeds it to the LLM along with
/// actual on-chain state, and generates a concrete trade proposal.
/// The LLM now operates on facts, not fiction.
/// </summary>
public sealed class StrategistAgent(
    IMediator mediator,
    ILlmProvider llm,
    ILogger<StrategistAgent> logger) : INotificationHandler<MarketOpportunityFoundEvent>
{
    private const string SystemPrompt = """
        You are the Strategist Agent of AutoSig, an autonomous multi-agent treasury system on Solana Devnet.
        You receive REAL on-chain market data AND live SOL/USD price data from CoinGecko.
        Your role is to analyze this data and generate a concrete, safe transaction proposal.
        
        You MUST respond with a single, valid JSON object. No markdown, no explanation, ONLY raw JSON.
        The JSON must conform to this exact schema:
        {
          "type": "SolTransfer" | "SplTokenTransfer" | "SplTokenMint",
          "amount_lamports": <integer, max 500000000 which is 0.5 SOL>,
          "destination_address": "<valid Solana Base58 address — chosen from the whitelist below>",
          "mint_address": "<Base58 mint address or null>",
          "rationale": "<1-2 sentence explanation referencing the actual market data AND price action>",
          "self_assessed_risk": <float between 0.0 and 1.0>
        }
        
        DESTINATION WHITELIST — you must pick ONE based on market conditions:
        - "DVt1X6D2nLaVBFQKnafm4gNPucLxUhFB9SrBKBkH7CqP"  → LIQUIDITY VAULT: Use when Bullish or HighActivity. Deploy capital here when market is rising.
        - "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJe8bv"   → SAFE HAVEN RESERVE: Use when Bearish or low confidence. Move funds here to protect capital.
        - "G6EoTTTgpkNBtVXo96EQp2m6uwwVh2Kt6YidjkmQqoha"  → EXPLORATION VAULT: Use when Neutral. Small test transfers when conditions are unclear.
        
        IMPORTANT CONSTRAINTS:
        - Never propose more than 500000000 lamports (0.5 SOL).
        - Scale your proposed amount relative to the treasury balance — NEVER propose more than 25% of the current balance.
        - Keep self_assessed_risk honest. A simple transfer should be 0.1–0.2.
        - Your rationale MUST reference the actual SOL/USD price and 24h change you received.
        - If the market sentiment is Bearish, propose a minimal amount (< 10_000_000 lamports) and use the SAFE HAVEN RESERVE.
        - If proposing SplTokenMint, destination_address is the wallet to receive minted tokens.
        """;


    public async Task Handle(MarketOpportunityFoundEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Strategist] 🧠 Analyzing real market data and crafting proposal...");

        try
        {
            // Feed REAL on-chain data to the LLM — not fake opportunities
            var userMessage = $"""
                === LIVE MARKET DATA FROM SCOUT ===
                {notification.Context.ToSummary()}
                
                === SCOUT'S ANALYSIS ===
                Opportunity: {notification.OpportunityDescription}
                Scout Confidence: {notification.ConfidenceScore:P0}
                
                Based on this REAL on-chain data, generate a transaction proposal.
                Remember: scale the amount to be proportional to the treasury balance.
                Current treasury: {notification.Context.TreasuryBalanceLamports:N0} lamports ({notification.Context.TreasuryBalanceSol:F4} SOL).
                """;

            var response = await llm.CompleteTypedAsync<LlmTradeProposalResponse>(SystemPrompt, userMessage, cancellationToken);

            // Parse the type enum safely
            if (!Enum.TryParse<ProposalType>(response.Type, out var proposalType))
                proposalType = ProposalType.SolTransfer;

            var proposal = new TradeProposal
            {
                Opportunity = notification.OpportunityDescription,
                Type = proposalType,
                AmountLamports = response.AmountLamports,
                DestinationAddress = response.DestinationAddress,
                MintAddress = response.MintAddress,
                Rationale = response.Rationale,
                SelfAssessedRisk = response.SelfAssessedRisk
            };

            logger.LogInformation("[Strategist] Proposal created: {Type} | {Amount} lamports → {Destination}",
                proposal.Type, proposal.AmountLamports, proposal.DestinationAddress[..8] + "...");

            await mediator.Publish(new ProposalGeneratedEvent(proposal), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Strategist] Failed to generate a valid proposal after retries.");
        }
    }
}
