using System.Collections.Concurrent;

namespace SmartX.Api.Services;

/// <summary>
/// Thread-safe simulation switches shared between API requests and the background telemetry worker.
/// </summary>
public class SimulationControlService
{
    // MacAddress -> sigma multiplier for a pending one-shot spike
    private readonly ConcurrentDictionary<string, double> _pendingSpikes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _droppedMacs = new(StringComparer.OrdinalIgnoreCase);

    public void ScheduleSpike(string macAddress, double sigmaMultiplier) => _pendingSpikes[macAddress] = sigmaMultiplier;

    /// <summary>Consumes a pending spike (one-shot).</summary>
    public bool TryTakeSpike(string macAddress, out double sigmaMultiplier) => _pendingSpikes.TryRemove(macAddress, out sigmaMultiplier);

    public void DropHeartbeat(string macAddress) => _droppedMacs[macAddress] = 0;

    public bool IsHeartbeatDropped(string macAddress) => _droppedMacs.ContainsKey(macAddress);

    public void Reset()
    {
        _pendingSpikes.Clear();
        _droppedMacs.Clear();
    }
}
