using AutoSig.Domain.Models;
using System.Collections.Concurrent;

namespace AutoSig.Application;

/// <summary>
/// Thread-safe singleton that tracks trade velocity AND daily drawdown state.
/// Shared by both ScoutAgent (pre-check) and RiskManagerAgent (record on approval)
/// so the Scout can skip the expensive LLM pipeline when the hourly limit is already hit.
/// Drawdown state lives here (not in RiskManager) because RiskManager is Transient.
/// </summary>
public sealed class VelocityTracker
{
    private readonly TradingPolicy _policy;
    private readonly ConcurrentQueue<DateTime> _hourlyTimestamps = new();
    private DateTime _lastTradeTime = DateTime.MinValue;

    // ── Daily drawdown tracking ───────────────────────────────────────────────
    private ulong _dailyStartingBalance;
    private DateTime _dailyResetTime = DateTime.UtcNow;

    public VelocityTracker(TradingPolicy policy) => _policy = policy;

    // ── Velocity API ─────────────────────────────────────────────────────────

    /// <summary>Returns true if the hourly trade limit has already been reached.</summary>
    public bool IsHourlyLimitReached()
    {
        Prune();
        return _hourlyTimestamps.Count >= _policy.MaxTradesPerHour;
    }

    /// <summary>Returns true if the minimum cooldown between trades is still active.</summary>
    public bool IsCooldownActive() =>
        _lastTradeTime != DateTime.MinValue &&
        DateTime.UtcNow - _lastTradeTime < _policy.MinTimeBetweenTrades;

    /// <summary>How many trades have been recorded in the last rolling hour.</summary>
    public int TradesInLastHour()
    {
        Prune();
        return _hourlyTimestamps.Count;
    }

    /// <summary>Record a successfully approved trade. Called by RiskManagerAgent.</summary>
    public void RecordTrade()
    {
        _hourlyTimestamps.Enqueue(DateTime.UtcNow);
        _lastTradeTime = DateTime.UtcNow;
    }

    // ── Drawdown API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the daily starting balance, resetting the window every 24h.
    /// Pass the current on-chain balance; if the window just reset, it becomes the new baseline.
    /// </summary>
    public ulong GetOrResetDailyStartingBalance(ulong currentBalance)
    {
        if (DateTime.UtcNow - _dailyResetTime > TimeSpan.FromHours(24))
        {
            _dailyStartingBalance = currentBalance;
            _dailyResetTime = DateTime.UtcNow;
        }
        if (_dailyStartingBalance == 0)
            _dailyStartingBalance = currentBalance;
        return _dailyStartingBalance;
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    /// <summary>Evict timestamps older than one rolling hour.</summary>
    private void Prune()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        while (_hourlyTimestamps.TryPeek(out var oldest) && oldest < cutoff)
            _hourlyTimestamps.TryDequeue(out _);
    }
}
