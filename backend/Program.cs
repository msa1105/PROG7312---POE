using SmartX.Api.Models;
using SmartX.Api.Services;
using System.Diagnostics;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

var uploadsRoot = builder.Configuration["Storage:UploadsPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "Uploads");
var keysDirectory = builder.Configuration["Storage:KeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "keys");

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddSingleton<DeviceRegistryService>();
builder.Services.AddSingleton<AnomalyDetectionService>();
builder.Services.AddSingleton<JaggedTelemetryBuffer>();
builder.Services.AddSingleton<IncidentTriageService>();
builder.Services.AddSingleton<SimulationControlService>();
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<FileEncryptionService>>();
    var masterKey = FileEncryptionService.LoadOrCreateMasterKey(
        builder.Configuration[FileEncryptionService.MasterKeyConfigKey], keysDirectory, message => logger.LogWarning("{Message}", message));
    return new FileEncryptionService(masterKey);
});
builder.Services.AddSingleton(sp => new AttachmentService(
    uploadsRoot, sp.GetRequiredService<FileEncryptionService>(), sp.GetRequiredService<DeviceRegistryService>()));
builder.Services.AddTransient<DataSeeder>();
builder.Services.AddHostedService<TelemetrySimulationWorker>();

// Configure CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173") // Frontend URL
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();

    var purged = scope.ServiceProvider.GetRequiredService<AttachmentService>().PurgeOrphanedBlobs();
    if (purged > 0) app.Logger.LogInformation("Removed {Count} orphaned attachment blob(s) from a previous run.", purged);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

// Resolves a route/body MAC address to a registered device (case-insensitive, validated format)
static SensorDevice? FindDevice(DeviceRegistryService registry, string? macAddress) =>
    DeviceRegistryService.TryNormalizeMac(macAddress, out var mac) ? registry.GetDevice(mac) : null;

static IResult DeviceNotFound(string? macAddress) =>
    Results.NotFound(new { error = $"Device '{macAddress}' is not registered on this gateway." });

static object BuildDeviceView(SensorDevice device, DeviceRegistryService registry, DateTime nowUtc) => new
{
    Device = device,
    LatestTelemetry = registry.GetLatestTelemetry(device.MacAddress),
    Status = registry.GetStatus(device.MacAddress, nowUtc),
    ActiveAlerts = registry.GetActiveAlerts(device.MacAddress)
};

// 1. Gateway Health & Status Endpoint (for Startup Interface, Landing Page and KPI strip)
app.MapGet("/api/health", (DeviceRegistryService registry, JaggedTelemetryBuffer buffer, IncidentTriageService incidentService, AnomalyDetectionService anomalyService) =>
{
    var now = DateTime.UtcNow;
    var devices = registry.GetAllDevices().ToList();
    var statuses = devices.Select(d => registry.GetStatus(d.MacAddress, now)).ToList();
    var deliveryScores = statuses.Where(s => s.PacketDeliveryPercent.HasValue).Select(s => s.PacketDeliveryPercent!.Value).ToList();
    var metrics = buffer.GetBufferMetrics();
    var meanTimeToDetect = registry.GetMeanTimeToDetectSeconds();
    int onlineCount = statuses.Count(s => s.IsOnline);

    return Results.Ok(new
    {
        Status = "ONLINE",
        GatewayVersion = "Smart-X v2.4-Core (.NET 10 Minimal API)",
        UptimeSeconds = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
        ActiveDeviceCount = onlineCount,
        RegisteredDeviceCount = devices.Count,
        AvailabilityPercent = devices.Count == 0 ? (double?)null : Math.Round(onlineCount * 100.0 / devices.Count, 1),
        PacketDeliveryPercent = deliveryScores.Count == 0 ? (double?)null : Math.Round(deliveryScores.Average(), 1),
        TotalTelemetryPackets = registry.TotalPacketsReceived,
        TotalIngestedPackets = metrics.TotalIngested,
        JaggedBufferedReadings = buffer.GetChannelStatus().TotalBufferedReadings,
        BufferMetrics = metrics,
        AnomaliesLastMinute = anomalyService.CountAnomaliesSince(now.AddMinutes(-1)),
        ActiveAlertCount = registry.CountActiveAlerts(),
        MeanTimeToDetectSeconds = meanTimeToDetect,
        LeaseSeconds = GatewayTiming.LeaseDuration.TotalSeconds,
        OperatorEngagement = incidentService.GetEngagementStats(meanTimeToDetect),
        Timestamp = now
    });
});

// 2. Ingestion Endpoints (Generics with TelemetryPacket<T>)
app.MapPost("/api/telemetry/ingest/float", (TelemetryPacket<float> packet, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    var device = FindDevice(registry, packet.MacAddress);
    if (device == null) return DeviceNotFound(packet.MacAddress);

    var snapshot = registry.RecordTelemetry(packet, anomalyService.Evaluate(device.MacAddress, packet.Value));
    return Results.Ok(new { packet.PacketId, snapshot.IsAnomaly, snapshot.ZScore });
});

