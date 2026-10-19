using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using SmartX.Api.Models;
using SmartX.Api.Services.Ingestion;
using SmartX.Api.Services.Recommendations;

namespace SmartX.Api.Services.Realtime;

/// <summary>Receives notifications that a device's dashboard view changed and must be re-published.</summary>
public interface IDeviceChangeNotifier
{
    void MarkDeviceDirty(string macAddress);
}

/// <summary>A notifier that ignores changes (used when no real-time clients exist, e.g. unit tests).</summary>
public sealed class NullDeviceChangeNotifier : IDeviceChangeNotifier
{
    public static readonly NullDeviceChangeNotifier Instance = new();
    public void MarkDeviceDirty(string macAddress) { }
}

/// <summary>
/// Coalescing real-time publisher. Devices touched by the processing pipeline are collected in a HashSet
/// (each device is sent at most once per 250 ms flush no matter how many packets it produced), command and
/// alert changes are pushed as they happen, and pipeline / recommendation snapshots are pushed periodically.
/// Reference: Microsoft, 2023. SignalR HubContext. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext> [Accessed 19 October 2026].
/// Reference: Microsoft, 2026c. Background tasks with hosted services in ASP.NET Core. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services> [Accessed 19 October 2026].
/// </summary>
public class GatewayBroadcaster : BackgroundService, IDeviceChangeNotifier
{
    private static readonly TimeSpan DeviceFlushInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PipelineInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RecommendationInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly IHubContext<GatewayHub> _hub;
    private readonly DeviceViewFactory _views;
    private readonly CommandDispatcher _commands;
    private readonly TelemetryIngestionQueue _queue;
    private readonly AlertStateTracker _alerts;
    private readonly RecommendationService _recommendations;
    private readonly ILogger<GatewayBroadcaster> _logger;

    private readonly HashSet<string> _dirtyDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private volatile bool _commandsDirty = true;
    private string? _lastRecommendationJson;

    public GatewayBroadcaster(IHubContext<GatewayHub> hub, DeviceViewFactory views, CommandDispatcher commands, TelemetryIngestionQueue queue,
        AlertStateTracker alerts, RecommendationService recommendations, ILogger<GatewayBroadcaster> logger)
    {
        _hub = hub;
        _views = views;
        _commands = commands;
        _queue = queue;
        _alerts = alerts;
        _recommendations = recommendations;
        _logger = logger;

        _commands.Changed += () => _commandsDirty = true;
        _alerts.AlertRaised += OnAlertRaised;
    }

    public void MarkDeviceDirty(string macAddress)
    {
        lock (_lock) _dirtyDevices.Add(macAddress);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(DeviceFlushInterval);
        var lastPipeline = DateTime.MinValue;
        var lastRecommendations = DateTime.MinValue;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var now = DateTime.UtcNow;
                await FlushDevicesAsync(now, stoppingToken);

                if (_commandsDirty)
                {
                    _commandsDirty = false;
                    await _hub.Clients.All.SendAsync("commandsUpdated", BuildCommandState(), stoppingToken);
                }

                if (now - lastPipeline >= PipelineInterval)
                {
                    lastPipeline = now;
                    await _hub.Clients.All.SendAsync("pipelineUpdated", BuildPipelineState(), stoppingToken);
                }

                if (now - lastRecommendations >= RecommendationInterval)
                {
                    lastRecommendations = now;
                    await PublishRecommendationsAsync(force: false, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Real-time broadcast failed; will retry on the next tick.");
            }
        }
    }

    /// <summary>Recomputes recommendations and pushes them when they changed (or when forced).</summary>
    public async Task<RecommendationSnapshot> PublishRecommendationsAsync(bool force, CancellationToken cancellationToken = default)
    {
        var snapshot = _recommendations.Evaluate();
        var signature = JsonSerializer.Serialize(new { snapshot.Suggestions, snapshot.Insights, snapshot.Conditions }, WebJson);
        if (force || signature != _lastRecommendationJson)
        {
            _lastRecommendationJson = signature;
            await _hub.Clients.All.SendAsync("recommendationsUpdated", snapshot, cancellationToken);
        }
        return snapshot;
    }

    public object BuildCommandState() => new { History = _commands.GetHistory(40), UndoStack = _commands.GetUndoStack() };

    public object BuildPipelineState() => new { Queue = _queue.GetStats(), Alerts = _alerts.GetStats(), DisconnectedNodes = _alerts.GetDisconnected() };

    private async Task FlushDevicesAsync(DateTime now, CancellationToken cancellationToken)
    {
        List<string> dirty;
        lock (_lock)
        {
            if (_dirtyDevices.Count == 0) return;
            dirty = _dirtyDevices.ToList();
            _dirtyDevices.Clear();
        }

        await _hub.Clients.All.SendAsync("devicesUpdated", _views.BuildFor(dirty, now), cancellationToken);
    }

    private void OnAlertRaised(DeviceAlert alert)
    {
        MarkDeviceDirty(alert.MacAddress);
        _ = _hub.Clients.All.SendAsync("alertRaised", alert);
    }
}
