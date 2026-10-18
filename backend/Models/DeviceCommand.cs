using System.Text.Json.Serialization;

namespace SmartX.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<CommandStatus>))]
public enum CommandStatus
{
    Queued,
    Delivered,
    Acknowledged,
    Failed,
    Expired
}

[JsonConverter(typeof(JsonStringEnumConverter<CommandSource>))]
public enum CommandSource
{
    Operator,
    Undo,
    Suggestion
}

/// <summary>
/// A manual override sent to one edge device (e.g. "Water Solenoid 1 -> ON").
/// Lifecycle: Queued (in the device outbox) -> Delivered (device polled it) -> Acknowledged / Failed, or Expired.
/// </summary>
public class DeviceCommand
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BatchId { get; init; }
    public string MacAddress { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>Desired actuator / relay state: true = ON (open valve, pump running, relay closed).</summary>
    public bool TargetState { get; init; }

    /// <summary>State reported by the device when the command was issued; used to build the Undo command.</summary>
    public bool? PreviousState { get; init; }

    public CommandSource Source { get; init; }
    public string IssuedBy { get; init; } = "Operator-Gateway";
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;

    public CommandStatus Status { get; set; } = CommandStatus.Queued;
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool? AppliedState { get; set; }
    public string? DeviceMessage { get; set; }

    public string Description => $"{DeviceName} → {(TargetState ? "ON" : "OFF")}";
    public bool IsPending => Status is CommandStatus.Queued or CommandStatus.Delivered;
}

/// <summary>
/// One operator action, which may address several devices at once (e.g. "Shut down all valves").
/// The Undo stack stores whole batches so a bulk action is reverted as a unit.
/// </summary>
public class CommandBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; init; } = string.Empty;
    public CommandSource Source { get; init; }
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;
    public List<DeviceCommand> Commands { get; init; } = new();

    /// <summary>For an undo batch: the batch it reverts.</summary>
    public Guid? RevertsBatchId { get; init; }

    public bool IsReverted { get; set; }
    public DateTime? RevertedAt { get; set; }
}
