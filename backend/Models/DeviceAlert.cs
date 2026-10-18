using System.Text.Json.Serialization;

namespace SmartX.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AlertKind>))]
public enum AlertKind
{
    Anomaly,
    HeartbeatLost
}

/// <summary>
/// A latched alert raised by the gateway. It stays active until an operator acknowledges it
/// (Human-in-the-Loop triage), so transient spikes cannot silently disappear between polls.
/// </summary>
public class DeviceAlert
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public string MacAddress { get; init; } = string.Empty;
    public AlertKind Kind { get; init; }
    public DateTime RaisedAt { get; init; } = DateTime.UtcNow;
    public string Message { get; init; } = string.Empty;

    // Anomaly context (deviation vector)
    public double? TriggerValue { get; init; }
    public double? BaselineMean { get; init; }
    public double? ZScore { get; init; }
    public double? Deviation => TriggerValue.HasValue && BaselineMean.HasValue ? TriggerValue - BaselineMean : null;

    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? Notes { get; set; }

    public bool IsActive => AcknowledgedAt == null;
}
