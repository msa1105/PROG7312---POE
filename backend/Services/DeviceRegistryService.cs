namespace SmartX.Api.Services;

using SmartX.Api.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

public partial class DeviceRegistryService
{
    public const int HistoryCapacity = 60;

    private readonly ConcurrentDictionary<string, SensorDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DeviceRuntimeState> _runtime = new(StringComparer.OrdinalIgnoreCase);

    // Every alert ever raised (since last reset); used for Mean-Time-To-Detect benchmarking
    private readonly List<DeviceAlert> _alertLog = new();
    private readonly object _alertLock = new();

    private long _totalPacketsReceived;

    private sealed class DeviceRuntimeState
    {
        public readonly object Lock = new();
        public TelemetrySnapshot? Latest;
        public BoundedTelemetryCollection<TelemetrySnapshot> History = new(HistoryCapacity);
        public readonly Queue<DateTime> RecentArrivals = new();
        public DateTime? FirstReceivedAt;
        public DateTime? LastReceivedAt;
        public long PacketsReceived;
        public readonly Dictionary<AlertKind, DeviceAlert> ActiveAlerts = new();
    }

    [GeneratedRegex("^([0-9A-F]{2}:){5}[0-9A-F]{2}$")]
    private static partial Regex MacRegex();

    public static bool TryNormalizeMac(string? macAddress, out string normalized)
    {
        normalized = (macAddress ?? string.Empty).Trim().ToUpperInvariant();
        return MacRegex().IsMatch(normalized);
    }

    public long TotalPacketsReceived => Interlocked.Read(ref _totalPacketsReceived);

    public SensorDevice RegisterDevice(SensorDevice device)
    {
        if (!TryNormalizeMac(device.MacAddress, out var mac))
            throw new ArgumentException("MAC address must use XX:XX:XX:XX:XX:XX hex notation.", nameof(device));

        var incoming = new SensorDevice
        {
            MacAddress = mac,
            DeploymentLocation = device.DeploymentLocation.Trim(),
            Category = device.Category
            // Attachments are server-managed and never accepted from the client payload
        };

        return _devices.AddOrUpdate(mac, incoming, (key, existing) =>
        {
            existing.DeploymentLocation = incoming.DeploymentLocation;
            existing.Category = incoming.Category;
            return existing;
        });
    }

