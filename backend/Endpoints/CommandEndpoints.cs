using SmartX.Api.Models;
using SmartX.Api.Services;
using SmartX.Api.Services.Realtime;
using SmartX.Api.Services.Recommendations;
using static SmartX.Api.Endpoints.EndpointHelpers;

namespace SmartX.Api.Endpoints;

public record CommandTarget(string MacAddress, bool TargetState);
public record IssueCommandRequest(List<CommandTarget> Targets, string? Label = null, string? IssuedBy = null);
public record CommandAckRequest(bool Success, bool? AppliedState = null, string? Message = null);

/// <summary>Real-Time Command Stream: manual overrides, Stack-based undo, and the device-side command channel.</summary>
public static class CommandEndpoints
{
    public const int MaxTargetsPerBatch = 50;

    public static void MapCommandEndpoints(this WebApplication app)
    {
        // Operator issues one or many manual overrides (e.g. "Pump A ON", "Shut down all valves")
        app.MapPost("/api/commands", (IssueCommandRequest req, DeviceRegistryService registry, CommandDispatcher dispatcher, RecommendationService recommendations) =>
        {
            if (req.Targets is not { Count: > 0 } || req.Targets.Count > MaxTargetsPerBatch)
                return BadRequest($"Provide between 1 and {MaxTargetsPerBatch} command targets.");

            var requests = new List<CommandRequest>();
            foreach (var target in req.Targets)
            {
                var device = FindDevice(registry, target.MacAddress);
                if (device == null) return DeviceNotFound(target.MacAddress);
                if (!RecommendationService.IsControllable(device))
                    return BadRequest($"{RecommendationService.DeviceName(device)} ({device.MacAddress}) is a read-only sensor and cannot receive override commands.");
                if (requests.Any(r => r.MacAddress == device.MacAddress))
                    return BadRequest($"Device {device.MacAddress} appears more than once in the batch.");

                requests.Add(new CommandRequest(device.MacAddress, RecommendationService.DeviceName(device), target.TargetState, registry.GetReportedState(device.MacAddress)));
            }

            var label = string.IsNullOrWhiteSpace(req.Label)
                ? requests.Count == 1 ? $"{requests[0].DeviceName} → {(requests[0].TargetState ? "ON" : "OFF")}" : $"{requests.Count} devices"
                : req.Label.Trim();

            var batch = dispatcher.Issue(label, CommandSource.Operator, requests, string.IsNullOrWhiteSpace(req.IssuedBy) ? "Operator-Gateway" : req.IssuedBy.Trim());
            recommendations.RecordCommandBatch(batch);
            return Results.Ok(batch);
        });

        // Stack<T> undo: revert the most recent operator action
        app.MapPost("/api/commands/undo", (CommandDispatcher dispatcher) =>
        {
            var result = dispatcher.Undo();
            if (result == null) return Results.Conflict(new { error = "Nothing to undo." });
            return Results.Ok(new { Reverted = result.Value.Reverted, UndoBatch = result.Value.UndoBatch });
        });

        app.MapGet("/api/commands/history", (int? limit, CommandDispatcher dispatcher) =>
            Results.Ok(dispatcher.GetHistory(Math.Clamp(limit ?? 40, 1, 200))));

        app.MapGet("/api/commands/stack", (CommandDispatcher dispatcher) => Results.Ok(dispatcher.GetUndoStack()));

        app.MapGet("/api/commands/{id:guid}", (Guid id, CommandDispatcher dispatcher) =>
            dispatcher.GetCommand(id) is DeviceCommand command ? Results.Ok(command) : Results.NotFound(new { error = "Command not found." }));

        // Device side: poll the FIFO outbox (marks commands Delivered)
        app.MapGet("/api/devices/{macAddress}/commands", (string macAddress, DeviceRegistryService registry, CommandDispatcher dispatcher) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);
            return Results.Ok(dispatcher.DequeueForDevice(device.MacAddress));
        });

        // Device side: acknowledge an applied command with the resulting state
        app.MapPost("/api/devices/{macAddress}/commands/{id:guid}/ack", (string macAddress, Guid id, CommandAckRequest req,
            DeviceRegistryService registry, CommandDispatcher dispatcher, AnomalyDetectionService anomaly, GatewayBroadcaster broadcaster) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);

            var command = dispatcher.Acknowledge(device.MacAddress, id, req.Success, req.AppliedState, req.Message);
            if (command == null) return Results.NotFound(new { error = "Command not found for this device." });

            // A commanded state change (e.g. relay switched off) is intentional, not an anomaly: restart the baseline
            if (req.Success && command.PreviousState != req.AppliedState)
                anomaly.ResetWindow(device.MacAddress);

            broadcaster.MarkDeviceDirty(device.MacAddress);
            return Results.Ok(command);
        });
    }
}
