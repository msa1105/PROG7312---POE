using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace SmartX.Api.Services;

[JsonConverter(typeof(JsonStringEnumConverter<CriticalEventKind>))]
public enum CriticalEventKind
{
    MoistureCrash,
    PowerSurge
}

public record SpikeDirective(string MacAddress, double Value);
public record CriticalEventDirective(string MacAddress, CriticalEventKind Kind, double Value);
public record FloodDirective(string MacAddress, int Count);

/// <summary>Fault-injection instructions claimed by the external device simulator on each transmission window.</summary>
public record SimulationDirectives(
    List<string> DroppedMacs,
    List<SpikeDirective> Spikes,
    List<CriticalEventDirective> CriticalEvents,
    List<FloodDirective> Floods);

/// <summary>
/// Thread-safe fault-injection switches set by operators through the API and consumed by the device simulator,
/// which then sends the corresponding readings back to the API over HTTP like a real device would.
/// </summary>
public class SimulationControlService
{
    public const double MoistureCrashPercent = 9.5;
    public const double PowerSurgeWatts = 1850.0;

    // MacAddress -> sigma multiplier for a pending one-shot spike
    private readonly ConcurrentDictionary<string, double> _pendingSpikes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _droppedMacs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<CriticalEventDirective> _criticalEvents = new();
    private readonly ConcurrentQueue<FloodDirective> _floods = new();

    public void ScheduleSpike(string macAddress, double sigmaMultiplier) => _pendingSpikes[macAddress] = sigmaMultiplier;

    public void DropHeartbeat(string macAddress) => _droppedMacs[macAddress] = 0;

    public bool RestoreHeartbeat(string macAddress) => _droppedMacs.TryRemove(macAddress, out _);

    public bool IsHeartbeatDropped(string macAddress) => _droppedMacs.ContainsKey(macAddress);

    public CriticalEventDirective ScheduleCriticalEvent(string macAddress, CriticalEventKind kind)
    {
        var directive = new CriticalEventDirective(macAddress, kind, kind == CriticalEventKind.MoistureCrash ? MoistureCrashPercent : PowerSurgeWatts);
        _criticalEvents.Enqueue(directive);
        return directive;
    }

    public void ScheduleFlood(string macAddress, int count) => _floods.Enqueue(new FloodDirective(macAddress, count));

    /// <summary>
    /// Hands pending directives to the simulator. Spike values are resolved against each node's rolling baseline
    /// (mean + k·σ); spikes for nodes without a baseline yet stay pending.
    /// </summary>
    public SimulationDirectives Claim(Func<string, RollingBaseline?> baselineProvider)
    {
        var spikes = new List<SpikeDirective>();
        foreach (var mac in _pendingSpikes.Keys.ToList())
        {
            var baseline = baselineProvider(mac);
            if (baseline == null || !_pendingSpikes.TryRemove(mac, out var multiplier)) continue;
            spikes.Add(new SpikeDirective(mac, Math.Round(baseline.Mean + multiplier * Math.Max(baseline.StdDev, 0.5), 2)));
        }

        var critical = new List<CriticalEventDirective>();
        while (_criticalEvents.TryDequeue(out var e)) critical.Add(e);

        var floods = new List<FloodDirective>();
        while (_floods.TryDequeue(out var f)) floods.Add(f);

        return new SimulationDirectives(_droppedMacs.Keys.ToList(), spikes, critical, floods);
    }

    public void Reset()
    {
        _pendingSpikes.Clear();
        _droppedMacs.Clear();
        _criticalEvents.Clear();
        _floods.Clear();
    }
}
