using AutoSig.Application;
using AutoSig.Console.Services;
using AutoSig.Console.UI;
using AutoSig.Domain.Models;
using AutoSig.Infrastructure.AI;
using AutoSig.Infrastructure.Solana;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

//  Banner 
AnsiConsole.Write(new FigletText("AutoSig").Color(Color.Cyan1).Centered());
AnsiConsole.Write(new Markup("[bold cyan]  Autonomous Multi-Agent Treasury on Solana Devnet[/]\n").Centered());
AnsiConsole.Write(new Markup("[grey]  Superteam Nigeria DeFi Developer Challenge | Built with .NET 10[/]\n\n").Centered());
AnsiConsole.Write(new Rule("[cyan]SYSTEM BOOT[/]") { Style = Style.Parse("cyan") });

//  Environment 
AnsiConsole.MarkupLine("[grey]  Loading .env file...[/]");
DotNetEnv.Env.Load();
AnsiConsole.MarkupLine("[grey]  Environment loaded.[/]");

//  Key Generation Utility 
if (args.Contains("--generate-key"))
{
    var newWallet = new Solnet.Wallet.Wallet(Solnet.Wallet.Bip39.WordCount.Twelve, Solnet.Wallet.Bip39.WordList.English);
    var pubKey = newWallet.Account.PublicKey.Key;
    var privKeyBase64 = Convert.ToBase64String(newWallet.Account.PrivateKey.KeyBytes);

    AnsiConsole.MarkupLine("\n[green]New Solana Wallet Generated![/]");
    AnsiConsole.MarkupLine($"[grey]Public Key:[/] [white]{pubKey}[/]");
    AnsiConsole.MarkupLine($"[grey]Private Key (Base64):[/] [white]{privKeyBase64}[/]");
    AnsiConsole.MarkupLine("\n[yellow]Add the following to your .env file:[/]");
    AnsiConsole.MarkupLine($"AUTOSIG_SOLANA_PRIVATE_KEY={privKeyBase64}\n");
    return;
}

//  Required Secrets 
AnsiConsole.MarkupLine("[grey]  Reading secrets from environment...[/]");

var openRouterApiKey = Environment.GetEnvironmentVariable("AUTOSIG_OPENROUTER_KEY");
if (string.IsNullOrWhiteSpace(openRouterApiKey))
{
    AnsiConsole.MarkupLine("[red]  MISSING: AUTOSIG_OPENROUTER_KEY -- get a free key at https://openrouter.ai[/]");
    throw new InvalidOperationException("Missing env var: AUTOSIG_OPENROUTER_KEY");
}
AnsiConsole.MarkupLine("[grey]  AUTOSIG_OPENROUTER_KEY loaded (****" + openRouterApiKey[^6..] + ").[/]");

var solanaPrivateKey = Environment.GetEnvironmentVariable("AUTOSIG_SOLANA_PRIVATE_KEY");
if (string.IsNullOrWhiteSpace(solanaPrivateKey))
{
    AnsiConsole.MarkupLine("[red]  MISSING: AUTOSIG_SOLANA_PRIVATE_KEY -- run with --generate-key to create one[/]");
    throw new InvalidOperationException("Missing env var: AUTOSIG_SOLANA_PRIVATE_KEY");
}
AnsiConsole.MarkupLine("[grey]  AUTOSIG_SOLANA_PRIVATE_KEY loaded (" + solanaPrivateKey.Length + " chars).[/]");

//  Per-Agent LLM Models 
// Each agent uses its own model -- see .env.example for options.
// Strategist: deep reasoning for trade proposals (DeepSeek R1)
// Risk Manager: fast efficient evaluation (DeepSeek R1-Distill-Qwen-32B)
var strategistModel = Environment.GetEnvironmentVariable("AUTOSIG_STRATEGIST_MODEL")
    ?? "deepseek/deepseek-r1:free";
var riskModel = Environment.GetEnvironmentVariable("AUTOSIG_RISK_MODEL")
    ?? "deepseek/deepseek-r1-distill-qwen-32b:free";

