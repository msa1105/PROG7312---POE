using System.Diagnostics;
using SmartX.Api.Services;
using SmartX.Api.Services.Ingestion;
using SmartX.Api.Services.Recommendations;
using static SmartX.Api.Endpoints.EndpointHelpers;

namespace SmartX.Api.Endpoints;

public record SpikeRequest(string MacAddress, double Multiplier = 4.5);
public record HeartbeatRequest(string MacAddress);
public record CriticalEventRequest(string MacAddress, CriticalEventKind Kind);
public record FloodRequest(string MacAddress, int Count = 2000);

/// <summary>
/// Fault-injection controls. Operators schedule directives here; the external device simulator claims them and
/// sends the resulting readings back through the normal ingestion endpoints.
/// </summary>
public static class SimulationEndpoints
{
    public const int MaxFlood = TelemetryEndpoints.MaxBulkReadings;

    public static void MapSimulationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/simulation/spike", (SpikeRequest req, DeviceRegistryService registry, SimulationControlService simulation) =>
        {
            var device = FindDevice(registry, req.MacAddress);
            if (device == null) return DeviceNotFound(req.MacAddress);
            if (req.Multiplier is <= AnomalyDetectionService.ZScoreThreshold or > 50)
                return BadRequest($"Multiplier must be greater than {AnomalyDetectionService.ZScoreThreshold} and at most 50 standard deviations.");

            simulation.ScheduleSpike(device.MacAddress, req.Multiplier);
            return Results.Ok(new { message = $"+{req.Multiplier}σ spike scheduled for {device.MacAddress}" });
        });

        app.MapPost("/api/simulation/drop-heartbeat", (HeartbeatRequest req, DeviceRegistryService registry, SimulationControlService simulation) =>
        {
            var device = FindDevice(registry, req.MacAddress);
            if (device == null) return DeviceNotFound(req.MacAddress);

            simulation.DropHeartbeat(device.MacAddress);
            return Results.Ok(new { message = $"Heartbeat dropped for {device.MacAddress}" });
        });

        app.MapPost("/api/simulation/restore-heartbeat", (HeartbeatRequest req, DeviceRegistryService registry, SimulationControlService simulation) =>
        {
            var device = FindDevice(registry, req.MacAddress);
            if (device == null) return DeviceNotFound(req.MacAddress);

            simulation.RestoreHeartbeat(device.MacAddress);
            return Results.Ok(new { message = $"Heartbeat restored for {device.MacAddress}" });
        });

        // High-priority alert: the simulated device sends a threshold-breaching reading that takes the priority lane
        app.MapPost("/api/simulation/critical", (CriticalEventRequest req, DeviceRegistryService registry, SimulationControlService simulation) =>
        {
            var device = FindDevice(registry, req.MacAddress);
            if (device == null) return DeviceNotFound(req.MacAddress);
            if (!Enum.IsDefined(req.Kind)) return BadRequest("Unknown critical event kind.");

            var expected = req.Kind == CriticalEventKind.MoistureCrash ? Models.SensorCategory.Environmental : Models.SensorCategory.PowerConsumption;
            if (device.Category != expected)
                return BadRequest($"{req.Kind} applies to {expected} devices; {device.MacAddress} is {device.Category}.");

            var directive = simulation.ScheduleCriticalEvent(device.MacAddress, req.Kind);
            return Results.Ok(new { message = $"{req.Kind} scheduled for {device.MacAddress}", directive.Value });
        });

        // Backlog flood: the simulated device bulk-uploads a burst of routine readings into the standard queue
        app.MapPost("/api/simulation/flood", (FloodRequest req, DeviceRegistryService registry, SimulationControlService simulation) =>
        {
            var device = FindDevice(registry, req.MacAddress);
            if (device == null) return DeviceNotFound(req.MacAddress);
            if (req.Count is < 1 or > MaxFlood) return BadRequest($"Count must be between 1 and {MaxFlood:N0}.");
            if (device.Category == Models.SensorCategory.Actuator) return BadRequest("Floods require a numeric sensor.");

            simulation.ScheduleFlood(device.MacAddress, req.Count);
            return Results.Ok(new { message = $"{req.Count:N0}-reading backlog upload scheduled for {device.MacAddress}" });
        });

        // Called by the device simulator once per transmission window
        app.MapPost("/api/simulation/directives/claim", (SimulationControlService simulation, AnomalyDetectionService anomaly) =>
            Results.Ok(simulation.Claim(anomaly.GetBaseline)));

        app.MapPost("/api/simulation/burst-batch", (JaggedTelemetryBuffer buffer) =>
        {
            var watch = Stopwatch.StartNew();

            // Simulate ingest of 5000 items in a jagged buffer (4 channels x 1250)
            var rand = new Random();
            for (int ch = 0; ch < buffer.ChannelCount; ch++)
            {
                var readings = new double[1250];
                for (int i = 0; i < readings.Length; i++) readings[i] = 400.0 + rand.NextDouble() * 20.0;
                buffer.LoadChannelBatch(ch, readings);
            }
            var flushed = buffer.FlushToList("24:6F:28:9A:12:03", "W");

            watch.Stop();
            return Results.Ok(new { ProcessedCount = flushed.Count, ElapsedMs = watch.ElapsedMilliseconds });
        });

        app.MapPost("/api/simulation/reset", async (DeviceRegistryService registry, AnomalyDetectionService anomaly, SimulationControlService simulation,
            AlertStateTracker alerts, CommandDispatcher commands, TelemetryIngestionQueue queue, RecommendationService recommendations, DataSeeder seeder) =>
        {
            simulation.Reset();
            anomaly.ResetAll();
            registry.ResetRuntimeState();
            alerts.Reset();
            commands.Reset();
            queue.SimulatedProcessingCostMs = 0;
            queue.ResetStatistics();
            recommendations.Reset(reseed: true);
            await seeder.SeedAsync();
            return Results.Ok(new { message = "Reset successful" });
        });
    }
}
