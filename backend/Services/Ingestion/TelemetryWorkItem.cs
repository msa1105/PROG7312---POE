using SmartX.Api.Models;

namespace SmartX.Api.Services.Ingestion;

/// <summary>
/// Type-erased handle to a queued TelemetryPacket&lt;T&gt;. The generic subclass keeps the payload unboxed
/// until processing, while the queues can hold packets of every sensor type side by side.
/// </summary>
public abstract class TelemetryWorkItem
{
    public abstract Guid PacketId { get; }
    public abstract string MacAddress { get; }
    public abstract DateTime Timestamp { get; }
    public abstract string MetricUnit { get; }

    /// <summary>Numeric reading used for anomaly scoring and threshold checks (null for digital state).</summary>
    public abstract double? NumericValue { get; }

    /// <summary>Records the packet in the registry, scoring it with the anomaly detector when numeric.</summary>
    public abstract TelemetrySnapshot Apply(DeviceRegistryService registry, AnomalyEvaluation? evaluation);
}

public sealed class TelemetryWorkItem<T> : TelemetryWorkItem
{
    private readonly TelemetryPacket<T> _packet;
    private readonly Func<T, double?> _numericProjection;

    public TelemetryWorkItem(TelemetryPacket<T> packet, Func<T, double?> numericProjection)
    {
        _packet = packet;
        _numericProjection = numericProjection;
    }

    public TelemetryPacket<T> Packet => _packet;
    public override Guid PacketId => _packet.PacketId;
    public override string MacAddress => _packet.MacAddress;
    public override DateTime Timestamp => _packet.Timestamp;
    public override string MetricUnit => _packet.MetricUnit;
    public override double? NumericValue => _numericProjection(_packet.Value);

    public override TelemetrySnapshot Apply(DeviceRegistryService registry, AnomalyEvaluation? evaluation)
        => registry.RecordTelemetry(_packet, evaluation);
}

public static class TelemetryWorkItems
{
    public static TelemetryWorkItem From(TelemetryPacket<float> packet) => new TelemetryWorkItem<float>(packet, v => v);
    public static TelemetryWorkItem From(TelemetryPacket<double> packet) => new TelemetryWorkItem<double>(packet, v => v);
    public static TelemetryWorkItem From(TelemetryPacket<int> packet) => new TelemetryWorkItem<int>(packet, v => v);
    public static TelemetryWorkItem From(TelemetryPacket<PowerMetric> packet) => new TelemetryWorkItem<PowerMetric>(packet, v => v.Watts);
    public static TelemetryWorkItem From(TelemetryPacket<bool> packet) => new TelemetryWorkItem<bool>(packet, _ => null);
}
