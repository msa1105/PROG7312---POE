using SmartX.Api.Models;
using SmartX.Api.Services;
using SmartX.Api.Services.Ingestion;
using SmartX.Api.Services.Realtime;
using static SmartX.Api.Endpoints.EndpointHelpers;

namespace SmartX.Api.Endpoints;

public record BatchIngestionRequest(string MacAddress, int ChannelIndex, double[] Readings, string MetricUnit);
public record BulkReading(DateTime? Timestamp, double Value);
public record BulkIngestionRequest(string MacAddress, string MetricUnit, List<BulkReading> Readings);
public record PowerAggregationRequest(double WattsMeter1, double WattsMeter2);

/// <summary>
/// Device-facing ingestion endpoints. Packets are classified at the boundary and queued (202 Accepted):
/// routine readings join the FIFO Queue&lt;T&gt;; safety-threshold breaches join the PriorityQueue and bypass the backlog.
/// </summary>
public static class TelemetryEndpoints
{
    public const int MaxBulkReadings = 10_000;

    public static void MapTelemetryEndpoints(this WebApplication app)
    {
        // Generics with TelemetryPacket<T>: one endpoint per payload type, a single typed pipeline behind them
        app.MapPost("/api/telemetry/ingest/float", (TelemetryPacket<float> packet, DeviceRegistryService registry, TelemetryIngestionQueue queue, AlertStateTracker alerts, GatewayBroadcaster broadcaster) =>
            Ingest(packet.MacAddress, TelemetryWorkItems.From(packet), registry, queue, alerts, broadcaster));

        app.MapPost("/api/telemetry/ingest/int", (TelemetryPacket<int> packet, DeviceRegistryService registry, TelemetryIngestionQueue queue, AlertStateTracker alerts, GatewayBroadcaster broadcaster) =>
            Ingest(packet.MacAddress, TelemetryWorkItems.From(packet), registry, queue, alerts, broadcaster));

        app.MapPost("/api/telemetry/ingest/power", (TelemetryPacket<PowerMetric> packet, DeviceRegistryService registry, TelemetryIngestionQueue queue, AlertStateTracker alerts, GatewayBroadcaster broadcaster) =>
            Ingest(packet.MacAddress, TelemetryWorkItems.From(packet), registry, queue, alerts, broadcaster));

        app.MapPost("/api/telemetry/ingest/bool", (TelemetryPacket<bool> packet, DeviceRegistryService registry, TelemetryIngestionQueue queue, AlertStateTracker alerts, GatewayBroadcaster broadcaster) =>
            Ingest(packet.MacAddress, TelemetryWorkItems.From(packet), registry, queue, alerts, broadcaster));

        // Backlog upload: a device that buffered readings while offline sends them in one request
        app.MapPost("/api/telemetry/ingest/bulk", (BulkIngestionRequest req, DeviceRegistryService registry, TelemetryIngestionQueue queue) =>
        {
            var device = FindDevice(registry, req.MacAddress);
            if (device == null) return DeviceNotFound(req.MacAddress);
            if (req.Readings is not { Count: > 0 } || req.Readings.Count > MaxBulkReadings)
                return BadRequest($"Readings must contain between 1 and {MaxBulkReadings:N0} values.");
            if (device.Category == SensorCategory.Actuator)
                return BadRequest("Bulk upload is only supported for numeric sensors.");

            var now = DateTime.UtcNow;
            int standard = 0, priority = 0, rejected = 0;
            foreach (var reading in req.Readings)
            {
                var timestamp = reading.Timestamp ?? now;
                var item = device.Category == SensorCategory.PowerConsumption
                    ? TelemetryWorkItems.From(new TelemetryPacket<PowerMetric> { MacAddress = device.MacAddress, Value = new PowerMetric(reading.Value), MetricUnit = req.MetricUnit, Timestamp = timestamp })
                    : TelemetryWorkItems.From(new TelemetryPacket<float> { MacAddress = device.MacAddress, Value = (float)reading.Value, MetricUnit = req.MetricUnit, Timestamp = timestamp });

                var result = queue.Enqueue(item, CriticalReadingClassifier.Classify(device.Category, item));
                if (!result.Accepted) rejected++;
                else if (result.Lane == IngestionLane.Priority) priority++;
                else standard++;
            }

            registry.RecordHeartbeat(device.MacAddress);
            return Results.Accepted(value: new { QueuedStandard = standard, QueuedPriority = priority, Rejected = rejected, queue.GetStats().Standard.Depth });
        });

        // Jagged Arrays double[][]: a channel batch is loaded, flushed (drained) and queued for processing
        app.MapPost("/api/telemetry/batch", (BatchIngestionRequest req, JaggedTelemetryBuffer buffer, DeviceRegistryService registry, TelemetryIngestionQueue queue) =>
        {
            var device = FindDevice(registry, req.MacAddress);
            if (device == null) return DeviceNotFound(req.MacAddress);
            if (req.ChannelIndex < 0 || req.ChannelIndex >= buffer.ChannelCount)
                return BadRequest($"ChannelIndex must be between 0 and {buffer.ChannelCount - 1}.");
            if (req.Readings is not { Length: > 0 and <= 100_000 })
                return BadRequest("Readings must contain between 1 and 100,000 values.");

            buffer.LoadChannelBatch(req.ChannelIndex, req.Readings);
            var flushed = buffer.FlushToList(device.MacAddress, req.MetricUnit, req.ChannelIndex);

            int priority = 0;
            foreach (var packet in flushed)
            {
                var item = TelemetryWorkItems.From(packet);
                if (queue.Enqueue(item, CriticalReadingClassifier.Classify(device.Category, item)).Lane == IngestionLane.Priority) priority++;
            }

            registry.RecordHeartbeat(device.MacAddress);
            return Results.Accepted(value: new { ProcessedCount = flushed.Count, QueuedPriority = priority });
        });

        app.MapGet("/api/telemetry/buffer/status", (JaggedTelemetryBuffer buffer) => Results.Ok(buffer.GetChannelStatus()));

        // Operator Overloading: PowerMetric struct operators (+, -, >, <, ==)
        app.MapPost("/api/telemetry/power-metrics/aggregate", (PowerAggregationRequest req) =>
        {
            var meter1 = new PowerMetric(req.WattsMeter1);
            var meter2 = new PowerMetric(req.WattsMeter2);

            PowerMetric aggregatedLoad = meter1 + meter2; // operator +
            PowerMetric delta = meter1 - meter2;          // operator -

            return Results.Ok(new
            {
                Meter1Watts = meter1.Watts,
                Meter2Watts = meter2.Watts,
                AggregatedWatts = Math.Round(aggregatedLoad.Watts, 2),
                DeltaWatts = Math.Round(delta.Watts, 2),
                IsMeter1Greater = meter1 > meter2,         // operator >
                IsMeter1Lesser = meter1 < meter2,          // operator <
                AreEqual = meter1 == meter2,               // operator ==
                FormulaApplied = "Meter3 = Meter1 + Meter2, Delta = |Meter1 - Meter2|",
                ExecutionEngine = ".NET 10 Value-Type Struct Operator Overload"
            });
        });

        // Ingestion pipeline inspection & demo controls
        app.MapGet("/api/pipeline/stats", (TelemetryIngestionQueue queue, AlertStateTracker alerts) =>
            Results.Ok(new { Queue = queue.GetStats(), Alerts = alerts.GetStats(), DisconnectedNodes = alerts.GetDisconnected() }));

        app.MapPost("/api/pipeline/processing-cost", (ProcessingCostRequest req, TelemetryIngestionQueue queue) =>
        {
            if (req.Milliseconds is < 0 or > 50) return BadRequest("Milliseconds must be between 0 and 50.");
            queue.SimulatedProcessingCostMs = req.Milliseconds;
            return Results.Ok(new { queue.SimulatedProcessingCostMs });
        });
    }

    private static IResult Ingest(string macAddress, TelemetryWorkItem item, DeviceRegistryService registry, TelemetryIngestionQueue queue,
        AlertStateTracker alerts, GatewayBroadcaster broadcaster)
    {
        var device = FindDevice(registry, macAddress);
        if (device == null) return DeviceNotFound(macAddress);

        var classification = CriticalReadingClassifier.Classify(device.Category, item);
        var result = queue.Enqueue(item, classification);
        registry.RecordHeartbeat(device.MacAddress);
        if (alerts.MarkReconnected(device.MacAddress)) broadcaster.MarkDeviceDirty(device.MacAddress);
        if (!result.Accepted)
            return Results.Problem("The standard ingestion queue is full; retry later.", statusCode: StatusCodes.Status503ServiceUnavailable);

        return Results.Accepted(value: new
        {
            item.PacketId,
            result.Sequence,
            result.Lane,
            result.StandardDepth,
            result.PriorityDepth,
            PriorityReason = classification?.Reason
        });
    }
}

public record ProcessingCostRequest(int Milliseconds);
