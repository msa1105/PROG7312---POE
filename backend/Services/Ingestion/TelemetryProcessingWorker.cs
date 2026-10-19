using System.Diagnostics;
using SmartX.Api.Models;
using SmartX.Api.Services.Realtime;

namespace SmartX.Api.Services.Ingestion;

/// <summary>
/// Single consumer of the two-lane ingestion queue. Each dequeued packet is resolved to its device (O(1) dictionary
/// lookup), scored by the anomaly detector, appended to the device's sorted timeline, and turned into de-duplicated
/// alerts via the HashSet-backed <see cref="AlertStateTracker"/>.
/// Reference: Microsoft, 2026c. Background tasks with hosted services in ASP.NET Core. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services> [Accessed 19 October 2026].
/// Reference: Microsoft, 2025l. SpinWait.SpinUntil method (System.Threading). [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.spinwait.spinuntil> [Accessed 19 October 2026].
/// </summary>
public class TelemetryProcessingWorker : BackgroundService
{
    private readonly TelemetryIngestionQueue _queue;
    private readonly DeviceRegistryService _registry;
    private readonly AnomalyDetectionService _anomaly;
    private readonly AlertStateTracker _alerts;
    private readonly IDeviceChangeNotifier _broadcaster;
    private readonly ILogger<TelemetryProcessingWorker> _logger;

    public TelemetryProcessingWorker(TelemetryIngestionQueue queue, DeviceRegistryService registry, AnomalyDetectionService anomaly,
        AlertStateTracker alerts, IDeviceChangeNotifier broadcaster, ILogger<TelemetryProcessingWorker> logger)
    {
        _queue = queue;
        _registry = registry;
        _anomaly = anomaly;
        _alerts = alerts;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _queue.WaitForWorkAsync(stoppingToken);

            while (_queue.TryDequeue(out var envelope))
            {
                try
                {
                    Process(envelope);
                }
                catch (Exception ex)
                {
                    // A bad packet must never stop the pipeline
                    _logger.LogWarning(ex, "Dropped packet {PacketId} from {Mac}", envelope.Item.PacketId, envelope.Item.MacAddress);
                }

                SimulateProcessingCost(envelope);
            }
        }
    }

    public void Process(IngestionEnvelope envelope)
    {
        var item = envelope.Item;
        var device = _registry.GetDevice(item.MacAddress);
        if (device == null) return; // device deregistered while the packet was queued

        AnomalyEvaluation? evaluation = item.NumericValue is double value && device.Category != SensorCategory.Actuator
            ? _anomaly.Evaluate(device.MacAddress, value)
            : null;

        var snapshot = item.Apply(_registry, evaluation);

        if (evaluation?.IsAnomaly == true)
        {
            _alerts.TryRaise(new DeviceAlert
            {
                MacAddress = device.MacAddress,
                Kind = AlertKind.Anomaly,
                Severity = AlertSeverity.Warning,
                RaisedAt = DateTime.UtcNow,
                TriggerValue = item.NumericValue,
                BaselineMean = evaluation.BaselineMean,
                ZScore = evaluation.ZScore,
                Message = $"Outlier |Z| = {Math.Abs(evaluation.ZScore ?? 0):F2} exceeds {AnomalyDetectionService.ZScoreThreshold:F1}σ threshold"
            });
        }

        if (envelope.Classification is CriticalClassification critical)
        {
            _alerts.TryRaise(new DeviceAlert
            {
                MacAddress = device.MacAddress,
                Kind = AlertKind.ThresholdBreach,
                Severity = critical.Severity,
                RaisedAt = DateTime.UtcNow,
                TriggerValue = item.NumericValue,
                BaselineMean = evaluation?.BaselineMean ?? snapshot.RollingMean,
                ZScore = evaluation?.ZScore,
                Message = critical.Reason
            });
        }

        _broadcaster.MarkDeviceDirty(device.MacAddress);
    }

    /// <summary>
    /// Demo-only artificial cost for standard packets, so a backlog forms and the priority bypass becomes visible.
    /// A spin-wait is used because timer-based delays are too coarse (~15 ms) on Windows.
    /// </summary>
    private void SimulateProcessingCost(IngestionEnvelope envelope)
    {
        int cost = _queue.SimulatedProcessingCostMs;
        if (cost <= 0 || envelope.Lane != IngestionLane.Standard) return;

        var watch = Stopwatch.StartNew();
        SpinWait.SpinUntil(() => watch.ElapsedMilliseconds >= cost);
    }
}
