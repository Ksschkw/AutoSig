using AutoSig.Domain.Events;
using MediatR;
using Spectre.Console;

namespace AutoSig.Console.UI;

/// <summary>
/// The Spectre UI Agent listens to all MediatR swarm events and
/// renders a real-time, color-coded terminal dashboard showing
/// every agent's activity as it happens.
/// </summary>
public sealed class SpectreUiAgent :
    INotificationHandler<MarketOpportunityFoundEvent>,
    INotificationHandler<ProposalGeneratedEvent>,
    INotificationHandler<ProposalApprovedEvent>,
    INotificationHandler<ProposalRejectedEvent>,
    INotificationHandler<TransactionCompletedEvent>
{
    private static int _cycleCount;

    // ── Shared panel renderer ────────────────────────────────────────────────
    private static void PrintAgentPanel(string agent, string tag, Color color, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var panel = new Panel($"[bold {color.ToMarkup()}]{message}[/]")
        {
            Header = new PanelHeader($"[bold white] {tag} {agent} [/] [grey]UTC {timestamp}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(color),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(panel);
    }

    // ── Agent 1: SCOUT ───────────────────────────────────────────────────────
    public Task Handle(MarketOpportunityFoundEvent notification, CancellationToken cancellationToken)
    {
        _cycleCount++;
        AnsiConsole.Write(new Rule($"[cyan] === CYCLE {_cycleCount} === [/]") { Style = Style.Parse("cyan dim") });

        // Live market data table
        var ctx   = notification.Context;
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.Cyan1))
            .AddColumn("[bold cyan]Metric[/]")
            .AddColumn("[bold white]Value[/]");

        table.AddRow("Treasury Balance",  $"[bold white]{ctx.TreasuryBalanceSol:F4} SOL[/]");

        var priceColor = ctx.Sol24hChangePct >= 0 ? "green" : "red";
        var priceStr   = ctx.SolUsdPrice > 0
            ? $"[bold {priceColor}]${ctx.SolUsdPrice:F2}  ({ctx.Sol24hChangePct:+0.00;-0.00}% 24h)[/]"
            : "[grey]Fetching...[/]";
        table.AddRow("SOL / USD Price", priceStr);
        table.AddRow("Network Slot",       $"[white]{ctx.CurrentSlot:N0}[/]");
        table.AddRow("Estimated TPS",      $"[white]{ctx.EstimatedTps:F1}[/]");
        table.AddRow("Recent Txns",        $"[white]{ctx.RecentTransactionCount:N0}[/]");
        table.AddRow("Market Sentiment",   GetSentimentMarkup(ctx.Sentiment));
        table.AddRow("Blockhash",          $"[grey]{ctx.LatestBlockhash[..20]}...[/]");

        AnsiConsole.Write(new Panel(table)
        {
            Header       = new PanelHeader("[bold cyan] LIVE DEVNET DATA [/]"),
            Border       = BoxBorder.Rounded,
            BorderStyle  = new Style(Color.Cyan1),
            Padding      = new Padding(1, 0)
        });

        PrintAgentPanel("SCOUT", "( 1/4 )", Color.Cyan1,
            $"Opportunity detected (confidence: {notification.ConfidenceScore:P0})\n" +
            $"{notification.OpportunityDescription}");

        return Task.CompletedTask;
    }

    // ── Agent 2: STRATEGIST ──────────────────────────────────────────────────
    public Task Handle(ProposalGeneratedEvent notification, CancellationToken cancellationToken)
    {
        var p = notification.Proposal;
        PrintAgentPanel("STRATEGIST", "( 2/4 )", Color.Yellow,
            $"Proposal generated  -- {p.Type}\n" +
            $"Amount     : {p.AmountLamports:N0} lamports ({p.AmountLamports / 1_000_000_000.0:F4} SOL)\n" +
            $"Destination: {p.DestinationAddress[..12]}...\n" +
            $"Rationale  : {p.Rationale}\n" +
            $"Self-Risk  : {p.SelfAssessedRisk:F2}");
        return Task.CompletedTask;
    }

    // ── Agent 3: RISK MANAGER — approved ────────────────────────────────────
    public Task Handle(ProposalApprovedEvent notification, CancellationToken cancellationToken)
    {
        var a = notification.Assessment;
        PrintAgentPanel("RISK MANAGER", "( 3/4 )", Color.Green,
            $"ALL 3 PHASES PASSED  |  AI Risk Score: {a.AiRiskScore:F2}\n" +
            $"Phase 1: Hard Guardrails   PASS\n" +
            $"Phase 2: Policy Guardrails PASS\n" +
            $"Phase 3: AI Guardrail      PASS\n" +
            $"Reason: {a.Reasoning}");
        return Task.CompletedTask;
    }

    // ── Agent 3: RISK MANAGER — rejected ────────────────────────────────────
    public Task Handle(ProposalRejectedEvent notification, CancellationToken cancellationToken)
    {
        var a = notification.Assessment;
        var phase = a.Verdict switch
        {
            Domain.Models.RiskVerdict.RejectedByHardGuardrail => "HARD + POLICY GUARDRAIL",
            Domain.Models.RiskVerdict.RejectedByAiGuardrail   => "AI GUARDRAIL",
            _                                                   => "UNKNOWN PHASE"
        };
        PrintAgentPanel("RISK MANAGER", "( 3/4 )", Color.Red,
            $"REJECTED by {phase}\n" +
            $"Reason: {a.Reasoning}");
        return Task.CompletedTask;
    }

    // ── Agent 4: EXECUTOR ────────────────────────────────────────────────────
    public Task Handle(TransactionCompletedEvent notification, CancellationToken cancellationToken)
    {
        var r = notification.Result;
        if (r.Success)
        {
            PrintAgentPanel("EXECUTOR", "( 4/4 )", Color.Magenta1,
                $"Transaction CONFIRMED on Solana Devnet\n" +
                $"Signature: {r.SignatureHash![..20]}...\n" +
                $"[link={r.ExplorerUrl}]View on Solana Explorer[/]");
        }
        else
        {
            PrintAgentPanel("EXECUTOR", "( 4/4 )", Color.Red,
                $"Transaction FAILED\n" +
                $"Error: {r.ErrorMessage}");
        }
        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string GetSentimentMarkup(Domain.Models.MarketSentiment sentiment) => sentiment switch
    {
        Domain.Models.MarketSentiment.Bullish      => "[bold green]BULLISH[/]",
        Domain.Models.MarketSentiment.HighActivity => "[bold yellow]HIGH ACTIVITY[/]",
        Domain.Models.MarketSentiment.Neutral      => "[bold white]NEUTRAL[/]",
        Domain.Models.MarketSentiment.Bearish      => "[bold red]BEARISH[/]",
        _                                          => "[grey]UNKNOWN[/]"
    };
}
