using System.Security.Cryptography;
using SmartX.Api.Models;
using SmartX.Api.Services;
using SmartX.Api.Services.Realtime;
using SmartX.Api.Services.Recommendations;
using static SmartX.Api.Endpoints.EndpointHelpers;

namespace SmartX.Api.Endpoints;

public record AcknowledgeAlertRequest(string? Notes = null, string? AcknowledgedBy = null);

/// <summary>Device registry, timelines, search, HITL triage actions and encrypted attachments.</summary>
public static class DeviceEndpoints
{
    public static readonly string[] AlertStateFilters = { "all", "alerting", "critical", "offline", "nominal" };

    public static void MapDeviceEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sensors/register", (SensorDevice device, DeviceRegistryService registry, GatewayBroadcaster broadcaster) =>
        {
            if (string.IsNullOrWhiteSpace(device.MacAddress)) return BadRequest("MAC Address cannot be empty.");
            if (!DeviceRegistryService.TryNormalizeMac(device.MacAddress, out _)) return BadRequest("MAC Address must use XX:XX:XX:XX:XX:XX hex notation.");
            if (string.IsNullOrWhiteSpace(device.DeploymentLocation)) return BadRequest("Deployment location cannot be empty.");
            if (!Enum.IsDefined(device.Category)) return BadRequest("Unknown sensor category.");

            var registered = registry.RegisterDevice(device);
            broadcaster.MarkDeviceDirty(registered.MacAddress);
            return Results.Ok(registered);
        });

        app.MapGet("/api/sensors", (DeviceViewFactory views) => Results.Ok(views.BuildAll(DateTime.UtcNow)));

        // Search / filter by text, operational category and active alert state (O(1) set membership per device)
        app.MapGet("/api/devices/search", (string? q, SensorCategory? category, string? alertState, DeviceRegistryService registry,
            AlertStateTracker alerts, DeviceViewFactory views) =>
        {
            var state = (alertState ?? "all").Trim().ToLowerInvariant();
            if (!AlertStateFilters.Contains(state))
                return BadRequest($"alertState must be one of: {string.Join(", ", AlertStateFilters)}.");

            var alerting = alerts.GetAlertingDevices();
            var disconnected = alerts.GetDisconnected().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var critical = alerts.GetActiveAlerts().Where(a => a.Severity == AlertSeverity.Critical).Select(a => a.MacAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var term = q?.Trim();

            var matches = registry.GetAllDevices().Where(d =>
                (category == null || d.Category == category) &&
                (string.IsNullOrEmpty(term) || d.MacAddress.Contains(term, StringComparison.OrdinalIgnoreCase) || d.DeploymentLocation.Contains(term, StringComparison.OrdinalIgnoreCase)) &&
                state switch
                {
                    "alerting" => alerting.Contains(d.MacAddress) || disconnected.Contains(d.MacAddress),
                    "critical" => critical.Contains(d.MacAddress),
                    "offline" => disconnected.Contains(d.MacAddress),
                    "nominal" => !alerting.Contains(d.MacAddress) && !disconnected.Contains(d.MacAddress),
                    _ => true
                });

            var now = DateTime.UtcNow;
            return Results.Ok(matches.Select(d => views.Build(d, now)).ToList());
        });

        // Operator search log: feeds the predictive pattern analysis
        app.MapPost("/api/operator/searches", (SearchEventRequest req, RecommendationService recommendations) =>
        {
            if (string.IsNullOrWhiteSpace(req.Query)) return BadRequest("Query is required.");
            int learned = recommendations.RecordSearch(req.Query, req.MatchedMacs ?? new List<string>());
            return Results.Ok(new { Learned = learned });
        });

        app.MapGet("/api/operator/events", (int? limit, RecommendationService recommendations) =>
            Results.Ok(recommendations.Model.GetHistory(Math.Clamp(limit ?? 30, 1, 300))));

        // Preceding readings (chronological) from the device's SortedDictionary timeline
        app.MapGet("/api/sensors/{macAddress}/history", (string macAddress, int? count, DeviceRegistryService registry) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);
            return Results.Ok(registry.GetHistory(device.MacAddress, Math.Clamp(count ?? 60, 1, DeviceRegistryService.TimelineCapacity)));
        });

        // Time-windowed, downsampled timeline for graphs
        app.MapGet("/api/sensors/{macAddress}/timeline", (string macAddress, int? minutes, int? maxPoints, DeviceRegistryService registry) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);

            var to = DateTime.UtcNow;
            var from = to.AddMinutes(-Math.Clamp(minutes ?? 5, 1, 30));
            return Results.Ok(registry.GetTimeline(device.MacAddress, from, to, Math.Clamp(maxPoints ?? 120, 10, 600)));
        });

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

        app.MapPost("/api/sensors/{macAddress}/buffer/restart", (string macAddress, DeviceRegistryService registry, AnomalyDetectionService anomaly, GatewayBroadcaster broadcaster) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);

            anomaly.ResetWindow(device.MacAddress);
            registry.RestartIngestion(device.MacAddress);
            broadcaster.MarkDeviceDirty(device.MacAddress);
            return Results.Ok(new { message = $"Ingestion buffer and rolling baseline restarted for {device.MacAddress}" });
        });

        app.MapPost("/api/sensors/{macAddress}/alerts/acknowledge", (string macAddress, AcknowledgeAlertRequest req, DeviceRegistryService registry,
            AlertStateTracker alerts, GatewayBroadcaster broadcaster) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);

            var acknowledged = alerts.Acknowledge(device.MacAddress, string.IsNullOrWhiteSpace(req.AcknowledgedBy) ? "Operator-Gateway" : req.AcknowledgedBy.Trim(), req.Notes);
            broadcaster.MarkDeviceDirty(device.MacAddress);
            return Results.Ok(new { AcknowledgedCount = acknowledged.Count, Alerts = acknowledged, MeanTimeToDetectSeconds = alerts.GetMeanTimeToDetectSeconds() });
        });

        // Multipart Attachment Upload with AES-256 Encryption at Rest
        app.MapPost("/api/sensors/{macAddress}/attachments", async (string macAddress, IFormFile? file, DeviceRegistryService registry, AttachmentService attachments,
            GatewayBroadcaster broadcaster) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);

            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");
            if (!AttachmentService.TrySanitizeFileName(file.FileName, out var safeName, out var error)) return BadRequest(error);

            await using var uploadStream = file.OpenReadStream();
            var attachment = await attachments.SaveAsync(device.MacAddress, safeName, uploadStream, file.Length);
            broadcaster.MarkDeviceDirty(device.MacAddress);
            return Results.Ok(attachment);
        }).DisableAntiforgery();

        // Attachment Download with HMAC verification and on-the-fly streaming AES-256 Decryption
        app.MapGet("/api/sensors/{macAddress}/attachments/{fileName}", async (string macAddress, string fileName, HttpContext http, DeviceRegistryService registry,
            AttachmentService attachments) =>
        {
            var device = FindDevice(registry, macAddress);
            if (device == null) return DeviceNotFound(macAddress);

            try
            {
                var opened = await attachments.OpenAsync(device.MacAddress, fileName);
                if (opened == null) return Results.NotFound(new { error = "Attachment file not found." });

                var (content, attachment) = opened.Value;
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(content, attachment.MimeType, attachment.FileName);
            }
            catch (CryptographicException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Attachment integrity verification failed");
            }
        });
    }
}

public record SearchEventRequest(string Query, List<string>? MatchedMacs);
