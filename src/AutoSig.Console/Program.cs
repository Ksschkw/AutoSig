using AutoSig.Application;
using AutoSig.Console.Services;
using AutoSig.Console.UI;
using AutoSig.Infrastructure.AI;
using AutoSig.Infrastructure.Solana;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

// ── Banner ────────────────────────────────────────────────────────────────────
AnsiConsole.Write(new FigletText("AutoSig").Color(Color.Cyan1).Centered());
AnsiConsole.Write(new Markup("[bold cyan]  Autonomous Multi-Agent Treasury on Solana Devnet[/]\n").Centered());
AnsiConsole.Write(new Markup("[grey]  Superteam Nigeria DeFi Developer Challenge | Built with .NET 10[/]\n\n").Centered());
AnsiConsole.Write(new Rule("[cyan]SYSTEM BOOT[/]") { Style = Style.Parse("cyan") });

// ── Configuration ─────────────────────────────────────────────────────────────
DotNetEnv.Env.Load(); // Loads from .env if present

if (args.Contains("--generate-key"))
{
    var newWallet = new Solnet.Wallet.Wallet(Solnet.Wallet.Bip39.WordCount.Twelve, Solnet.Wallet.Bip39.WordList.English);
    var pubKey = newWallet.Account.PublicKey.Key;
    var privKeyBase64 = Convert.ToBase64String(newWallet.Account.PrivateKey.KeyBytes);
    
    AnsiConsole.MarkupLine("\n[green] New Solana Wallet Generated![/]");
    AnsiConsole.MarkupLine($"[grey]Public Key:[/] [white]{pubKey}[/]");
    AnsiConsole.MarkupLine($"[grey]Private Key (Base64):[/] [white]{privKeyBase64}[/]");
    AnsiConsole.MarkupLine("\n[yellow]Add the following to your .env file in the root directory:[/]");
    AnsiConsole.MarkupLine($"AUTOSIG_SOLANA_PRIVATE_KEY={privKeyBase64}\n");
    return;
}

// Keys are read from environment variables or .env file to keep secrets out of source control.
var openRouterApiKey = Environment.GetEnvironmentVariable("AUTOSIG_OPENROUTER_KEY")
    ?? throw new InvalidOperationException("Missing env var: AUTOSIG_OPENROUTER_KEY");

var solanaPrivateKey = Environment.GetEnvironmentVariable("AUTOSIG_SOLANA_PRIVATE_KEY")
    ?? throw new InvalidOperationException("Missing env var: AUTOSIG_SOLANA_PRIVATE_KEY");

// Model selection: use free tier for development, upgrade for demo recording
var llmModel = Environment.GetEnvironmentVariable("AUTOSIG_LLM_MODEL")
    ?? "meta-llama/llama-3.3-70b-instruct:free";

// ── System Info Table ────────────────────────────────────────────────────────
var configTable = new Table()
    .Border(TableBorder.Simple)
    .BorderStyle(new Style(Color.Grey))
    .AddColumn("[grey]Component[/]")
    .AddColumn("[white]Configuration[/]");

configTable.AddRow("[grey]LLM Model[/]", $"[white]{llmModel}[/]");
configTable.AddRow("[grey]Chain[/]", "[white]Solana Devnet[/]");
configTable.AddRow("[grey]Framework[/]", "[white].NET 10[/]");
configTable.AddRow("[grey]Agents[/]", "[white]Scout → Strategist → Risk Manager → Executor[/]");
configTable.AddRow("[grey]Guardrails[/]", "[yellow]Hard (C#) + Policy (Velocity/Drawdown) + AI (LLM)[/]");
configTable.AddRow("[grey]Market Data[/]", "[green]LIVE — Real-time Solana RPC[/]");

AnsiConsole.Write(configTable);
AnsiConsole.Write(new Rule("[cyan]AGENT SWARM STARTING[/]") { Style = Style.Parse("cyan") });
AnsiConsole.WriteLine();

