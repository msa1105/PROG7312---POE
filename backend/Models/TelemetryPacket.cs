namespace SmartX.Api.Models;

/// <summary>
/// Reference: Microsoft, 2026d. Generic types and methods - C#. [online] Available at: <https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/generics> [Accessed 19 October 2026].
/// </summary>
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

    // Smart-plug relay state reported by power meters (null for devices without a relay)
    public bool? RelayOn { get; init; }
}