app.MapPost("/api/telemetry/ingest/int", (TelemetryPacket<int> packet, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    var device = FindDevice(registry, packet.MacAddress);
    if (device == null) return DeviceNotFound(packet.MacAddress);

    var snapshot = registry.RecordTelemetry(packet, anomalyService.Evaluate(device.MacAddress, packet.Value));
    return Results.Ok(new { packet.PacketId, snapshot.IsAnomaly, snapshot.ZScore });
});

app.MapPost("/api/telemetry/ingest/power", (TelemetryPacket<PowerMetric> packet, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    var device = FindDevice(registry, packet.MacAddress);
    if (device == null) return DeviceNotFound(packet.MacAddress);

    var snapshot = registry.RecordTelemetry(packet, anomalyService.Evaluate(device.MacAddress, packet.Value.Watts));
    return Results.Ok(new { packet.PacketId, snapshot.IsAnomaly, snapshot.ZScore });
});

app.MapPost("/api/telemetry/ingest/bool", (TelemetryPacket<bool> packet, DeviceRegistryService registry) =>
{
    var device = FindDevice(registry, packet.MacAddress);
    if (device == null) return DeviceNotFound(packet.MacAddress);

    registry.RecordTelemetry(packet);
    return Results.Ok(new { packet.PacketId, isAnomaly = false });
});

// 3. Batch Buffer Endpoint (Jagged Arrays double[][])
app.MapPost("/api/telemetry/batch", (BatchIngestionRequest req, JaggedTelemetryBuffer buffer, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    var device = FindDevice(registry, req.MacAddress);
    if (device == null) return DeviceNotFound(req.MacAddress);
    if (req.ChannelIndex < 0 || req.ChannelIndex >= buffer.ChannelCount)
        return Results.BadRequest(new { error = $"ChannelIndex must be between 0 and {buffer.ChannelCount - 1}." });
    if (req.Readings is not { Length: > 0 and <= 100_000 })
        return Results.BadRequest(new { error = "Readings must contain between 1 and 100,000 values." });

    buffer.LoadChannelBatch(req.ChannelIndex, req.Readings);
    var flushed = buffer.FlushToList(device.MacAddress, req.MetricUnit, req.ChannelIndex);

    var anomalies = new List<Guid>();
    foreach (var packet in flushed)
    {
        var snapshot = registry.RecordTelemetry(packet, anomalyService.Evaluate(packet.MacAddress, packet.Value));
        if (snapshot.IsAnomaly)
        {
            anomalies.Add(packet.PacketId);
        }
    }

    return Results.Ok(new { ProcessedCount = flushed.Count, Anomalies = anomalies });
});

// Endpoint to inspect live Jagged Telemetry Buffer state
app.MapGet("/api/telemetry/buffer/status", (JaggedTelemetryBuffer buffer) =>
{
    return Results.Ok(buffer.GetChannelStatus());
});

// 4. Operator Overloading API Endpoint
// Directly utilizes PowerMetric struct operator overloads (+, -, >, <, ==)
app.MapPost("/api/telemetry/power-metrics/aggregate", (PowerAggregationRequest req) =>
{
    var meter1 = new PowerMetric(req.WattsMeter1);
    var meter2 = new PowerMetric(req.WattsMeter2);

    // Explicit C# struct operator overloads
    PowerMetric aggregatedLoad = meter1 + meter2; // operator +
    PowerMetric delta = meter1 - meter2;          // operator -
    bool isMeter1Greater = meter1 > meter2;        // operator >
    bool isMeter1Lesser = meter1 < meter2;         // operator <
    bool areEqual = meter1 == meter2;              // operator ==

    return Results.Ok(new
    {
        Meter1Watts = meter1.Watts,
        Meter2Watts = meter2.Watts,
        AggregatedWatts = Math.Round(aggregatedLoad.Watts, 2),
        DeltaWatts = Math.Round(delta.Watts, 2),
        IsMeter1Greater = isMeter1Greater,
        IsMeter1Lesser = isMeter1Lesser,
        AreEqual = areEqual,
        FormulaApplied = "Meter3 = Meter1 + Meter2, Delta = |Meter1 - Meter2|",
        ExecutionEngine = ".NET 10 Value-Type Struct Operator Overload"
    });
});

