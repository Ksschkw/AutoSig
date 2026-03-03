using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AutoSig.Console.UI;

// ─────────────────────────────────────────────────────────────────────────────
// Output levels:
//  brief   -- Only the Spectre panel summaries from SpectreUiAgent (no step logs)
//  normal  -- Key entry/exit events per agent (start, LLM response, outcome)
//  verbose -- Every step logged by every agent  [DEFAULT]
//  debug   -- Everything including LLM attempt internals, raw JSON previews, etc.
// ─────────────────────────────────────────────────────────────────────────────

public enum OutputLevel { Brief, Normal, Verbose, Debug }

internal sealed class SpectreConsoleLogger(string categoryName, OutputLevel level) : ILogger
{
    // ── Agent label + color ───────────────────────────────────────────────────
    private (string label, Color color) ResolveAgent(string message)
    {
        if (message.Contains("[Scout]"))          return ("SCOUT         ", Color.Cyan1);
        if (message.Contains("[Strategist]"))     return ("STRATEGIST    ", Color.Yellow);
        if (message.Contains("[RiskManager]"))    return ("RISK MANAGER  ", Color.Orange1);
        if (message.Contains("[Executor]"))       return ("EXECUTOR      ", Color.Magenta1);
        if (message.Contains("[LLM]"))            return ("LLM           ", Color.SteelBlue1);
        if (message.Contains("[MarketData]"))     return ("MARKET DATA   ", Color.Grey);
        if (message.Contains("[ConsensusLoop]"))  return ("CONSENSUS     ", Color.White);
        if (message.Contains("[Solana]") ||
            categoryName.Contains("Solana"))      return ("SOLANA        ", Color.Green);
        return ("SYSTEM        ", Color.Grey);
    }

    // ── Decide whether to print a given message at the current output level ──
    private bool ShouldShow(string message)
    {
        return level switch
        {
            OutputLevel.Brief   => false, // brief = panels only, suppress all step logs
            OutputLevel.Normal  => IsKeyEvent(message),
            OutputLevel.Verbose => !IsDebugNoise(message),
            OutputLevel.Debug   => true,
            _                   => true
        };
    }

    // Key events: one line per major phase transition per agent
    private static bool IsKeyEvent(string msg) =>
        msg.Contains("SCAN STARTED")            ||
        msg.Contains("Market data received")     ||
        msg.Contains("Opportunity identified")   ||
        msg.Contains("REASONING STARTED")        ||
        msg.Contains("Calling DeepSeek")         ||
        msg.Contains("Calling OpenRouter")        ||
        msg.Contains("HTTP 200")                 ||
        msg.Contains("HTTP 4")                   ||  // 404, 429, etc
        msg.Contains("HTTP 5")                   ||  // 500s
        msg.Contains("Proposal built")           ||
        msg.Contains("EVALUATING PROPOSAL")      ||
        msg.Contains("Phase 1 PASSED")           ||
        msg.Contains("Phase 2 PASSED")           ||
        msg.Contains("Phase 3")                  ||
        msg.Contains("CONSENSUS REACHED")        ||
        msg.Contains("Signing with treasury")    ||
        msg.Contains("Transaction CONFIRMED")    ||
        msg.Contains("Transaction FAILED")       ||
        msg.Contains("RPC rejected")             ||
        msg.Contains("REJECTED")                 ||
        msg.Contains("Heuristic score")          ||
        msg.Contains("ONLINE")                   ||
        msg.Contains("WARN")                     ||
        msg.Contains("ERR");

    // Debug noise: internal LLM polling details only useful when debugging
    private static bool IsDebugNoise(string msg) =>
        msg.Contains("Attempt ")                 ||
        msg.Contains("system=")                  ||
        msg.Contains("Raw JSON")                 ||
        msg.Contains("chars).")                  ||
        msg.Contains("Provider ready");

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message)) return;

        // Always show warnings and errors regardless of level
        if (logLevel < LogLevel.Warning && !ShouldShow(message)) return;

        var (label, color) = ResolveAgent(message);
        var ts             = DateTime.UtcNow.ToString("HH:mm:ss");

        // Strip the [AgentPrefix] token — it's already captured in the label
        var cleanMsg = System.Text.RegularExpressions.Regex
            .Replace(message, @"\[\w+\]", "")
            .Trim();
        if (string.IsNullOrWhiteSpace(cleanMsg)) cleanMsg = message.Trim();

        var (levelTag, levelColor) = logLevel switch
        {
            LogLevel.Warning  => ("WARN", Color.Yellow),
            LogLevel.Error    => ("ERR ", Color.Red),
            LogLevel.Critical => ("CRIT", Color.Red),
            _                 => ("    ", color)
        };

        AnsiConsole.MarkupLine(
            $"[grey]{ts}[/]  [{color.ToMarkup()}]{label}[/]  [{levelColor.ToMarkup()}]{levelTag}[/]  {Markup.Escape(cleanMsg)}");

        if (exception is not null && level == OutputLevel.Debug)
        {
            AnsiConsole.MarkupLine(
                $"[red]         {Markup.Escape(exception.GetType().Name)}: {Markup.Escape(exception.Message)}[/]");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Provider
// ─────────────────────────────────────────────────────────────────────────────

[ProviderAlias("SpectreConsole")]
internal sealed class SpectreConsoleLoggerProvider(OutputLevel level) : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SpectreConsoleLogger>
        _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new SpectreConsoleLogger(name, level));

    public void Dispose() => _loggers.Clear();

    // ── Factory ───────────────────────────────────────────────────────────────
    public static OutputLevel ParseLevel(string? raw) => (raw ?? "normal").ToLowerInvariant() switch
    {
        "brief"   => OutputLevel.Brief,
        "normal"  => OutputLevel.Normal,
        "verbose" => OutputLevel.Verbose,
        "debug"   => OutputLevel.Debug,
        _         => OutputLevel.Normal
    };
}
