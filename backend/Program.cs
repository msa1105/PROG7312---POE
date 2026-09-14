using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddSingleton<DeviceRegistryService>();
builder.Services.AddSingleton<AnomalyDetectionService>();
builder.Services.AddSingleton<JaggedTelemetryBuffer>();
builder.Services.AddSingleton<IncidentTriageService>();
builder.Services.AddSingleton<FileEncryptionService>();
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
    seeder.Seed();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

// 1. Gateway Health & Status Endpoint (for Startup Interface & Landing Page)
app.MapGet("/api/health", (DeviceRegistryService registry, JaggedTelemetryBuffer buffer, IncidentTriageService incidentService) =>
{
    var devices = registry.GetAllDevices().ToList();
    var metrics = buffer.GetBufferMetrics();
    var engagement = incidentService.GetEngagementStats();
    
    return Results.Ok(new
    {
        Status = "ONLINE",
        GatewayVersion = "Smart-X v2.4-Core (.NET 10 Minimal API)",
        UptimeSeconds = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
        ActiveDeviceCount = devices.Count,
        TotalIngestedPackets = metrics.TotalIngested,
        OperatorEngagement = engagement,
        Timestamp = DateTime.UtcNow
    });
});

// 2. Ingestion Endpoints (Generics with TelemetryPacket<T>)
app.MapPost("/api/telemetry/ingest/float", (TelemetryPacket<float> packet, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    registry.UpdateLatestTelemetry(packet);
    bool isAnomaly = anomalyService.IsAnomaly(packet.MacAddress, packet.Value);
    return Results.Ok(new { packet.PacketId, isAnomaly });
});

app.MapPost("/api/telemetry/ingest/int", (TelemetryPacket<int> packet, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    registry.UpdateLatestTelemetry(packet);
    bool isAnomaly = anomalyService.IsAnomaly(packet.MacAddress, packet.Value);
    return Results.Ok(new { packet.PacketId, isAnomaly });
});

app.MapPost("/api/telemetry/ingest/bool", (TelemetryPacket<bool> packet, DeviceRegistryService registry) =>
{
    registry.UpdateLatestTelemetry(packet);
    return Results.Ok(new { packet.PacketId, isAnomaly = false });
});

// 3. Batch Buffer Endpoint (Jagged Arrays double[][])
app.MapPost("/api/telemetry/batch", (BatchIngestionRequest req, JaggedTelemetryBuffer buffer, DeviceRegistryService registry, AnomalyDetectionService anomalyService) =>
{
    buffer.LoadChannelBatch(req.ChannelIndex, req.Readings);
    var flushed = buffer.FlushToList(req.MacAddress, req.MetricUnit);
    
    var anomalies = new List<Guid>();
    foreach (var packet in flushed)
    {
        registry.UpdateLatestTelemetry(packet);
        if (anomalyService.IsAnomaly(packet.MacAddress, packet.Value))
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

    var registered = registry.RegisterDevice(device);
    return Results.Ok(registered);
});

app.MapGet("/api/sensors", (DeviceRegistryService registry) =>
{
    var devices = registry.GetAllDevices().Select(d => new
    {
        Device = d,
        LatestTelemetry = registry.GetLatestTelemetry(d.MacAddress)
    });
    return Results.Ok(devices);
});

// 6. Multipart Attachment Upload with AES-256 Encryption at Rest
app.MapPost("/api/sensors/{macAddress}/attachments", async (string macAddress, IFormFile file, DeviceRegistryService registry, FileEncryptionService encService) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded.");

    var allowedExtensions = new[] { ".bin", ".json", ".log", ".txt", ".jpg", ".png" };
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    if (!allowedExtensions.Contains(extension))
        return Results.BadRequest("File format not supported.");

    var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", macAddress.Replace(":", ""));
    Directory.CreateDirectory(uploadPath);

    var encryptedFilePath = Path.Combine(uploadPath, file.FileName + ".enc");
    using (var uploadStream = file.OpenReadStream())
    {
        await encService.EncryptStreamAsync(uploadStream, encryptedFilePath);
    }

    var attachment = new DeviceAttachment
    {
        FileName = file.FileName,
        FilePath = encryptedFilePath,
        SizeBytes = file.Length,
        MimeType = file.ContentType
    };

    registry.AddAttachment(macAddress, attachment);

    return Results.Ok(attachment);
}).DisableAntiforgery();

