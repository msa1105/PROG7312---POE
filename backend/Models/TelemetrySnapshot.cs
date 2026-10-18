namespace SmartX.Api.Models;

/// <summary>
/// Immutable, type-erased view of the latest TelemetryPacket&lt;T&gt; for a device, enriched with the
/// rolling statistical baseline that was used to evaluate it. Created once per packet at ingestion.
/// </summary>
public record TelemetrySnapshot(
    Guid PacketId,
    string MacAddress,
    DateTime Timestamp,
    object? Value,
    string MetricUnit,
    string PayloadType,
    bool IsAnomaly,
    double? ZScore,
    double? RollingMean,
    double? RollingStdDev,
    int? SignalStrengthDbm,
    double? BatteryPercent,
    bool? RelayOn = null
);

/// <summary>
/// Gateway-side connectivity and reliability state of a device, computed with server time.
/// </summary>
public record DeviceConnectionStatus(
    bool IsOnline,
    double? SecondsSinceLastPacket,
    double LeaseSeconds,
    long PacketsReceived,
    double? PacketDeliveryPercent
);
