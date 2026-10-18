using Microsoft.Extensions.Logging.Abstractions;
using SmartX.Api.Models;
using SmartX.Api.Services;
using SmartX.Api.Services.Ingestion;
using SmartX.Api.Services.Realtime;
using Xunit;

namespace SmartX.Api.Tests;

/// <summary>End-to-end processing pipeline (queue → worker → registry/timeline → alert sets) without HTTP.</summary>
public class TelemetryPipelineTests
{
    private const string PowerMac = "24:6F:28:9A:12:03";
    private const string EnvMac = "24:6F:28:9A:12:01";

    private sealed class Pipeline
    {
        public DeviceRegistryService Registry { get; } = new();
        public AnomalyDetectionService Anomaly { get; } = new();
        public AlertStateTracker Alerts { get; } = new();
        public TelemetryIngestionQueue Queue { get; } = new();
        public TelemetryProcessingWorker Worker { get; }
        public LeaseMonitorWorker Leases { get; }

        public Pipeline()
        {
            Registry.RegisterDevice(new SensorDevice { MacAddress = PowerMac, Category = SensorCategory.PowerConsumption, DeploymentLocation = "Facility Alpha > Substation Main" });
            Registry.RegisterDevice(new SensorDevice { MacAddress = EnvMac, Category = SensorCategory.Environmental, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Rack A" });
            Worker = new TelemetryProcessingWorker(Queue, Registry, Anomaly, Alerts, NullDeviceChangeNotifier.Instance, NullLogger<TelemetryProcessingWorker>.Instance);
            Leases = new LeaseMonitorWorker(Registry, Alerts, new CommandDispatcher(), NullDeviceChangeNotifier.Instance);
        }

        /// <summary>Simulates the ingest endpoint: classify, heartbeat, enqueue.</summary>
        public void Ingest(TelemetryWorkItem item, SensorCategory category)
        {
            Registry.RecordHeartbeat(item.MacAddress);
            Queue.Enqueue(item, CriticalReadingClassifier.Classify(category, item));
        }

        public void IngestPower(double watts, DateTime? timestamp = null) => Ingest(TelemetryWorkItems.From(new TelemetryPacket<PowerMetric>
        {
            MacAddress = PowerMac, Value = new PowerMetric(watts), MetricUnit = "W", Timestamp = timestamp ?? DateTime.UtcNow, RelayOn = true, SignalStrengthDbm = -60, BatteryPercent = 88
        }), SensorCategory.PowerConsumption);

        public void IngestMoisture(float value) => Ingest(TelemetryWorkItems.From(new TelemetryPacket<float> { MacAddress = EnvMac, Value = value, MetricUnit = "%" }), SensorCategory.Environmental);

        public int Drain()
        {
            int processed = 0;
            while (Queue.TryDequeue(out var envelope))
            {
                Worker.Process(envelope);
                processed++;
            }
            return processed;
        }
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

        var spike = service.Evaluate("mac", 450);
        Assert.True(spike.IsAnomaly);
        Assert.True(spike.ZScore > AnomalyDetectionService.ZScoreThreshold);
        Assert.Equal(1, service.CountAnomaliesSince(DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void Pipeline_ProcessesPowerMetricPackets_IntoSnapshotWithMetadata()
    {
        var p = new Pipeline();
        p.IngestPower(401.5);
        Assert.Equal(1, p.Drain());

        var latest = p.Registry.GetLatestTelemetry(PowerMac)!;
        Assert.Equal("PowerMetric", latest.PayloadType);
        Assert.Equal(401.5, latest.Value);
        Assert.Equal(-60, latest.SignalStrengthDbm);
        Assert.True(p.Registry.GetReportedState(PowerMac));
    }

    [Fact]
    public void Pipeline_SigmaSpike_RaisesOneLatchedAnomalyAlert_UntilAcknowledged()
    {
        var p = new Pipeline();
        var random = new Random(7);
        for (int i = 0; i < AnomalyDetectionService.MinimumSamples + 10; i++) p.IngestPower(380 + random.NextDouble() * 40);
        p.Drain();

        p.IngestPower(900);
        p.IngestPower(905);
        p.Drain();

        var alert = Assert.Single(p.Alerts.GetActiveAlerts(PowerMac), a => a.Kind == AlertKind.Anomaly);
        Assert.True(alert.Deviation > 0);
        Assert.True(p.Alerts.GetStats().DuplicateAlertsSuppressed >= 1); // the second spike did not raise a second alert

        p.Alerts.Acknowledge(PowerMac, "tester", "Inspected meter");
        Assert.Empty(p.Alerts.GetActiveAlerts(PowerMac));
        Assert.NotNull(p.Alerts.GetMeanTimeToDetectSeconds());
    }

    [Fact]
    public void Pipeline_CriticalReading_TakesPriorityLane_AndRaisesThresholdAlert()
    {
        var p = new Pipeline();
        for (int i = 0; i < 50; i++) p.IngestPower(400);
        p.IngestMoisture(9.5f);

        Assert.True(p.Queue.TryDequeue(out var first));
        Assert.Equal(IngestionLane.Priority, first.Lane);
        Assert.Equal(EnvMac, first.Item.MacAddress);
        p.Worker.Process(first);
        p.Drain();

        var alert = Assert.Single(p.Alerts.GetActiveAlerts(EnvMac));
        Assert.Equal(AlertKind.ThresholdBreach, alert.Kind);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(50, p.Queue.GetStats().LastBypass!.OvertookStandardPackets);
    }

    [Fact]
    public void Timeline_KeepsChronologicalOrder_WhenPacketsAreProcessedOutOfOrder()
    {
        var p = new Pipeline();
        var now = DateTime.UtcNow;
        p.IngestPower(400, now.AddSeconds(-3));
        p.IngestPower(1600, now.AddSeconds(-1)); // critical -> processed first
        p.IngestPower(401, now.AddSeconds(-2));
        p.Drain();

        var history = p.Registry.GetHistory(PowerMac);
        Assert.Equal(new[] { 400.0, 401.0, 1600.0 }, history.Select(s => (double)s.Value!));
        Assert.Equal(1600.0, p.Registry.GetLatestTelemetry(PowerMac)!.Value); // latest by timestamp, not by processing order
    }

    [Fact]
    public void LeaseMonitor_MarksNodeOfflineOnce_AndHeartbeatClearsIt()
    {
        var p = new Pipeline();
        p.IngestMoisture(50);
        p.Drain();

        var later = DateTime.UtcNow + GatewayTiming.LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.False(p.Registry.GetStatus(EnvMac, later).IsOnline);

        p.Leases.CheckLeases(later);
        p.Leases.CheckLeases(later.AddSeconds(1));
        p.Leases.CheckLeases(later.AddSeconds(2));

        Assert.Equal(new[] { EnvMac }, p.Alerts.GetDisconnected());
        Assert.Single(p.Alerts.GetActiveAlerts(EnvMac), a => a.Kind == AlertKind.HeartbeatLost);
        Assert.Equal(2, p.Alerts.GetStats().DuplicateDisconnectsSuppressed);

        Assert.True(p.Alerts.MarkReconnected(EnvMac));
        Assert.Empty(p.Alerts.GetDisconnected());
    }

    [Fact]
    public void Heartbeat_IsRecordedOnArrival_NotProcessing()
    {
        var p = new Pipeline();
        p.IngestMoisture(50); // queued, not yet processed

        Assert.True(p.Registry.GetStatus(EnvMac, DateTime.UtcNow).IsOnline);
        Assert.Null(p.Registry.GetLatestTelemetry(EnvMac));
    }

    [Fact]
    public void Registry_PacketDeliveryScore_DropsWhenTransmissionsAreMissed()
    {
        var registry = new DeviceRegistryService();
        registry.RegisterDevice(new SensorDevice { MacAddress = EnvMac, DeploymentLocation = "F > Z > N" });
        registry.RecordHeartbeat(EnvMac);

        var status = registry.GetStatus(EnvMac, DateTime.UtcNow.AddSeconds(30));
        Assert.True(status.PacketDeliveryPercent < 10, $"Expected low delivery, got {status.PacketDeliveryPercent}");
    }

    [Fact]
    public void Registry_RestartIngestion_ClearsTimelineButKeepsLastReading()
    {
        var p = new Pipeline();
        for (int i = 0; i < 30; i++) p.IngestPower(400 + i);
        p.Drain();

        p.Anomaly.ResetWindow(PowerMac);
        p.Registry.RestartIngestion(PowerMac);

        Assert.Single(p.Registry.GetHistory(PowerMac));
        Assert.Null(p.Anomaly.GetBaseline(PowerMac));
        Assert.Equal(429.0, p.Registry.GetLatestTelemetry(PowerMac)!.Value);
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
        Assert.NotNull(registry.GetDevice("aa:bb:cc:dd:ee:ff")); // case-insensitive O(1) lookup
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
        Assert.All(single, packet => Assert.Equal(401.0, packet.Value));
        Assert.Equal(30, buffer.GetChannelStatus().TotalBufferedReadings);

        Assert.Equal(30, buffer.FlushToList(PowerMac, "W").Count);
        Assert.Empty(buffer.FlushToList(PowerMac, "W"));
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

        Assert.NotNull(service.UpdateStatus(first.Id, IncidentStatus.InTriage)!.TriagedAt);
        Assert.Null(service.UpdateStatus(first.Id, IncidentStatus.Reported)!.TriagedAt);
        Assert.Throws<ArgumentException>(() => service.UpdateStatus(first.Id, (IncidentStatus)7));
        Assert.Throws<ArgumentException>(() => service.ReportIssue(new IncidentIssue { MacAddress = PowerMac, Title = " " }));

        var stats = service.GetEngagementStats(meanTimeToDetectSeconds: 12.5);
        Assert.NotNull(stats.MeanTimeToTriageSeconds);
        Assert.Equal(12.5, stats.MeanTimeToDetectSeconds);
    }
}
