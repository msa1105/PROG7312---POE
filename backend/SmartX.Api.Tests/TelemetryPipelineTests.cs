using SmartX.Api.Models;
using SmartX.Api.Services;
using Xunit;

namespace SmartX.Api.Tests;

public class TelemetryPipelineTests
{
    private const string PowerMac = "24:6F:28:9A:12:03";
    private const string EnvMac = "24:6F:28:9A:12:01";

    private static (DeviceRegistryService Registry, AnomalyDetectionService Anomaly, SimulationControlService Simulation, TelemetrySimulationWorker Worker) CreatePipeline()
    {
        var registry = new DeviceRegistryService();
        var anomaly = new AnomalyDetectionService();
        var simulation = new SimulationControlService();
        registry.RegisterDevice(new SensorDevice { MacAddress = PowerMac, Category = SensorCategory.PowerConsumption, DeploymentLocation = "Facility Alpha > Substation Main" });
        registry.RegisterDevice(new SensorDevice { MacAddress = EnvMac, Category = SensorCategory.Environmental, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Rack A" });
        return (registry, anomaly, simulation, new TelemetrySimulationWorker(registry, anomaly, simulation));
    }

    [Fact]
    public void AnomalyDetection_FlagsOutlierBeyondThreeSigma_AndReportsBaseline()
    {
        var service = new AnomalyDetectionService();
        double[] pattern = { 400, 402, 398, 401, 399, 400, 403, 397 };
        for (int i = 0; i < AnomalyDetectionService.MinimumSamples + 5; i++)
        {
            Assert.False(service.Evaluate("mac", pattern[i % pattern.Length]).IsAnomaly);
        }

        var normal = service.Evaluate("mac", 401);
        Assert.False(normal.IsAnomaly);
        Assert.NotNull(normal.BaselineMean);

        var spike = service.Evaluate("mac", 450);
        Assert.True(spike.IsAnomaly);
        Assert.True(spike.ZScore > AnomalyDetectionService.ZScoreThreshold);
        Assert.Equal(1, service.CountAnomaliesSince(DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void Worker_EmitsPowerMetricPackets_WithTransmissionMetadata()
    {
        var (registry, _, _, worker) = CreatePipeline();

        worker.Tick();

        var latest = registry.GetLatestTelemetry(PowerMac);
        Assert.NotNull(latest);
        Assert.Equal("PowerMetric", latest.PayloadType);
        Assert.IsType<double>(latest.Value);
        Assert.InRange((double)latest.Value!, 380.0, 420.0);
        Assert.NotNull(latest.SignalStrengthDbm);
        Assert.NotNull(latest.BatteryPercent);
        Assert.Equal("float", registry.GetLatestTelemetry(EnvMac)!.PayloadType);
    }

    [Fact]
    public void Worker_InjectedSigmaSpike_IsFlagged_AndLatchesAlertUntilAcknowledged()
    {
        var (registry, _, simulation, worker) = CreatePipeline();
        for (int i = 0; i < AnomalyDetectionService.MinimumSamples + 5; i++) worker.Tick();

        simulation.ScheduleSpike(PowerMac, 4.5);
        worker.Tick();

        var latest = registry.GetLatestTelemetry(PowerMac)!;
        Assert.True(latest.IsAnomaly);
        Assert.InRange(latest.ZScore!.Value, 4.4, 4.6);
        Assert.NotNull(latest.RollingStdDev);

        // The spike is one-shot, but the alert stays latched after normal readings resume
        worker.Tick();
        Assert.False(registry.GetLatestTelemetry(PowerMac)!.IsAnomaly);
        var alert = Assert.Single(registry.GetActiveAlerts(PowerMac));
        Assert.Equal(AlertKind.Anomaly, alert.Kind);
        Assert.True(alert.Deviation > 0);

        var acknowledged = registry.AcknowledgeAlerts(PowerMac, "tester", "Inspected meter");
        Assert.Single(acknowledged);
        Assert.Empty(registry.GetActiveAlerts(PowerMac));
        Assert.NotNull(registry.GetMeanTimeToDetectSeconds());
    }

    [Fact]
    public void Registry_HeartbeatLease_ExpiresAfterTwoTransmissionWindows()
    {
        var (registry, _, _, worker) = CreatePipeline();
        worker.Tick();
        var now = DateTime.UtcNow;

        Assert.True(registry.GetStatus(EnvMac, now.AddSeconds(1)).IsOnline);

        var later = now + GatewayTiming.LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.False(registry.GetStatus(EnvMac, later).IsOnline);

        registry.CheckHeartbeatLeases(later);
        var alert = Assert.Single(registry.GetActiveAlerts(EnvMac));
        Assert.Equal(AlertKind.HeartbeatLost, alert.Kind);

        // Repeated lease checks do not duplicate the latched alert
        registry.CheckHeartbeatLeases(later.AddSeconds(5));
        Assert.Single(registry.GetActiveAlerts(EnvMac));
    }

    [Fact]
    public void Registry_PacketDeliveryScore_DropsWhenTransmissionsAreMissed()
    {
        var (registry, _, _, worker) = CreatePipeline();
        worker.Tick();

        var status = registry.GetStatus(EnvMac, DateTime.UtcNow.AddSeconds(30));
        Assert.NotNull(status.PacketDeliveryPercent);
        Assert.True(status.PacketDeliveryPercent < 10, $"Expected low delivery, got {status.PacketDeliveryPercent}");
        Assert.Equal(1, status.PacketsReceived);
    }

    [Fact]
    public void Worker_DroppedHeartbeat_StopsTelemetryForThatNodeOnly()
    {
        var (registry, _, simulation, worker) = CreatePipeline();
        simulation.DropHeartbeat(EnvMac);

        worker.Tick();

        Assert.Null(registry.GetLatestTelemetry(EnvMac));
        Assert.NotNull(registry.GetLatestTelemetry(PowerMac));
    }

    [Fact]
    public void Registry_History_UsesBoundedCollection_AndRestartClearsIt()
    {
        var (registry, anomaly, _, worker) = CreatePipeline();
        for (int i = 0; i < DeviceRegistryService.HistoryCapacity + 5; i++) worker.Tick();

        Assert.Equal(DeviceRegistryService.HistoryCapacity, registry.GetHistory(PowerMac).Count);

        anomaly.ResetWindow(PowerMac);
        registry.RestartIngestion(PowerMac);
        Assert.Empty(registry.GetHistory(PowerMac));
        Assert.Null(anomaly.GetBaseline(PowerMac));
        Assert.NotNull(registry.GetLatestTelemetry(PowerMac)); // last known packet is retained
    }

    [Fact]
    public void Registry_RegisterDevice_IgnoresClientAttachments_AndRejectsBadMac()
    {
        var registry = new DeviceRegistryService();
        var registered = registry.RegisterDevice(new SensorDevice
        {
            MacAddress = "aa:bb:cc:dd:ee:ff",
            DeploymentLocation = "Facility > Zone > Node",
            Attachments = { new DeviceAttachment { FileName = "x.log", FilePath = @"C:\Windows\win.ini" } }
        });

        Assert.Equal("AA:BB:CC:DD:EE:FF", registered.MacAddress);
        Assert.Empty(registered.Attachments);
        Assert.Throws<ArgumentException>(() => registry.RegisterDevice(new SensorDevice { MacAddress = "not-a-mac" }));
    }

    [Fact]
    public void JaggedBuffer_Flush_DrainsChannels_AndSupportsSingleChannelFlush()
    {
        var buffer = new JaggedTelemetryBuffer();
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            buffer.LoadChannelBatch(ch, Enumerable.Repeat(400.0 + ch, 10).ToArray());
        }

        var single = buffer.FlushToList(PowerMac, "W", channelIndex: 1);
        Assert.Equal(10, single.Count);
        Assert.All(single, p => Assert.Equal(401.0, p.Value));

        var status = buffer.GetChannelStatus();
        Assert.Equal(0, status.Channels[1].AllocatedLength);
        Assert.Equal(30, status.TotalBufferedReadings);

        Assert.Equal(30, buffer.FlushToList(PowerMac, "W").Count);
        Assert.Empty(buffer.FlushToList(PowerMac, "W")); // nothing is ingested twice
        Assert.Equal(40, buffer.GetBufferMetrics().TotalIngested);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.LoadChannelBatch(4, new double[1]));
    }

    [Fact]
    public void IncidentTriage_AssignsServerIds_TracksTriageTime_AndValidatesStatus()
    {
        var service = new IncidentTriageService();
        var first = service.ReportIssue(new IncidentIssue { Id = "client-chosen", MacAddress = PowerMac, Title = "A", Severity = IncidentSeverity.Low });
        var second = service.ReportIssue(new IncidentIssue { MacAddress = PowerMac, Title = "B", Severity = IncidentSeverity.Critical });

        Assert.Equal("ISS-1004", first.Id);
        Assert.Equal("ISS-1005", second.Id);
        Assert.Equal(25, first.XpAwarded);
        Assert.Equal(150, second.XpAwarded);

        var triaged = service.UpdateStatus(first.Id, IncidentStatus.InTriage)!;
        Assert.NotNull(triaged.TriagedAt);

        var reopened = service.UpdateStatus(first.Id, IncidentStatus.Reported)!;
        Assert.Null(reopened.TriagedAt);
        Assert.Null(reopened.ResolvedAt);

        Assert.Throws<ArgumentException>(() => service.UpdateStatus(first.Id, (IncidentStatus)7));
        Assert.Throws<ArgumentException>(() => service.ReportIssue(new IncidentIssue { MacAddress = PowerMac, Title = " " }));

        var stats = service.GetEngagementStats(meanTimeToDetectSeconds: 12.5);
        Assert.NotNull(stats.MeanTimeToTriageSeconds);
        Assert.Equal(12.5, stats.MeanTimeToDetectSeconds);
    }
}
