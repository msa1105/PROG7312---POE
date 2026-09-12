using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddSingleton<DeviceRegistryService>();
builder.Services.AddSingleton<AnomalyDetectionService>();
builder.Services.AddSingleton<JaggedTelemetryBuffer>();

// Configure CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:3000") // Frontend URL
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

// Ingestion Endpoints
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

// Batch Buffer Endpoint
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

// Device Registration Endpoints
app.MapPost("/api/sensors/register", (SensorDevice device, DeviceRegistryService registry) =>
{
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

// Multipart Attachment Uploader Endpoint
app.MapPost("/api/sensors/{macAddress}/attachments", async (string macAddress, IFormFile file, DeviceRegistryService registry) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded.");

    var allowedExtensions = new[] { ".bin", ".json", ".log", ".txt", ".jpg", ".png" };
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    if (!allowedExtensions.Contains(extension))
        return Results.BadRequest("File format not supported.");

    var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", macAddress.Replace(":", ""));
    Directory.CreateDirectory(uploadPath);

    var filePath = Path.Combine(uploadPath, file.FileName);
    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    var attachment = new DeviceAttachment
    {
        FileName = file.FileName,
        FilePath = filePath,
        SizeBytes = file.Length,
        MimeType = file.ContentType
    };

    registry.AddAttachment(macAddress, attachment);

    return Results.Ok(attachment);
}).DisableAntiforgery();

// Topology Validation Endpoint
app.MapPost("/api/topology/validate", (DeploymentNode root) =>
{
    var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>());
    if (isValid) return Results.Ok(new { IsValid = true, Reason = reason });
    return Results.BadRequest(new { IsValid = false, Reason = reason });
});

app.Run();

// DTOs for endpoints
public record BatchIngestionRequest(string MacAddress, int ChannelIndex, double[] Readings, string MetricUnit);
