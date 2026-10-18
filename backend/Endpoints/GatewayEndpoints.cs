using System.Diagnostics;
using SmartX.Api.Models;
using SmartX.Api.Services;
using static SmartX.Api.Endpoints.EndpointHelpers;

namespace SmartX.Api.Endpoints;

public record UpdateIssueStatusRequest(IncidentStatus Status, string? Notes = null);

/// <summary>Gateway health, incident triage and topology endpoints (Part 1 features).</summary>
public static class GatewayEndpoints
{
    public static void MapGatewayEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", (DeviceRegistryService registry, JaggedTelemetryBuffer buffer, IncidentTriageService incidents,
            AnomalyDetectionService anomaly, AlertStateTracker alerts) =>
        {
            var now = DateTime.UtcNow;
            var devices = registry.GetAllDevices();
            var statuses = devices.Select(d => registry.GetStatus(d.MacAddress, now)).ToList();
            var deliveryScores = statuses.Where(s => s.PacketDeliveryPercent.HasValue).Select(s => s.PacketDeliveryPercent!.Value).ToList();
            var metrics = buffer.GetBufferMetrics();
            var meanTimeToDetect = alerts.GetMeanTimeToDetectSeconds();
            int onlineCount = statuses.Count(s => s.IsOnline);

            return Results.Ok(new
            {
                Status = "ONLINE",
                GatewayVersion = "Smart-X v2.5-Stream (.NET 10 Minimal API)",
                UptimeSeconds = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
                ActiveDeviceCount = onlineCount,
                RegisteredDeviceCount = devices.Count,
                AvailabilityPercent = devices.Count == 0 ? (double?)null : Math.Round(onlineCount * 100.0 / devices.Count, 1),
                PacketDeliveryPercent = deliveryScores.Count == 0 ? (double?)null : Math.Round(deliveryScores.Average(), 1),
                TotalTelemetryPackets = registry.TotalPacketsReceived,
                TotalIngestedPackets = metrics.TotalIngested,
                JaggedBufferedReadings = buffer.GetChannelStatus().TotalBufferedReadings,
                BufferMetrics = metrics,
                AnomaliesLastMinute = anomaly.CountAnomaliesSince(now.AddMinutes(-1)),
                ActiveAlertCount = alerts.GetStats().ActiveAlerts,
                MeanTimeToDetectSeconds = meanTimeToDetect,
                LeaseSeconds = GatewayTiming.LeaseDuration.TotalSeconds,
                OperatorEngagement = incidents.GetEngagementStats(meanTimeToDetect),
                Timestamp = now
            });
        });

        // User-reported incident triage (List<IncidentIssue>)
        app.MapGet("/api/issues", (string? macAddress, IncidentStatus? status, IncidentTriageService incidents) =>
            Results.Ok(incidents.GetAllIssues(macAddress, status)));

        app.MapPost("/api/issues", (IncidentIssue issue, IncidentTriageService incidents, DeviceRegistryService registry) =>
        {
            if (FindDevice(registry, issue.MacAddress) == null)
                return BadRequest($"Sensor '{issue.MacAddress}' is not registered on this gateway.");

            try
            {
                return Results.Ok(incidents.ReportIssue(issue));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(CleanMessage(ex));
            }
        });

        app.MapPatch("/api/issues/{id}/status", (string id, UpdateIssueStatusRequest req, IncidentTriageService incidents) =>
        {
            try
            {
                var updated = incidents.UpdateStatus(id, req.Status, req.Notes);
                return updated == null ? Results.NotFound(new { error = "Issue not found." }) : Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(CleanMessage(ex));
            }
        });

        app.MapGet("/api/issues/stats", (IncidentTriageService incidents, AlertStateTracker alerts) =>
            Results.Ok(incidents.GetEngagementStats(alerts.GetMeanTimeToDetectSeconds())));

        // Recursive topology validation
        app.MapPost("/api/topology/validate", (DeploymentNode root) =>
        {
            var auditTrail = new List<string>();
            var (isValid, reason) = TopologyEngine.ValidateHierarchy(root, new HashSet<string>(), auditTrail);
            var body = new { IsValid = isValid, Reason = reason, AuditTrail = auditTrail };
            return isValid ? Results.Ok(body) : Results.BadRequest(body);
        });

        app.MapGet("/api/topology/live", (DeviceRegistryService registry, AlertStateTracker alerts) =>
        {
            var now = DateTime.UtcNow;
            var inputs = registry.GetAllDevices()
                .Select(d => new DeviceTopologyInput(d, registry.GetStatus(d.MacAddress, now), alerts.GetActiveAlerts(d.MacAddress).Count))
                .ToList();

            var root = TopologyEngine.BuildLiveTopology(inputs);
            var auditTrail = new List<string>();
            var (isValid, reason) = TopologyEngine.ValidateHierarchy(TopologyEngine.ToDeploymentNode(root), new HashSet<string>(), auditTrail);
            return Results.Ok(new { Root = root, Validation = new { IsValid = isValid, Reason = reason, AuditTrail = auditTrail } });
        });
    }
}