// 5. Device Registration Endpoints
app.MapPost("/api/sensors/register", (SensorDevice device, DeviceRegistryService registry) =>
{
    if (string.IsNullOrWhiteSpace(device.MacAddress))
        return Results.BadRequest(new { error = "MAC Address cannot be empty." });
    if (!DeviceRegistryService.TryNormalizeMac(device.MacAddress, out _))
        return Results.BadRequest(new { error = "MAC Address must use XX:XX:XX:XX:XX:XX hex notation." });
    if (string.IsNullOrWhiteSpace(device.DeploymentLocation))
        return Results.BadRequest(new { error = "Deployment location cannot be empty." });
    if (!Enum.IsDefined(device.Category))
        return Results.BadRequest(new { error = "Unknown sensor category." });

    var registered = registry.RegisterDevice(device);
    return Results.Ok(registered);
});

app.MapGet("/api/sensors", (DeviceRegistryService registry) =>
{
    var now = DateTime.UtcNow;
    return Results.Ok(registry.GetAllDevices().Select(d => BuildDeviceView(d, registry, now)).ToList());
});

// Preceding historical readings for one node (progressive disclosure drill-down)
app.MapGet("/api/sensors/{macAddress}/history", (string macAddress, DeviceRegistryService registry) =>
{
    var device = FindDevice(registry, macAddress);
    if (device == null) return DeviceNotFound(macAddress);
    return Results.Ok(registry.GetHistory(device.MacAddress));
});

// 6. Human-in-the-Loop inline triage actions
app.MapPost("/api/sensors/{macAddress}/ping", (string macAddress, DeviceRegistryService registry) =>
{
    var device = FindDevice(registry, macAddress);
    if (device == null) return DeviceNotFound(macAddress);

    // Gateway-side liveness probe: a node is reachable while its heartbeat lease is valid
    var status = registry.GetStatus(device.MacAddress, DateTime.UtcNow);
    var latest = registry.GetLatestTelemetry(device.MacAddress);
    return Results.Ok(new
    {
        device.MacAddress,
        Reachable = status.IsOnline,
        status.SecondsSinceLastPacket,
        status.LeaseSeconds,
        LastSignalStrengthDbm = latest?.SignalStrengthDbm,
        LastBatteryPercent = latest?.BatteryPercent,
        CheckedAt = DateTime.UtcNow
    });
});

app.MapPost("/api/sensors/{macAddress}/buffer/restart", (string macAddress, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    var device = FindDevice(registry, macAddress);
    if (device == null) return DeviceNotFound(macAddress);

    anomalyService.ResetWindow(device.MacAddress);
    registry.RestartIngestion(device.MacAddress);
    return Results.Ok(new { message = $"Ingestion buffer and rolling baseline restarted for {device.MacAddress}" });
});

app.MapPost("/api/sensors/{macAddress}/alerts/acknowledge", (string macAddress, AcknowledgeAlertRequest req, DeviceRegistryService registry) =>
{
    var device = FindDevice(registry, macAddress);
    if (device == null) return DeviceNotFound(macAddress);

    var acknowledged = registry.AcknowledgeAlerts(device.MacAddress, string.IsNullOrWhiteSpace(req.AcknowledgedBy) ? "Operator-Gateway" : req.AcknowledgedBy.Trim(), req.Notes);
    return Results.Ok(new { AcknowledgedCount = acknowledged.Count, Alerts = acknowledged, MeanTimeToDetectSeconds = registry.GetMeanTimeToDetectSeconds() });
});

// 7. Multipart Attachment Upload with AES-256 Encryption at Rest
app.MapPost("/api/sensors/{macAddress}/attachments", async (string macAddress, IFormFile? file, DeviceRegistryService registry, AttachmentService attachments) =>
{
    var device = FindDevice(registry, macAddress);
    if (device == null) return DeviceNotFound(macAddress);

    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "No file uploaded." });

    if (!AttachmentService.TrySanitizeFileName(file.FileName, out var safeName, out var error))
        return Results.BadRequest(new { error });

    await using var uploadStream = file.OpenReadStream();
    var attachment = await attachments.SaveAsync(device.MacAddress, safeName, uploadStream, file.Length);
    return Results.Ok(attachment);
}).DisableAntiforgery();

// Attachment Download Endpoint with HMAC verification and on-the-fly streaming AES-256 Decryption
app.MapGet("/api/sensors/{macAddress}/attachments/{fileName}", async (string macAddress, string fileName, HttpContext http, DeviceRegistryService registry, AttachmentService attachments) =>
{
    var device = FindDevice(registry, macAddress);
    if (device == null) return DeviceNotFound(macAddress);

    try
    {
        var opened = await attachments.OpenAsync(device.MacAddress, fileName);
        if (opened == null)
            return Results.NotFound(new { error = "Attachment file not found." });

        var (content, attachment) = opened.Value;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(content, attachment.MimeType, attachment.FileName);
    }
    catch (CryptographicException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Attachment integrity verification failed");
    }
});