    /// <summary>
    /// Adds an attachment, replacing any existing attachment with the same file name.
    /// Returns the replaced attachment (if any) so the caller can remove its blob.
    /// </summary>
    public DeviceAttachment? AddAttachment(string macAddress, DeviceAttachment attachment)
    {
        if (!_devices.TryGetValue(macAddress, out var device))
            throw new KeyNotFoundException($"Device '{macAddress}' is not registered.");

        lock (device.Attachments)
        {
            var existing = device.Attachments.FirstOrDefault(a => a.FileName.Equals(attachment.FileName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) device.Attachments.Remove(existing);
            device.Attachments.Add(attachment);
            return existing;
        }
    }

    public DeviceAttachment? GetAttachment(string macAddress, string fileName)
    {
        if (!_devices.TryGetValue(macAddress, out var device)) return null;
        lock (device.Attachments)
        {
            return device.Attachments.FirstOrDefault(a => a.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public SensorDevice? GetDevice(string macAddress)
    {
        _devices.TryGetValue(macAddress, out var device);
        return device;
    }

    public IEnumerable<SensorDevice> GetAllDevices()
    {
        return _devices.Values.OrderBy(d => d.MacAddress, StringComparer.Ordinal);
    }

    /// <summary>
    /// Records an ingested packet: stores the latest snapshot and history, updates delivery
    /// counters, and latches an anomaly alert when the evaluation flags an outlier.
    /// </summary>
    public TelemetrySnapshot RecordTelemetry<T>(TelemetryPacket<T> packet, AnomalyEvaluation? evaluation = null)
    {
        var now = DateTime.UtcNow;
        var snapshot = new TelemetrySnapshot(
            PacketId: packet.PacketId,
            MacAddress: packet.MacAddress,
            Timestamp: packet.Timestamp,
            Value: ProjectValue(packet.Value),
            MetricUnit: packet.MetricUnit,
            PayloadType: PayloadTypeName<T>(),
            IsAnomaly: evaluation?.IsAnomaly ?? false,
            ZScore: evaluation?.ZScore is double z ? Math.Round(z, 2) : null,
            RollingMean: evaluation?.BaselineMean is double m ? Math.Round(m, 2) : null,
            RollingStdDev: evaluation?.BaselineStdDev is double s ? Math.Round(s, 2) : null,
            SignalStrengthDbm: packet.SignalStrengthDbm,
            BatteryPercent: packet.BatteryPercent
        );

        var state = _runtime.GetOrAdd(packet.MacAddress, _ => new DeviceRuntimeState());
        lock (state.Lock)
        {
            state.Latest = snapshot;
            state.History.Add(snapshot);
            state.FirstReceivedAt ??= now;
            state.LastReceivedAt = now;
            state.PacketsReceived++;
            state.RecentArrivals.Enqueue(now);
            PruneArrivals(state, now);

            if (evaluation?.IsAnomaly == true && !state.ActiveAlerts.ContainsKey(AlertKind.Anomaly))
            {
                var value = snapshot.Value is double d ? d : (double?)null;
                RaiseAlert(state, new DeviceAlert
                {
                    MacAddress = packet.MacAddress,
                    Kind = AlertKind.Anomaly,
                    RaisedAt = now,
                    TriggerValue = value,
                    BaselineMean = evaluation.BaselineMean,
                    ZScore = evaluation.ZScore,
                    Message = $"Outlier |Z| = {Math.Abs(evaluation.ZScore ?? 0):F2} exceeds {AnomalyDetectionService.ZScoreThreshold:F1}σ threshold"
                });
            }
        }

        Interlocked.Increment(ref _totalPacketsReceived);
        return snapshot;
    }

    /// <summary>
    /// Raises a latched HeartbeatLost alert for every node whose lease (two transmission windows) has expired.
    /// </summary>
    public void CheckHeartbeatLeases(DateTime nowUtc)
    {
        foreach (var (mac, state) in _runtime)
        {
            lock (state.Lock)
            {
                if (state.LastReceivedAt is not DateTime last) continue;
                if (nowUtc - last <= GatewayTiming.LeaseDuration) continue;
                if (state.ActiveAlerts.ContainsKey(AlertKind.HeartbeatLost)) continue;

                RaiseAlert(state, new DeviceAlert
                {
                    MacAddress = mac,
                    Kind = AlertKind.HeartbeatLost,
                    RaisedAt = nowUtc,
                    Message = $"No telemetry within {GatewayTiming.LeaseWindows} transmission windows ({GatewayTiming.LeaseDuration.TotalSeconds:F1}s lease)"
                });
            }
        }
    }

    public TelemetrySnapshot? GetLatestTelemetry(string macAddress)
    {
        if (!_runtime.TryGetValue(macAddress, out var state)) return null;
        lock (state.Lock) return state.Latest;
    }

    public List<TelemetrySnapshot> GetHistory(string macAddress)
    {
        if (!_runtime.TryGetValue(macAddress, out var state)) return new();
        BoundedTelemetryCollection<TelemetrySnapshot> history;
        lock (state.Lock) history = state.History;
        return history.ToList();
    }

    public DeviceConnectionStatus GetStatus(string macAddress, DateTime nowUtc)
    {
        if (!_runtime.TryGetValue(macAddress, out var state))
            return new DeviceConnectionStatus(false, null, GatewayTiming.LeaseDuration.TotalSeconds, 0, null);

        lock (state.Lock)
        {
            PruneArrivals(state, nowUtc);
            double? sinceLast = state.LastReceivedAt is DateTime last ? Math.Max(0, (nowUtc - last).TotalSeconds) : null;
            bool isOnline = sinceLast.HasValue && sinceLast.Value <= GatewayTiming.LeaseDuration.TotalSeconds;

            return new DeviceConnectionStatus(
                IsOnline: isOnline,
                SecondsSinceLastPacket: sinceLast.HasValue ? Math.Round(sinceLast.Value, 1) : null,
                LeaseSeconds: GatewayTiming.LeaseDuration.TotalSeconds,
                PacketsReceived: state.PacketsReceived,
                PacketDeliveryPercent: ComputeDeliveryPercent(state, nowUtc)
            );
        }
    }

    public List<DeviceAlert> GetActiveAlerts(string macAddress)
    {
        if (!_runtime.TryGetValue(macAddress, out var state)) return new();
        lock (state.Lock) return state.ActiveAlerts.Values.OrderByDescending(a => a.RaisedAt).ToList();
    }

    public int CountActiveAlerts()
    {
        int total = 0;
        foreach (var state in _runtime.Values)
        {
            lock (state.Lock) total += state.ActiveAlerts.Count;
        }
        return total;
    }

    /// <summary>
    /// Operator acknowledgement (HITL triage) of every active alert on a node.
    /// </summary>
    public List<DeviceAlert> AcknowledgeAlerts(string macAddress, string acknowledgedBy, string? notes)
    {
        if (!_runtime.TryGetValue(macAddress, out var state)) return new();

        var now = DateTime.UtcNow;
        lock (state.Lock)
        {
            var acknowledged = state.ActiveAlerts.Values.ToList();
            foreach (var alert in acknowledged)
            {
                alert.AcknowledgedAt = now;
                alert.AcknowledgedBy = acknowledgedBy;
                alert.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            }
            state.ActiveAlerts.Clear();
            return acknowledged;
        }
    }

    /// <summary>
    /// Mean-Time-To-Detect: mean delay between the gateway raising an alert and an operator acknowledging it.
    /// </summary>
    public double? GetMeanTimeToDetectSeconds()
    {
        lock (_alertLock)
        {
            var samples = _alertLog
                .Where(a => a.AcknowledgedAt.HasValue)
                .Select(a => (a.AcknowledgedAt!.Value - a.RaisedAt).TotalSeconds)
                .ToList();
            return samples.Count == 0 ? null : Math.Round(samples.Average(), 1);
        }
    }

    /// <summary>
    /// Restarts a node's ingestion buffer: clears its reading history and delivery counters.
    /// </summary>
    public void RestartIngestion(string macAddress)
    {
        if (!_runtime.TryGetValue(macAddress, out var state)) return;
        lock (state.Lock)
        {
            state.History = new BoundedTelemetryCollection<TelemetrySnapshot>(HistoryCapacity);
            state.RecentArrivals.Clear();
            state.PacketsReceived = 0;
            state.FirstReceivedAt = null;
        }
    }

    /// <summary>
    /// Clears all runtime telemetry state (latest packets, history, counters and alerts).
    /// </summary>
    public void ResetRuntimeState()
    {
        _runtime.Clear();
        lock (_alertLock) _alertLog.Clear();
        Interlocked.Exchange(ref _totalPacketsReceived, 0);
    }

    private void RaiseAlert(DeviceRuntimeState state, DeviceAlert alert)
    {
        state.ActiveAlerts[alert.Kind] = alert;
        lock (_alertLock) _alertLog.Add(alert);
    }

    private static void PruneArrivals(DeviceRuntimeState state, DateTime nowUtc)
    {
        var cutoff = nowUtc - GatewayTiming.DeliveryWindow;
        while (state.RecentArrivals.Count > 0 && state.RecentArrivals.Peek() < cutoff)
            state.RecentArrivals.Dequeue();
    }

    /// <summary>
    /// Packet-delivery success score over a sliding window: received packets / expected transmissions.
    /// One window of tolerance absorbs phase jitter between the sender and the measurement window.
    /// </summary>
    private static double? ComputeDeliveryPercent(DeviceRuntimeState state, DateTime nowUtc)
    {
        if (state.FirstReceivedAt is not DateTime first) return null;

        var observed = nowUtc - first;
        if (observed > GatewayTiming.DeliveryWindow) observed = GatewayTiming.DeliveryWindow;

        int expected = (int)Math.Floor(observed / GatewayTiming.TransmissionInterval) - 1;
        if (expected <= 0) return state.LastReceivedAt.HasValue ? 100.0 : null;

        return Math.Round(Math.Min(100.0, state.RecentArrivals.Count * 100.0 / expected), 1);
    }

    private static object? ProjectValue<T>(T value) => value switch
    {
        PowerMetric p => p.Watts,
        bool b => b,
        float f => (double)f,
        double d => d,
        int i => (double)i,
        _ => value
    };

    private static string PayloadTypeName<T>() => typeof(T) switch
    {
        var t when t == typeof(float) => "float",
        var t when t == typeof(double) => "double",
        var t when t == typeof(int) => "int",
        var t when t == typeof(bool) => "bool",
        var t => t.Name
    };
}
