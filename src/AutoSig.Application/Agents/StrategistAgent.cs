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
/// The Strategist Agent  the BRAIN of the swarm.
/// Receives REAL market data from the Scout, feeds it to the LLM along with
/// actual on-chain state, and generates a concrete trade proposal.
/// The LLM now operates on facts, not fiction.
/// </summary>
public sealed class StrategistAgent(
    IMediator mediator,
    IStrategistLlmProvider llm,
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
          "destination_address": "<valid Solana Base58 address  chosen from the whitelist below>",
          "mint_address": "<Base58 mint address or null>",
          "rationale": "<1-2 sentence explanation referencing the actual market data AND price action>",
          "self_assessed_risk": <float between 0.0 and 1.0>
        }
        
        DESTINATION WHITELIST  you must pick ONE based on market conditions:
        - "DVt1X6D2nLaVBFQKnafm4gNPucLxUhFB9SrBKBkH7CqP"   LIQUIDITY VAULT: Use when Bullish or HighActivity. Deploy capital here when market is rising.
        - "9Cz592iKyRYZznR5gEWNwV2PeK1XBkx2Zyx9nQ3cn5y7"   SAFE HAVEN RESERVE: Use when Bearish or low confidence. Move funds here to protect capital.
        - "GeHD5Equ44E4nhfBkaD8UFZDdGb1qLY981GNVboMr9Gx"   EXPLORATION VAULT: Use when Neutral. Small test transfers when conditions are unclear.
        
        IMPORTANT CONSTRAINTS:
        - Never propose more than 500000000 lamports (0.5 SOL).
        - Scale your proposed amount relative to the treasury balance  NEVER propose more than 25% of the current balance.
        - Keep self_assessed_risk honest. A simple transfer should be 0.10.2.
        - Your rationale MUST reference the actual SOL/USD price and 24h change you received.
        - If the market sentiment is Bearish, propose a minimal amount (< 10_000_000 lamports) and use the SAFE HAVEN RESERVE.
        - If proposing SplTokenMint, destination_address is the wallet to receive minted tokens.
        """;


    public async Task Handle(MarketOpportunityFoundEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("[Strategist]  REASONING STARTED ");
        logger.LogInformation("[Strategist]  Scout confidence: {Confidence:P0} | Sentiment: {Sentiment}",
            notification.ConfidenceScore, notification.Context.Sentiment);
        logger.LogInformation("[Strategist]  Treasury: {Balance:F4} SOL | SOL Price: ${Price:F2} ({Change:+0.00;-0.00}%)",
            notification.Context.TreasuryBalanceSol, notification.Context.SolUsdPrice, notification.Context.Sol24hChangePct);

        try
        {
            // Feed REAL on-chain data to the LLM  not fake opportunities
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

            logger.LogInformation("[Strategist]  Calling DeepSeek/LLM via OpenRouter for trade proposal...");

            LlmTradeProposalResponse response;
            try
            {
                response = await llm.CompleteTypedAsync<LlmTradeProposalResponse>(SystemPrompt, userMessage, cancellationToken);
                logger.LogInformation("[Strategist] LLM responded successfully.");
            }
            catch (HttpRequestException httpEx) when (
                httpEx.Message.Contains("404") || httpEx.Message.Contains("401") ||
                httpEx.Message.Contains("No endpoints found"))
            {
                // LLM completely unreachable (bad model ID, auth failure, etc.)
                // Do NOT emit a fallback proposal — that would run a real on-chain transaction
                // with zero AI reasoning behind it. Skip the cycle entirely.
                logger.LogError("[Strategist] LLM endpoint unavailable ({Status}). Skipping cycle — no transaction will execute.",
                    httpEx.StatusCode);
                logger.LogError("[Strategist] Check AUTOSIG_STRATEGIST_MODEL in .env — model may not be available on your OpenRouter account.");
                return; // Kills this cycle. ConsensusLoop will retry in 30s.
            }
            catch (Exception ex)
            {
                // Timeout or temporary failure — log and skip. Do NOT transact without AI approval.
                logger.LogWarning(ex, "[Strategist] LLM call failed (timeout or parse error). Skipping cycle for safety.");
                return; // Skip this cycle entirely. No fallback, no transactions.
            }

            // Parse the type enum safely
            if (!Enum.TryParse<ProposalType>(response.Type, out var proposalType))
            {
                logger.LogWarning("[Strategist]  Unknown proposal type '{Type}' from LLM  defaulting to SolTransfer.", response.Type);
                proposalType = ProposalType.SolTransfer;
            }

            var proposal = new TradeProposal
            {
                Opportunity       = notification.OpportunityDescription,
                Type              = proposalType,
                AmountLamports    = response.AmountLamports,
                DestinationAddress= response.DestinationAddress,
                MintAddress       = response.MintAddress,
                Rationale         = response.Rationale,
                SelfAssessedRisk  = response.SelfAssessedRisk
            };

            logger.LogInformation("[Strategist]  Proposal built: {Type} | {Amount:N0} lam | Destination: {Dest}...",
                proposal.Type, proposal.AmountLamports, proposal.DestinationAddress[..Math.Min(8, proposal.DestinationAddress.Length)]);
            logger.LogInformation("[Strategist]   Rationale: {Rationale}", response.Rationale[..Math.Min(100, response.Rationale.Length)]);
            logger.LogInformation("[Strategist]  Publishing ProposalGeneratedEvent to Risk Manager...");

            // Pass the baton to the Risk Manager
            await mediator.Publish(new ProposalGeneratedEvent(proposal), cancellationToken);

            logger.LogInformation("[Strategist]  ProposalGeneratedEvent published.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Strategist]  FATAL ERROR crafting proposal.");
        }
    }
}
