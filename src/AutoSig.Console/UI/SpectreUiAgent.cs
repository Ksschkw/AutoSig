using AutoSig.Domain.Events;
using MediatR;
using Spectre.Console;

namespace AutoSig.Console.UI;

/// <summary>
/// The Spectre UI Agent — listens to all MediatR swarm events and
/// renders a real-time, color-coded hacker-style terminal dashboard.
/// </summary>
public sealed class SpectreUiAgent :
    INotificationHandler<MarketOpportunityFoundEvent>,
    INotificationHandler<ProposalGeneratedEvent>,
    INotificationHandler<ProposalApprovedEvent>,
    INotificationHandler<ProposalRejectedEvent>,
    INotificationHandler<TransactionCompletedEvent>
{
    private static void PrintAgentPanel(string agent, string emoji, Color color, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var panel = new Panel($"[bold {color.ToMarkup()}]{emoji} {message}[/]")
        {
            Header = new PanelHeader($"[bold white] {agent} [/] [grey]UTC {timestamp}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(color),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(panel);
    }

    public Task Handle(MarketOpportunityFoundEvent notification, CancellationToken cancellationToken)
    {
        PrintAgentPanel("SCOUT", "🔭", Color.Cyan1,
            $"Opportunity Detected (Confidence: {notification.ConfidenceScore:P0})\n{notification.OpportunityDescription}");
        return Task.CompletedTask;
    }

    public Task Handle(ProposalGeneratedEvent notification, CancellationToken cancellationToken)
    {
        var p = notification.Proposal;
        PrintAgentPanel("STRATEGIST", "🧠", Color.Yellow,
            $"Proposal Generated [{p.Type}]\n" +
            $"Amount: {p.AmountLamports:N0} lamports | Dest: {p.DestinationAddress[..10]}...\n" +
            $"Rationale: {p.Rationale}\n" +
            $"Self-Risk: {p.SelfAssessedRisk:F2}");
        return Task.CompletedTask;
    }

    public Task Handle(ProposalApprovedEvent notification, CancellationToken cancellationToken)
    {
        var a = notification.Assessment;
        PrintAgentPanel("RISK MANAGER", "🛡️", Color.Green,
            $"✅ APPROVED | AI Risk Score: {a.AiRiskScore:F2}\nReason: {a.Reasoning}");
        return Task.CompletedTask;
    }

    public Task Handle(ProposalRejectedEvent notification, CancellationToken cancellationToken)
    {
        var a = notification.Assessment;
        var prefix = a.Verdict.ToString().Contains("Hard") ? "🚫 HARD GUARDRAIL" : "❌ AI GUARDRAIL";
        PrintAgentPanel("RISK MANAGER", "🛡️", Color.Red,
            $"{prefix} REJECTED\nReason: {a.Reasoning}");
        return Task.CompletedTask;
    }

    public Task Handle(TransactionCompletedEvent notification, CancellationToken cancellationToken)
    {
        var r = notification.Result;
        if (r.Success)
        {
            PrintAgentPanel("EXECUTOR", "🚀", Color.Magenta1,
                $"Transaction CONFIRMED! 🎉\nSignature: {r.SignatureHash![..20]}...\n[link={r.ExplorerUrl}]View on Solana Explorer ↗[/]");
        }
        else
        {
            PrintAgentPanel("EXECUTOR", "💥", Color.Red,
                $"Transaction FAILED!\nError: {r.ErrorMessage}");
        }
        return Task.CompletedTask;
    }
}
