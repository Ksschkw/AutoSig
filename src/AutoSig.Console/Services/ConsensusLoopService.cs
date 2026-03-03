using AutoSig.Application.Agents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoSig.Console.Services;

/// <summary>
/// The Consensus Loop  runs as a .NET BackgroundService (IHostedService).
/// Triggers the Scout Agent on a configurable interval, which chains the
/// entire Strategist  RiskManager  Executor pipeline via MediatR events.
/// </summary>
public sealed class ConsensusLoopService(
    ScoutAgent scout,
    ILogger<ConsensusLoopService> logger) : BackgroundService
{
    // How often the Scout scans for opportunities (adjustable for demo)
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[ConsensusLoop] AutoSig is ONLINE. Scanning every {Interval}s.", ScanInterval.TotalSeconds);

        // Give the host a moment to fully boot before the first scan
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scout.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ConsensusLoop] Unhandled error during scan cycle. Continuing...");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }

        logger.LogInformation("[ConsensusLoop] AutoSig is SHUTTING DOWN.");
    }
}
