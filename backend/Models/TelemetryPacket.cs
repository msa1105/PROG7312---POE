namespace SmartX.Api.Models;

public sealed class TelemetryPacket<T>
{
    public Guid PacketId { get; init; } = Guid.NewGuid();
    public string MacAddress { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public T Value { get; init; } = default!;
    public string MetricUnit { get; init; } = string.Empty;

    // Transmission metadata (optional) - last known radio and power state of the edge node
    public int? SignalStrengthDbm { get; init; }
    public double? BatteryPercent { get; init; }
}