AnsiConsole.MarkupLine($"[grey]  Strategist model  : [white]{strategistModel}[/][/]");
AnsiConsole.MarkupLine($"[grey]  Risk Manager model: [white]{riskModel}[/][/]");

// ── Output Verbosity ─────────────────────────────────────────────────────────
var outputLevel = AutoSig.Console.UI.SpectreConsoleLoggerProvider.ParseLevel(
    Environment.GetEnvironmentVariable("AUTOSIG_OUTPUT_LEVEL"));
AnsiConsole.MarkupLine($"[grey]  Output level      : [white]{outputLevel}[/]  (brief | normal | verbose | debug)[/]");

//  Trading Policy 
AnsiConsole.MarkupLine("[grey]  Building trading policy from environment...[/]");

static ulong EnvUlong(string key, ulong def) =>
    ulong.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
static int EnvInt(string key, int def) =>
    int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
static double EnvDouble(string key, double def) =>
    double.TryParse(Environment.GetEnvironmentVariable(key),
        System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

var policy = new TradingPolicy
{
    MaxSingleTransactionLamports = EnvUlong ("AUTOSIG_MAX_TX_LAMPORTS",           500_000_000),
    MinReserveBalanceLamports    = EnvUlong ("AUTOSIG_MIN_RESERVE_LAMPORTS",        50_000_000),
    MaxTradesPerHour             = EnvInt   ("AUTOSIG_MAX_TRADES_PER_HOUR",                 10),
    MinTimeBetweenTrades         = TimeSpan.FromSeconds(EnvInt("AUTOSIG_TRADE_COOLDOWN_SECONDS", 15)),
    MaxDailyDrawdownPercent      = EnvDouble("AUTOSIG_MAX_DAILY_DRAWDOWN",                0.05),
};

AnsiConsole.MarkupLine(
    $"[grey]  Policy: MaxTx={policy.MaxSingleTransactionLamports / 1e9:F2} SOL | " +
    $"Reserve={policy.MinReserveBalanceLamports / 1e9:F2} SOL | " +
    $"MaxTrades/hr={policy.MaxTradesPerHour} | Cooldown={policy.MinTimeBetweenTrades.TotalSeconds}s | " +
    $"MaxDrawdown={policy.MaxDailyDrawdownPercent:P0}[/]");

//  System Info Table 
var configTable = new Table()
    .Border(TableBorder.Simple)
    .BorderStyle(new Style(Color.Grey))
    .AddColumn("[grey]Component[/]")
    .AddColumn("[white]Configuration[/]");

configTable.AddRow("[grey]Strategist LLM[/]",   $"[white]{strategistModel}[/]");
configTable.AddRow("[grey]Risk Mgr LLM[/]",      $"[white]{riskModel}[/]");
configTable.AddRow("[grey]Chain[/]",              "[white]Solana Devnet[/]");
configTable.AddRow("[grey]Framework[/]",          "[white].NET 10[/]");
configTable.AddRow("[grey]Agents[/]",             "[white]Scout -> Strategist -> Risk Manager -> Executor[/]");
configTable.AddRow("[grey]Guardrails[/]",         "[yellow]Hard (C#) + Policy (Velocity/Drawdown) + AI (LLM)[/]");
configTable.AddRow("[grey]Market Data[/]",        "[green]LIVE -- Solana RPC + Binance Price Oracle[/]");

AnsiConsole.Write(configTable);
AnsiConsole.Write(new Rule("[cyan]AGENT SWARM STARTING[/]") { Style = Style.Parse("cyan") });

//  DI + Host 
AnsiConsole.MarkupLine("[grey]  Configuring DI container...[/]");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddProvider(new AutoSig.Console.UI.SpectreConsoleLoggerProvider(outputLevel));
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System",    LogLevel.Warning);
        logging.AddFilter("AutoSig",   LogLevel.Information);
    })
    .ConfigureServices(services =>
    {
        services.AddApplicationServices(policy);
        services.AddAiServices(openRouterApiKey, strategistModel, riskModel);
        services.AddSolanaServices(solanaPrivateKey);

        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.MarketOpportunityFoundEvent>, SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.ProposalGeneratedEvent>,     SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.ProposalApprovedEvent>,      SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.ProposalRejectedEvent>,      SpectreUiAgent>();
        services.AddTransient<INotificationHandler<AutoSig.Domain.Events.TransactionCompletedEvent>,  SpectreUiAgent>();

        services.AddHostedService<ConsensusLoopService>();
    })
    .Build();

