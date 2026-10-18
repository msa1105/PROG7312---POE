namespace SmartX.Api.Services;

using SmartX.Api.Models;
using System.Text.RegularExpressions;

/// <summary>
/// Live device registry. Devices are held in a Dictionary&lt;string, DeviceRecord&gt; keyed by the normalised
/// (upper-case, colon-separated) MAC address, so every incoming packet resolves its device in O(1).
/// Each record owns a SortedDictionary-backed <see cref="TelemetryTimeline"/> of its historical readings.
/// Reference: Dictionary<TKey,TValue> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2
/// Reference: StringComparer.OrdinalIgnoreCase (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.stringcomparer.ordinalignorecase
/// </summary>
public partial class DeviceRegistryService
{
    public const int TimelineCapacity = 3600; // ~90 minutes at one reading per 1.5 s transmission window (bulk backlogs included)

    private readonly Dictionary<string, DeviceRecord> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private long _totalPacketsReceived;

    private sealed class DeviceRecord
    {
        public required SensorDevice Device { get; init; }
        public TelemetryTimeline Timeline { get; set; } = new(TimelineCapacity);
        public readonly Queue<DateTime> RecentArrivals = new();
        public DateTime? FirstReceivedAt;
        public DateTime? LastReceivedAt;
        public long PacketsReceived;
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

        lock (_lock)
        {
            if (_devices.TryGetValue(mac, out var existing))
            {
                existing.Device.DeploymentLocation = device.DeploymentLocation.Trim();
                existing.Device.Category = device.Category;
                return existing.Device;
            }

            // Attachments are server-managed and never accepted from the client payload
            var created = new SensorDevice { MacAddress = mac, DeploymentLocation = device.DeploymentLocation.Trim(), Category = device.Category };
            _devices[mac] = new DeviceRecord { Device = created };
            return created;
        }
    }

    /// <summary>
    /// Adds an attachment, replacing any existing attachment with the same file name.
    /// Returns the replaced attachment (if any) so the caller can remove its blob.
    /// </summary>
    public DeviceAttachment? AddAttachment(string macAddress, DeviceAttachment attachment)
    {
        var device = GetDevice(macAddress) ?? throw new KeyNotFoundException($"Device '{macAddress}' is not registered.");

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
        var device = GetDevice(macAddress);
        if (device == null) return null;
        lock (device.Attachments)
        {
            return device.Attachments.FirstOrDefault(a => a.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>O(1) device lookup by MAC address.</summary>
    public SensorDevice? GetDevice(string macAddress)
    {
        lock (_lock) return _devices.TryGetValue(macAddress, out var record) ? record.Device : null;
    }

    public bool Contains(string macAddress)
    {
        lock (_lock) return _devices.ContainsKey(macAddress);
    }

    public List<SensorDevice> GetAllDevices()
    {
        lock (_lock) return _devices.Values.Select(r => r.Device).OrderBy(d => d.MacAddress, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Records that a device contacted the gateway (called at the API boundary, before queuing), so the heartbeat
    /// lease reflects when the device transmitted - not how long its packet waited in the processing queue.
    /// Returns false when the device is not registered.
    /// </summary>
    public bool RecordHeartbeat(string macAddress, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        lock (_lock)
        {
            if (!_devices.TryGetValue(macAddress, out var record)) return false;
            record.FirstReceivedAt ??= now;
            record.LastReceivedAt = now;
            record.RecentArrivals.Enqueue(now);
            PruneArrivals(record, now);
            return true;
        }
    }

    /// <summary>
    /// Records a processed packet: appends it to the device timeline (in timestamp order) and counts it.
    /// Throws when the device is not registered.
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
            BatteryPercent: packet.BatteryPercent,
            RelayOn: packet.RelayOn
        );

        lock (_lock)
        {
            if (!_devices.TryGetValue(packet.MacAddress, out var record))
                throw new KeyNotFoundException($"Device '{packet.MacAddress}' is not registered.");

            record.Timeline.Add(snapshot);
            record.PacketsReceived++;
        }

        Interlocked.Increment(ref _totalPacketsReceived);
        return snapshot;
    }

    /// <summary>The chronologically latest reading (by device timestamp).</summary>
    public TelemetrySnapshot? GetLatestTelemetry(string macAddress)
    {
        lock (_lock) return _devices.TryGetValue(macAddress, out var record) ? record.Timeline.Latest : null;
    }

    /// <summary>Most recent readings in chronological order (preceding readings for the diagnostic drawer).</summary>
    public List<TelemetrySnapshot> GetHistory(string macAddress, int count = 60)
    {
        lock (_lock) return _devices.TryGetValue(macAddress, out var record) ? record.Timeline.TakeLast(count) : new();
    }

    /// <summary>Time-windowed, downsampled timeline for graphs.</summary>
    public List<TimelinePoint> GetTimeline(string macAddress, DateTime fromUtc, DateTime toUtc, int maxPoints)
    {
        lock (_lock) return _devices.TryGetValue(macAddress, out var record) ? record.Timeline.Range(fromUtc, toUtc, maxPoints) : new();
    }

    /// <summary>
    /// Last state reported by an actuator (its boolean value) or a smart-plug relay; null when unknown.
    /// </summary>
    public bool? GetReportedState(string macAddress)
    {
        var latest = GetLatestTelemetry(macAddress);
        return latest?.Value is bool state ? state : latest?.RelayOn;
    }

    public DeviceConnectionStatus GetStatus(string macAddress, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(macAddress, out var record))
                return new DeviceConnectionStatus(false, null, GatewayTiming.LeaseDuration.TotalSeconds, 0, null);

            PruneArrivals(record, nowUtc);
            double? sinceLast = record.LastReceivedAt is DateTime last ? Math.Max(0, (nowUtc - last).TotalSeconds) : null;
            bool isOnline = sinceLast.HasValue && sinceLast.Value <= GatewayTiming.LeaseDuration.TotalSeconds;

            return new DeviceConnectionStatus(
                IsOnline: isOnline,
                SecondsSinceLastPacket: sinceLast.HasValue ? Math.Round(sinceLast.Value, 1) : null,
                LeaseSeconds: GatewayTiming.LeaseDuration.TotalSeconds,
                PacketsReceived: record.PacketsReceived,
                PacketDeliveryPercent: ComputeDeliveryPercent(record, nowUtc)
            );
        }
    }

    /// <summary>MACs of devices that have reported at least once but whose heartbeat lease has expired.</summary>
    public List<string> GetExpiredLeases(DateTime nowUtc)
    {
        lock (_lock)
        {
            return _devices.Values
                .Where(r => r.LastReceivedAt is DateTime last && nowUtc - last > GatewayTiming.LeaseDuration)
                .Select(r => r.Device.MacAddress)
                .ToList();
        }
    }

    /// <summary>Restarts a node's ingestion buffer: clears its reading history and delivery counters.</summary>
    public void RestartIngestion(string macAddress)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(macAddress, out var record)) return;

