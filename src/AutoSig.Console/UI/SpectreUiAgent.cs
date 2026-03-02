using AutoSig.Domain.Events;
using MediatR;
using Spectre.Console;

namespace AutoSig.Console.UI;

/// <summary>
/// The Spectre UI Agent — listens to all MediatR swarm events and
/// renders a real-time, color-coded hacker-style terminal dashboard.
/// Now displays real on-chain market data from the Scout Agent.
/// </summary>
public sealed class SpectreUiAgent :
    INotificationHandler<MarketOpportunityFoundEvent>,
    INotificationHandler<ProposalGeneratedEvent>,
    INotificationHandler<ProposalApprovedEvent>,
    INotificationHandler<ProposalRejectedEvent>,
    INotificationHandler<TransactionCompletedEvent>
{
    private static int _cycleCount;

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
        _cycleCount++;
        AnsiConsole.Write(new Rule($"[cyan]═══ CYCLE {_cycleCount} ═══[/]") { Style = Style.Parse("cyan dim") });

        // Display live market data table
        var ctx = notification.Context;
        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderStyle(new Style(Color.Grey))
            .AddColumn("[grey]Metric[/]")
            .AddColumn("[white]Value[/]");

        table.AddRow("[grey]Treasury Balance[/]", $"[bold white]{ctx.TreasuryBalanceSol:F4} SOL[/]");
        table.AddRow("[grey]Network Slot[/]", $"[white]{ctx.CurrentSlot:N0}[/]");
        table.AddRow("[grey]Estimated TPS[/]", $"[white]{ctx.EstimatedTps:F1}[/]");
        table.AddRow("[grey]Recent Transactions[/]", $"[white]{ctx.RecentTransactionCount:N0}[/]");
        table.AddRow("[grey]Market Sentiment[/]", GetSentimentMarkup(ctx.Sentiment));
        table.AddRow("[grey]Blockhash[/]", $"[grey]{ctx.LatestBlockhash[..20]}...[/]");

        AnsiConsole.Write(new Panel(table)
        {
            Header = new PanelHeader("[bold cyan] 📊 LIVE DEVNET DATA [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1),
            Padding = new Padding(1, 0)
        });

        PrintAgentPanel("SCOUT", "🔭", Color.Cyan1,
            $"Opportunity Detected (Confidence: {notification.ConfidenceScore:P0})\n{notification.OpportunityDescription}");
        return Task.CompletedTask;
    }

    public Task Handle(ProposalGeneratedEvent notification, CancellationToken cancellationToken)
    {
        var p = notification.Proposal;
        PrintAgentPanel("STRATEGIST", "🧠", Color.Yellow,
            $"Proposal Generated [{p.Type}]\n" +
            $"Amount: {p.AmountLamports:N0} lamports ({p.AmountLamports / 1_000_000_000.0:F4} SOL)\n" +
            $"Destination: {p.DestinationAddress[..10]}...\n" +
            $"Rationale: {p.Rationale}\n" +
            $"Self-Risk: {p.SelfAssessedRisk:F2}");
        return Task.CompletedTask;
    }

    public Task Handle(ProposalApprovedEvent notification, CancellationToken cancellationToken)
    {
        var a = notification.Assessment;
        PrintAgentPanel("RISK MANAGER", "🛡️", Color.Green,
            $"✅ ALL 3 PHASES PASSED | AI Risk Score: {a.AiRiskScore:F2}\n" +
            $"Phase 1: Hard Guardrails ✅ | Phase 2: Policy Guardrails ✅ | Phase 3: AI Guardrail ✅\n" +
            $"Reason: {a.Reasoning}");
        return Task.CompletedTask;
    }

    public Task Handle(ProposalRejectedEvent notification, CancellationToken cancellationToken)
    {
        var a = notification.Assessment;
        var phaseLabel = a.Verdict switch
        {
            Domain.Models.RiskVerdict.RejectedByHardGuardrail => "🚫 HARD/POLICY GUARDRAIL",
            Domain.Models.RiskVerdict.RejectedByAiGuardrail => "❌ AI GUARDRAIL",
            _ => "❌ UNKNOWN"
        };
        PrintAgentPanel("RISK MANAGER", "🛡️", Color.Red,
            $"{phaseLabel} REJECTED\nReason: {a.Reasoning}");
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

    private static string GetSentimentMarkup(Domain.Models.MarketSentiment sentiment) => sentiment switch
    {
        Domain.Models.MarketSentiment.Bullish => "[bold green]🟢 BULLISH[/]",
        Domain.Models.MarketSentiment.HighActivity => "[bold yellow]🔥 HIGH ACTIVITY[/]",
        Domain.Models.MarketSentiment.Neutral => "[bold white]⚪ NEUTRAL[/]",
        Domain.Models.MarketSentiment.Bearish => "[bold red]🔴 BEARISH[/]",
        _ => "[grey]UNKNOWN[/]"
    };
}