AnsiConsole.MarkupLine("[grey]  DI container built.[/]");

//  Treasury Info 
AnsiConsole.MarkupLine("[grey]  Connecting to Solana Devnet and resolving treasury address...[/]");
var solana    = host.Services.GetRequiredService<AutoSig.Domain.Interfaces.ISolanaService>();
var publicKey = solana.GetPublicKey();
var balance   = await solana.GetBalanceLamportsAsync();
AnsiConsole.MarkupLine($"[grey]  Treasury: [white]{publicKey}[/][/]");
AnsiConsole.MarkupLine($"[grey]  Balance : [white]{balance / 1_000_000_000.0:F4} SOL[/][/]");

//  Security Policy Panel 
var policyTable = new Table()
    .Border(TableBorder.Simple)
    .BorderStyle(new Style(Color.Yellow))
    .AddColumn("[yellow]Security Policy[/]")
    .AddColumn("[white]Value[/]");

policyTable.AddRow("[yellow]Max Single Tx[/]",      $"[white]{policy.MaxSingleTransactionLamports / 1e9:F2} SOL[/]");
policyTable.AddRow("[yellow]Max Trades/Hour[/]",    $"[white]{policy.MaxTradesPerHour}[/]");
policyTable.AddRow("[yellow]Max Trades/Day[/]",     $"[white]{policy.MaxTradesPerDay}[/]");
policyTable.AddRow("[yellow]Trade Cooldown[/]",     $"[white]{policy.MinTimeBetweenTrades.TotalSeconds}s[/]");
policyTable.AddRow("[yellow]Max Daily Drawdown[/]", $"[white]{policy.MaxDailyDrawdownPercent:P0}[/]");
policyTable.AddRow("[yellow]Reserve Floor[/]",      $"[white]{policy.MinReserveBalanceLamports / 1e9:F2} SOL[/]");

AnsiConsole.Write(new Panel(policyTable)
{
    Header = new PanelHeader("[bold yellow] ACTIVE TRADING POLICY [/]"),
    Border = BoxBorder.Rounded,
    BorderStyle = new Style(Color.Yellow),
    Padding = new Padding(1, 0)
});

AnsiConsole.MarkupLine($"[grey]  Treasury : [white]{publicKey}[/][/]");
AnsiConsole.MarkupLine($"[grey]  Balance  : [white]{balance / 1_000_000_000.0:F4} SOL ({balance:N0} lamports)[/][/]");

//  Auto-Airdrop 
if (balance < 10_000_000)
{
    try
    {
        AnsiConsole.MarkupLine("[yellow]  Balance < 0.01 SOL -- requesting Devnet airdrop...[/]");
        AnsiConsole.MarkupLine("[grey]  Calling Solana Devnet faucet for 1 SOL...[/]");
        await solana.RequestAirdropAsync(1_000_000_000);
        AnsiConsole.MarkupLine("[grey]  Waiting 3s for on-chain confirmation...[/]");
        await Task.Delay(3000);
        balance = await solana.GetBalanceLamportsAsync();
        AnsiConsole.MarkupLine($"[green]  Airdrop confirmed. New balance: {balance / 1_000_000_000.0:F4} SOL[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"\n[red]  Airdrop failed: {ex.Message}[/]");
        AnsiConsole.MarkupLine("[yellow]  Manually fund via https://faucet.solana.com[/]");
        AnsiConsole.MarkupLine($"[grey]  Address: [/][white]{publicKey}[/]");
        AnsiConsole.MarkupLine("[yellow]  Executor will fail until funded.[/]\n");
    }
}

AnsiConsole.Write(new Rule("[green]SYSTEM READY -- LIVE MARKET DATA MODE[/]") { Style = Style.Parse("green") });
AnsiConsole.MarkupLine("[grey]  Launching autonomous agent swarm...[/]\n");

await host.RunAsync();