            // Keep only the last known reading so the dashboard still shows the node's state
            var latest = record.Timeline.Latest;
            record.Timeline = new TelemetryTimeline(TimelineCapacity);
            if (latest != null) record.Timeline.Add(latest);
            record.RecentArrivals.Clear();
            record.PacketsReceived = 0;
            record.FirstReceivedAt = null;
        }
    }

    /// <summary>Clears all runtime telemetry state (timelines and counters), keeping registered devices.</summary>
    public void ResetRuntimeState()
    {
        lock (_lock)
        {
            foreach (var record in _devices.Values)
            {
                record.Timeline = new TelemetryTimeline(TimelineCapacity);
                record.RecentArrivals.Clear();
                record.FirstReceivedAt = null;
                record.LastReceivedAt = null;
                record.PacketsReceived = 0;
            }
        }
        Interlocked.Exchange(ref _totalPacketsReceived, 0);
    }

    private static void PruneArrivals(DeviceRecord record, DateTime nowUtc)
    {
        var cutoff = nowUtc - GatewayTiming.DeliveryWindow;
        while (record.RecentArrivals.Count > 0 && record.RecentArrivals.Peek() < cutoff)
            record.RecentArrivals.Dequeue();
    }

    /// <summary>
    /// Packet-delivery success score over a sliding window: received packets / expected transmissions.
    /// One window of tolerance absorbs phase jitter between the sender and the measurement window.
    /// </summary>
    private static double? ComputeDeliveryPercent(DeviceRecord record, DateTime nowUtc)
    {
        if (record.FirstReceivedAt is not DateTime first) return null;

        var observed = nowUtc - first;
        if (observed > GatewayTiming.DeliveryWindow) observed = GatewayTiming.DeliveryWindow;

        int expected = (int)Math.Floor(observed / GatewayTiming.TransmissionInterval) - 1;
        if (expected <= 0) return record.LastReceivedAt.HasValue ? 100.0 : null;

        return Math.Round(Math.Min(100.0, record.RecentArrivals.Count * 100.0 / expected), 1);
    }

    private static object? ProjectValue<T>(T value) => value switch
    {
        PowerMetric p => p.Watts,
        bool b => b,
        float f => Math.Round((double)f, 4),
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
