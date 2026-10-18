using SmartX.Api.Models;

namespace SmartX.Api.Services;

public record AlertTrackerStats(int ActiveAlerts, int DisconnectedNodes, long DuplicateAlertsSuppressed, long DuplicateDisconnectsSuppressed, long TotalRaised);

/// <summary>
/// Unique tracking of error states using hash sets.
/// - HashSet&lt;string&gt; of disconnected node MACs: a node that is already known to be offline is not re-reported.
/// - HashSet&lt;DeviceAlert&gt; keyed by (MAC, alert kind): Add() returns false for a duplicate, so a sensor that keeps
///   breaching a threshold every packet raises exactly one active alert until an operator acknowledges it.
/// Duplicates are rejected in O(1) before any further processing and counted for the dashboard.
/// Reference: HashSet<T> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1
/// Reference: IEqualityComparer<T> Interface (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iequalitycomparer-1
/// </summary>
public class AlertStateTracker
{
    private readonly HashSet<string> _disconnected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DeviceAlert> _active = new(AlertIdentityComparer.Instance);
    private readonly List<DeviceAlert> _log = new();
    private readonly object _lock = new();
    private long _duplicateAlertsSuppressed;
    private long _duplicateDisconnectsSuppressed;

    public event Action<DeviceAlert>? AlertRaised;

    /// <summary>Raises an alert unless one of the same kind is already active for the device.</summary>
    public bool TryRaise(DeviceAlert alert)
    {
        lock (_lock)
        {
            if (!_active.Add(alert))
            {
                _duplicateAlertsSuppressed++;
                return false;
            }
            _log.Add(alert);
            if (_log.Count > 1000) _log.RemoveAt(0);
        }

        AlertRaised?.Invoke(alert);
        return true;
    }

    public bool IsActive(string macAddress, AlertKind kind)
    {
        lock (_lock) return _active.Contains(new DeviceAlert { MacAddress = macAddress, Kind = kind });
    }

    /// <summary>Records a node as disconnected. Returns false if it was already known to be offline.</summary>
    public bool MarkDisconnected(string macAddress)
    {
        lock (_lock)
        {
            if (_disconnected.Add(macAddress)) return true;
            _duplicateDisconnectsSuppressed++;
            return false;
        }
    }

    /// <summary>Removes a node from the disconnected set when telemetry resumes. Returns true if it was offline.</summary>
    public bool MarkReconnected(string macAddress)
    {
        lock (_lock) return _disconnected.Remove(macAddress);
    }

    public bool IsDisconnected(string macAddress)
    {
        lock (_lock) return _disconnected.Contains(macAddress);
    }

    public List<string> GetDisconnected()
    {
        lock (_lock) return _disconnected.OrderBy(m => m, StringComparer.Ordinal).ToList();
    }

    public List<DeviceAlert> GetActiveAlerts(string? macAddress = null)
    {
        lock (_lock)
        {
            return _active
                .Where(a => macAddress == null || a.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.RaisedAt)
                .ToList();
        }
    }

    /// <summary>Set of MACs that currently have at least one active alert (used by the alert-state filter).</summary>
    public HashSet<string> GetAlertingDevices()
    {
        lock (_lock) return _active.Select(a => a.MacAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Operator acknowledgement (HITL triage) of every active alert on a node.</summary>
    public List<DeviceAlert> Acknowledge(string macAddress, string acknowledgedBy, string? notes)
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            var matching = _active.Where(a => a.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var alert in matching)
            {
                alert.AcknowledgedAt = now;
                alert.AcknowledgedBy = acknowledgedBy;
                alert.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
                _active.Remove(alert);
            }
            return matching;
        }
    }

    /// <summary>Mean-Time-To-Detect: mean delay between an alert being raised and an operator acknowledging it.</summary>
    public double? GetMeanTimeToDetectSeconds()
    {
        lock (_lock)
        {
            var samples = _log.Where(a => a.AcknowledgedAt.HasValue).Select(a => (a.AcknowledgedAt!.Value - a.RaisedAt).TotalSeconds).ToList();
            return samples.Count == 0 ? null : Math.Round(samples.Average(), 1);
        }
    }

    /// <summary>Number of alerts of a kind raised for a device since a point in time (pattern analysis input).</summary>
    public int CountRaisedSince(string macAddress, AlertKind kind, DateTime sinceUtc)
    {
        lock (_lock)
        {
            return _log.Count(a => a.Kind == kind && a.RaisedAt >= sinceUtc && a.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase));
        }
    }

    public AlertTrackerStats GetStats()
    {
        lock (_lock) return new AlertTrackerStats(_active.Count, _disconnected.Count, _duplicateAlertsSuppressed, _duplicateDisconnectsSuppressed, _log.Count);
    }

    public void Reset()
    {
        lock (_lock)
        {
            _disconnected.Clear();
            _active.Clear();
            _log.Clear();
            _duplicateAlertsSuppressed = 0;
            _duplicateDisconnectsSuppressed = 0;
        }
    }

    /// <summary>Two alerts are the same error state when they share device and kind.</summary>
    private sealed class AlertIdentityComparer : IEqualityComparer<DeviceAlert>
    {
        public static readonly AlertIdentityComparer Instance = new();

        public bool Equals(DeviceAlert? x, DeviceAlert? y) =>
            x is not null && y is not null && x.Kind == y.Kind && x.MacAddress.Equals(y.MacAddress, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(DeviceAlert alert) =>
            HashCode.Combine(alert.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(alert.MacAddress));
    }
}
