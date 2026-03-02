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
/// The Strategist Agent.
/// Listens for MarketOpportunityFoundEvent, prompts the LLM to create a structured trade proposal,
/// then emits a ProposalGeneratedEvent.
/// </summary>
public sealed class StrategistAgent(
    IMediator mediator,
    ILlmProvider llm,
    ILogger<StrategistAgent> logger) : INotificationHandler<MarketOpportunityFoundEvent>
{
    private const string SystemPrompt = """
        You are the Strategist Agent of AutoSig, an autonomous multi-agent treasury system on Solana.
        Your role is to analyze market opportunities and generate a concrete, safe transaction proposal.
        
        You MUST respond with a single, valid JSON object. No markdown, no explanation, ONLY raw JSON.
        The JSON must conform to this exact schema:
        {
          "type": "SolTransfer" | "SplTokenTransfer" | "SplTokenMint",
          "amount_lamports": <integer, max 500000000 which is 0.5 SOL>,
          "destination_address": "<valid Solana Base58 address>",
          "mint_address": "<Base58 mint address or null>",
          "rationale": "<1-2 sentence explanation of the proposal>",
          "self_assessed_risk": <float between 0.0 and 1.0>
        }
        
        IMPORTANT CONSTRAINTS:
        - Never propose more than 500000000 lamports (0.5 SOL).
        - For SolTransfer, use destination: "DVt1X6D2nLaVBFQKnafm4gNPucLxUhFB9SrBKBkH7CqP" (our test protocol vault).
        - For SplTokenMint, destination_address is the wallet to receive minted tokens.
        - Keep self_assessed_risk honest. A simple transfer should be 0.1–0.2.
        """;

    public async Task Handle(MarketOpportunityFoundEvent notification, CancellationToken cancellationToken)
    {
        Console.WriteLine("[DEBUG] StrategistAgent received the event. Calling LLM...");
        logger.LogInformation("[Strategist] Analyzing opportunity and crafting proposal...");

        try
        {
            var userMessage = $"Market Opportunity Detected:\n{notification.OpportunityDescription}\nConfidence Score: {notification.ConfidenceScore:P0}\n\nGenerate a transaction proposal.";

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