// 8. Dynamic Engagement: User-Reported Issue Triage Endpoints
app.MapGet("/api/issues", (string? macAddress, IncidentStatus? status, IncidentTriageService incidentService) =>
{
    return Results.Ok(incidentService.GetAllIssues(macAddress, status));
});

app.MapPost("/api/issues", (IncidentIssue issue, IncidentTriageService incidentService, DeviceRegistryService registry) =>
{
    if (FindDevice(registry, issue.MacAddress) == null)
        return Results.BadRequest(new { error = $"Sensor '{issue.MacAddress}' is not registered on this gateway." });

    try
    {
        return Results.Ok(incidentService.ReportIssue(issue));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message.Split(" (Parameter")[0] });
    }
});

app.MapPatch("/api/issues/{id}/status", (string id, UpdateIssueStatusRequest req, IncidentTriageService incidentService) =>
{
    try
    {
        var updated = incidentService.UpdateStatus(id, req.Status, req.Notes);
        if (updated == null) return Results.NotFound(new { error = "Issue not found." });
        return Results.Ok(updated);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message.Split(" (Parameter")[0] });
    }
});

app.MapGet("/api/issues/stats", (IncidentTriageService incidentService, DeviceRegistryService registry) =>
{
    return Results.Ok(incidentService.GetEngagementStats(registry.GetMeanTimeToDetectSeconds()));
});

// 9. Recursive Topology Validation Endpoints
app.MapPost("/api/topology/validate", (DeploymentNode root) =>
{
    var auditTrail = new List<string>();
    var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>(), auditTrail);
    var body = new { IsValid = isValid, Reason = reason, AuditTrail = auditTrail };
    return isValid ? Results.Ok(body) : Results.BadRequest(body);
});

// Live deployment hierarchy built from registered devices, with bubbled status and per-zone SRE indices
app.MapGet("/api/topology/live", (DeviceRegistryService registry) =>
{
    var now = DateTime.UtcNow;
    var inputs = registry.GetAllDevices()
        .Select(d => new DeviceTopologyInput(d, registry.GetStatus(d.MacAddress, now), registry.GetActiveAlerts(d.MacAddress).Count))
        .ToList();

    var root = TopologyEngine.BuildLiveTopology(inputs);
    var auditTrail = new List<string>();
    var (isValid, reason) = TopologyEngine.ValidateHierarchy(TopologyEngine.ToDeploymentNode(root), new HashSet<string>(), auditTrail);

    return Results.Ok(new { Root = root, Validation = new { IsValid = isValid, Reason = reason, AuditTrail = auditTrail } });
});

// 10. Simulation Controls Endpoints
app.MapPost("/api/simulation/spike", (SpikeRequest req, DeviceRegistryService registry, SimulationControlService simulation) =>
{
    var device = FindDevice(registry, req.MacAddress);
    if (device == null) return DeviceNotFound(req.MacAddress);
    if (req.Multiplier is <= AnomalyDetectionService.ZScoreThreshold or > 50)
        return Results.BadRequest(new { error = $"Multiplier must be greater than {AnomalyDetectionService.ZScoreThreshold} and at most 50 standard deviations." });

    simulation.ScheduleSpike(device.MacAddress, req.Multiplier);
    return Results.Ok(new { message = $"+{req.Multiplier}σ spike scheduled for {device.MacAddress}" });
});

app.MapPost("/api/simulation/drop-heartbeat", (DropHeartbeatRequest req, DeviceRegistryService registry, SimulationControlService simulation) =>
{
    var device = FindDevice(registry, req.MacAddress);
    if (device == null) return DeviceNotFound(req.MacAddress);

    simulation.DropHeartbeat(device.MacAddress);
    return Results.Ok(new { message = $"Heartbeat dropped for {device.MacAddress}" });
});

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

app.MapPost("/api/simulation/reset", async (DeviceRegistryService registry, AnomalyDetectionService anomalyService, SimulationControlService simulation, DataSeeder seeder) =>
{
    simulation.Reset();
    anomalyService.ResetAll();
    registry.ResetRuntimeState();
    await seeder.SeedAsync();
    return Results.Ok(new { message = "Reset successful" });
});

app.Run();

// DTOs for endpoints
public record BatchIngestionRequest(string MacAddress, int ChannelIndex, double[] Readings, string MetricUnit);
public record SpikeRequest(string MacAddress, double Multiplier = 4.5);
public record DropHeartbeatRequest(string MacAddress);
public record PowerAggregationRequest(double WattsMeter1, double WattsMeter2);
public record UpdateIssueStatusRequest(IncidentStatus Status, string? Notes = null);
public record AcknowledgeAlertRequest(string? Notes = null, string? AcknowledgedBy = null);

// Exposes the entry point to WebApplicationFactory-based integration tests
public partial class Program { }
