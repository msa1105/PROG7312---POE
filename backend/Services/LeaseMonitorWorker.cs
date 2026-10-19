using SmartX.Api.Models;
using SmartX.Api.Services.Realtime;

namespace SmartX.Api.Services;

/// <summary>
/// Gateway-side heartbeat supervision. Every half second it checks each node's lease; a node whose lease expired is
/// added to the disconnected HashSet exactly once (repeat detections are rejected by the set) and raises a single
/// latched HeartbeatLost alert. Also expires manual commands that devices never acknowledged.
/// Reference: Microsoft, 2025g. PeriodicTimer class (System.Threading). [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer> [Accessed 19 October 2026].
/// </summary>
public class LeaseMonitorWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly DeviceRegistryService _registry;
    private readonly AlertStateTracker _alerts;
    private readonly CommandDispatcher _commands;
    private readonly IDeviceChangeNotifier _broadcaster;

    public LeaseMonitorWorker(DeviceRegistryService registry, AlertStateTracker alerts, CommandDispatcher commands, IDeviceChangeNotifier broadcaster)
    {
        _registry = registry;
        _alerts = alerts;
        _commands = commands;
        _broadcaster = broadcaster;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            CheckLeases(DateTime.UtcNow);
            _commands.ExpireStale(DateTime.UtcNow);
        }
    }

    public void CheckLeases(DateTime nowUtc)
    {
        foreach (var mac in _registry.GetExpiredLeases(nowUtc))
        {
            if (!_alerts.MarkDisconnected(mac)) continue; // already known offline: duplicate suppressed by the set

            _alerts.TryRaise(new DeviceAlert
            {
                MacAddress = mac,
                Kind = AlertKind.HeartbeatLost,
                Severity = AlertSeverity.High,
                RaisedAt = nowUtc,
                Message = $"No telemetry within {GatewayTiming.LeaseWindows} transmission windows ({GatewayTiming.LeaseDuration.TotalSeconds:F1}s lease)"
            });
            _broadcaster.MarkDeviceDirty(mac);
        }
    }
}