// Attachment Download Endpoint with on-the-fly streaming AES-256 Decryption
app.MapGet("/api/sensors/{macAddress}/attachments/{fileName}", async (string macAddress, string fileName, DeviceRegistryService registry, FileEncryptionService encService) =>
{
    var device = registry.GetDevice(macAddress);
    if (device == null) return Results.NotFound(new { error = "Device not found." });

    var attachment = device.Attachments.FirstOrDefault(a => a.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    if (attachment == null || !File.Exists(attachment.FilePath))
        return Results.NotFound(new { error = "Attachment file not found." });

    var decryptedStream = await encService.DecryptToStreamAsync(attachment.FilePath);
    return Results.File(decryptedStream, string.IsNullOrWhiteSpace(attachment.MimeType) ? "application/octet-stream" : attachment.MimeType, attachment.FileName);
});

// 7. Dynamic Engagement: User-Reported Issue Triage Endpoints
app.MapGet("/api/issues", (string? macAddress, IncidentStatus? status, IncidentTriageService incidentService) =>
{
    return Results.Ok(incidentService.GetAllIssues(macAddress, status));
});

app.MapPost("/api/issues", (IncidentIssue issue, IncidentTriageService incidentService) =>
{
    var created = incidentService.ReportIssue(issue);
    return Results.Ok(created);
});

app.MapPatch("/api/issues/{id}/status", (string id, UpdateIssueStatusRequest req, IncidentTriageService incidentService) =>
{
    var updated = incidentService.UpdateStatus(id, req.Status, req.Notes);
    if (updated == null) return Results.NotFound(new { error = "Issue not found." });
    return Results.Ok(updated);
});

app.MapGet("/api/issues/stats", (IncidentTriageService incidentService) =>
{
    return Results.Ok(incidentService.GetEngagementStats());
});

// 8. Recursive Topology Validation Endpoint
app.MapPost("/api/topology/validate", (DeploymentNode root) =>
{
    var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>());
    if (isValid) return Results.Ok(new { IsValid = true, Reason = reason });
    return Results.BadRequest(new { IsValid = false, Reason = reason });
});

// 9. Simulation Controls Endpoints
app.MapPost("/api/simulation/spike", (SpikeRequest req) =>
{
    TelemetrySimulationWorker.SpikeMacs.Add(req.MacAddress);
    return Results.Ok(new { message = $"Spike scheduled for {req.MacAddress}" });
});

app.MapPost("/api/simulation/drop-heartbeat", (DropHeartbeatRequest req) =>
{
    TelemetrySimulationWorker.DroppedMacs.Add(req.MacAddress);
    return Results.Ok(new { message = $"Heartbeat dropped for {req.MacAddress}" });
});

app.MapPost("/api/simulation/burst-batch", (JaggedTelemetryBuffer buffer) =>
{
    var watch = Stopwatch.StartNew();
    
    // Simulate ingest of 5000 items in a jagged buffer
    var rand = new Random();
    for (int ch = 0; ch < 4; ch++) 
    {
        var readings = new double[1250];
        for (int i = 0; i < 1250; i++) readings[i] = 400.0 + rand.NextDouble() * 20.0;
        buffer.LoadChannelBatch(ch, readings);
    }
    var flushed = buffer.FlushToList("24:6F:28:9A:12:03", "W");
    
    watch.Stop();
    return Results.Ok(new { ProcessedCount = flushed.Count, ElapsedMs = watch.ElapsedMilliseconds });
});

app.MapPost("/api/simulation/reset", (DeviceRegistryService registry, DataSeeder seeder) =>
{
    TelemetrySimulationWorker.DroppedMacs.Clear();
    TelemetrySimulationWorker.SpikeMacs.Clear();
    
    seeder.Seed();
    return Results.Ok(new { message = "Reset successful" });
});

app.Run();

// DTOs for endpoints
public record BatchIngestionRequest(string MacAddress, int ChannelIndex, double[] Readings, string MetricUnit);
public record SpikeRequest(string MacAddress, double Multiplier = 4.5);
public record DropHeartbeatRequest(string MacAddress);
public record PowerAggregationRequest(double WattsMeter1, double WattsMeter2);
public record UpdateIssueStatusRequest(IncidentStatus Status, string? Notes = null);