// ── Host ──────────────────────────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders(); // Remove default console logger — Spectre handles UI
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
        // Only our agents log at Information level
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddFilter("AutoSig", LogLevel.Information);
    })
    .ConfigureServices(services =>
    {
        // Application Layer (MediatR + Agents)
        services.AddApplicationServices();

        // Infrastructure: AI (OpenRouter)
        services.AddAiServices(openRouterApiKey, llmModel);

        // Infrastructure: Solana (Devnet RPC + Signer Enclave + Market Data)
        services.AddSolanaServices(solanaPrivateKey);

        // Console UI Agent (listens to all swarm events for rendering)
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.MarketOpportunityFoundEvent>, SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.ProposalGeneratedEvent>, SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.ProposalApprovedEvent>, SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.ProposalRejectedEvent>, SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.TransactionCompletedEvent>, SpectreUiAgent>();

        // Consensus Loop (BackgroundService)
        services.AddHostedService<ConsensusLoopService>();
    })
    .Build();

// ── Startup: Display wallet info ───────────────────────────────────────────────
var solana = host.Services.GetRequiredService<AutoSig.Domain.Interfaces.ISolanaService>();
var publicKey = solana.GetPublicKey();
var balance = await solana.GetBalanceLamportsAsync();

// ── Security Policy Display ──────────────────────────────────────────────────
var policy = new AutoSig.Domain.Models.TradingPolicy();
var securityTable = new Table()
    .Border(TableBorder.Simple)
    .BorderStyle(new Style(Color.Yellow))
    .AddColumn("[yellow]Security Policy[/]")
    .AddColumn("[white]Value[/]");

securityTable.AddRow("[yellow]Max Single Tx[/]", $"[white]{policy.MaxSingleTransactionLamports / 1e9:F2} SOL[/]");
securityTable.AddRow("[yellow]Max Trades/Hour[/]", $"[white]{policy.MaxTradesPerHour}[/]");
securityTable.AddRow("[yellow]Max Trades/Day[/]", $"[white]{policy.MaxTradesPerDay}[/]");
securityTable.AddRow("[yellow]Trade Cooldown[/]", $"[white]{policy.MinTimeBetweenTrades.TotalSeconds}s[/]");
securityTable.AddRow("[yellow]Max Daily Drawdown[/]", $"[white]{policy.MaxDailyDrawdownPercent:P0}[/]");
securityTable.AddRow("[yellow]Reserve Floor[/]", $"[white]{policy.MinReserveBalanceLamports / 1e9:F2} SOL[/]");

AnsiConsole.Write(new Panel(securityTable)
{
    Header = new PanelHeader("[bold yellow] 🛡️ ACTIVE TRADING POLICY [/]"),
    Border = BoxBorder.Rounded,
    BorderStyle = new Style(Color.Yellow),
    Padding = new Padding(1, 0)
});

// ── Wallet Info ──────────────────────────────────────────────────────────────
AnsiConsole.MarkupLine($"[grey]  Treasury   : [white]{publicKey}[/][/]");
AnsiConsole.MarkupLine($"[grey]  Balance    : [white]{balance / 1_000_000_000.0:F4} SOL ({balance:N0} lamports)[/][/]");

if (balance < 10_000_000) // Less than 0.01 SOL — auto-airdrop for demo
{
    try
    {
        AnsiConsole.MarkupLine("[yellow]  ⚡ Low balance detected — requesting Devnet airdrop...[/]");
        await solana.RequestAirdropAsync(1_000_000_000); // 1 SOL
        await Task.Delay(3000); // Wait for airdrop confirmation
        balance = await solana.GetBalanceLamportsAsync();
        AnsiConsole.MarkupLine($"[green]  ✅ Airdrop received. New balance: {balance / 1_000_000_000.0:F4} SOL[/]");
    }
    catch (Exception)
    {
        AnsiConsole.MarkupLine("\n[red]  ❌ Auto-airdrop failed (Devnet faucet is likely rate-limited or down).[/]");
        AnsiConsole.MarkupLine("[yellow]  Please manually fund your wallet.[/]");
        AnsiConsole.MarkupLine($"[grey]  1. Go to [link=https://faucet.solana.com/]https://faucet.solana.com/[/][/]");
        AnsiConsole.MarkupLine($"[grey]  2. Enter this address: [/][white]{publicKey}[/]");
        AnsiConsole.MarkupLine("[grey]  3. Claim Devnet SOL, then wait 5 seconds.[/]");
        AnsiConsole.MarkupLine("\n[yellow]  The agents will still plan trades, but the Executor will fail until funded.[/]\n");
    }
}

AnsiConsole.Write(new Rule("[green]SYSTEM READY — LIVE MARKET DATA MODE[/]") { Style = Style.Parse("green") });
AnsiConsole.WriteLine();

await host.RunAsync();
