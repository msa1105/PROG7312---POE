using SmartX.Api.Models;
using SmartX.Api.Services;
using SmartX.Api.Services.Ingestion;
using Xunit;

namespace SmartX.Api.Tests;

/// <summary>Unit tests for the Part 2 collections: Queue, PriorityQueue, Stack, Dictionary, SortedDictionary and HashSet.</summary>
public class CommandStreamDataStructureTests
{
    private const string Valve = "AA:00:00:00:00:01";
    private const string Pump = "AA:00:00:00:00:02";

    private static TelemetryWorkItem Reading(string mac, double value, DateTime? at = null) =>
        TelemetryWorkItems.From(new TelemetryPacket<double> { MacAddress = mac, Value = value, MetricUnit = "%", Timestamp = at ?? DateTime.UtcNow });

    private static CriticalClassification Critical(string reason = "critical") => new(AlertSeverity.Critical, reason);
    private static CriticalClassification High(string reason = "high") => new(AlertSeverity.High, reason);

    // ---------- Queue<T> + PriorityQueue ----------

    [Fact]
    public void IngestionQueue_StandardLane_IsFirstInFirstOut()
    {
        var queue = new TelemetryIngestionQueue();
        for (int i = 0; i < 5; i++) queue.Enqueue(Reading(Valve, i), null);

        var order = new List<double>();
        while (queue.TryDequeue(out var envelope)) order.Add(envelope.Item.NumericValue!.Value);

        Assert.Equal(new[] { 0.0, 1, 2, 3, 4 }, order);
    }

    [Fact]
    public void IngestionQueue_PriorityLane_BypassesStandardBacklog_BySeverityThenArrival()
    {
        var queue = new TelemetryIngestionQueue();
        queue.Enqueue(Reading(Valve, 1), null);
        queue.Enqueue(Reading(Valve, 2), null);
        queue.Enqueue(Reading(Pump, 100), High("high-1"));
        queue.Enqueue(Reading(Pump, 200), Critical("critical-1"));
        queue.Enqueue(Reading(Pump, 300), Critical("critical-2"));

        var order = new List<string>();
        while (queue.TryDequeue(out var envelope)) order.Add(envelope.Classification?.Reason ?? $"std-{envelope.Item.NumericValue}");

        Assert.Equal(new[] { "critical-1", "critical-2", "high-1", "std-1", "std-2" }, order);

        var stats = queue.GetStats();
        Assert.Equal(3, stats.Priority.Processed);
        Assert.Equal(2, stats.Standard.Processed);
        Assert.Equal(2, stats.LastBypass!.OvertookStandardPackets);
    }

    [Fact]
    public void IngestionQueue_RejectsStandardPacketsWhenFull_ButAlwaysAcceptsCriticalOnes()
    {
        var queue = new TelemetryIngestionQueue(standardCapacity: 2);
        Assert.True(queue.Enqueue(Reading(Valve, 1), null).Accepted);
        Assert.True(queue.Enqueue(Reading(Valve, 2), null).Accepted);
        Assert.False(queue.Enqueue(Reading(Valve, 3), null).Accepted);
        Assert.True(queue.Enqueue(Reading(Valve, 4), Critical()).Accepted);
        Assert.Equal(1, queue.GetStats().Rejected);
    }

    [Theory]
    [InlineData(SensorCategory.Environmental, 9.0, AlertSeverity.Critical)]
    [InlineData(SensorCategory.Environmental, 18.0, AlertSeverity.High)]
    [InlineData(SensorCategory.PowerConsumption, 1800.0, AlertSeverity.Critical)]
    [InlineData(SensorCategory.PowerConsumption, 1200.0, AlertSeverity.High)]
    public void CriticalClassifier_RoutesThresholdBreaches(SensorCategory category, double value, AlertSeverity expected)
    {
        var result = CriticalReadingClassifier.Classify(category, Reading(Valve, value));
        Assert.NotNull(result);
        Assert.Equal(expected, result.Severity);
    }

    [Theory]
    [InlineData(SensorCategory.Environmental, 45.0)]
    [InlineData(SensorCategory.PowerConsumption, 410.0)]
    public void CriticalClassifier_LeavesNominalReadingsInStandardLane(SensorCategory category, double value) =>
        Assert.Null(CriticalReadingClassifier.Classify(category, Reading(Valve, value)));

    // ---------- Stack<T> undo + per-device Queue<T> outbox ----------

    private static CommandRequest Request(string mac, bool target, bool? previous) => new(mac, mac == Valve ? "Valve" : "Pump", target, previous);

    [Fact]
    public void Dispatcher_DeliversEachDeviceOutboxInIssueOrder()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.Issue("a", CommandSource.Operator, new[] { Request(Valve, true, false) });
        dispatcher.Issue("b", CommandSource.Operator, new[] { Request(Pump, true, false) });
        dispatcher.Issue("c", CommandSource.Operator, new[] { Request(Valve, false, true) });

        var valveCommands = dispatcher.DequeueForDevice(Valve);
        Assert.Equal(new[] { true, false }, valveCommands.Select(c => c.TargetState));
        Assert.All(valveCommands, c => Assert.Equal(CommandStatus.Delivered, c.Status));
        Assert.Empty(dispatcher.DequeueForDevice(Valve));
        Assert.Single(dispatcher.DequeueForDevice(Pump));
    }

    [Fact]
    public void Dispatcher_AcknowledgementCompletesCommand_OnlyForTheOwningDevice()
    {
        var dispatcher = new CommandDispatcher();
        var command = dispatcher.Issue("a", CommandSource.Operator, new[] { Request(Valve, true, false) }).Commands[0];
        dispatcher.DequeueForDevice(Valve);

        Assert.Null(dispatcher.Acknowledge(Pump, command.Id, true, true, "wrong device"));
        var acked = dispatcher.Acknowledge(Valve, command.Id, true, true, "opened")!;
        Assert.Equal(CommandStatus.Acknowledged, acked.Status);
        Assert.True(acked.AppliedState);
        Assert.Equal(0, dispatcher.PendingCount(Valve));
    }

    [Fact]
    public void Dispatcher_Undo_RevertsMostRecentBatchFirst_WithInverseCommands()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.Issue("Open valve", CommandSource.Operator, new[] { Request(Valve, true, false) });
        dispatcher.Issue("Shut down all", CommandSource.Operator, new[] { Request(Valve, false, true), Request(Pump, false, true) });

        var first = dispatcher.Undo()!.Value;
        Assert.Equal("Shut down all", first.Reverted.Label);
        Assert.Equal(2, first.UndoBatch.Commands.Count);
        Assert.All(first.UndoBatch.Commands, c => Assert.True(c.TargetState)); // restores both devices to ON
        Assert.Equal(CommandSource.Undo, first.UndoBatch.Source);

        var second = dispatcher.Undo()!.Value;
        Assert.Equal("Open valve", second.Reverted.Label);
        Assert.False(second.UndoBatch.Commands.Single().TargetState);

        Assert.Null(dispatcher.Undo()); // undo batches are not themselves pushed onto the stack
        Assert.Equal(0, dispatcher.GetUndoStack().Depth);
    }

    [Fact]
    public void Dispatcher_Undo_SkipsDevicesWhosePreviousStateIsUnknownOrUnchanged()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.Issue("mixed", CommandSource.Operator, new[] { Request(Valve, true, null), Request(Pump, true, true) });

        var result = dispatcher.Undo()!.Value;
        Assert.Empty(result.UndoBatch.Commands);
        Assert.Contains("no state change", result.UndoBatch.Label);
    }

    [Fact]
    public void Dispatcher_ExpiresCommandsThatAreNeverAcknowledged()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.Issue("a", CommandSource.Operator, new[] { Request(Valve, true, false) });

        Assert.Equal(1, dispatcher.ExpireStale(DateTime.UtcNow + CommandDispatcher.CommandTimeout + TimeSpan.FromSeconds(1)));
        Assert.Equal(CommandStatus.Expired, dispatcher.GetHistory(1)[0].Commands[0].Status);
        Assert.Empty(dispatcher.DequeueForDevice(Valve));
    }

    // ---------- SortedDictionary timeline ----------

    private static TelemetrySnapshot Snapshot(double value, DateTime at) =>
        new(Guid.NewGuid(), Valve, at, value, "%", "double", false, null, null, null, null, null);

    [Fact]
    public void Timeline_OrdersByTimestamp_AndEvictsOldestBeyondCapacity()
    {
        var timeline = new TelemetryTimeline(capacity: 3);
        var t0 = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        timeline.Add(Snapshot(3, t0.AddSeconds(3)));
        timeline.Add(Snapshot(1, t0.AddSeconds(1)));
        timeline.Add(Snapshot(2, t0.AddSeconds(2)));
        timeline.Add(Snapshot(4, t0.AddSeconds(4)));

        Assert.Equal(new[] { 2.0, 3, 4 }, timeline.TakeLast(10).Select(s => (double)s.Value!));
        Assert.Equal(4.0, timeline.Latest!.Value);
    }

    [Fact]
    public void Timeline_DuplicateTimestamps_AreKeptAsSeparateReadings()
    {
        var timeline = new TelemetryTimeline();
        var at = DateTime.UtcNow;
        timeline.Add(Snapshot(1, at));
        timeline.Add(Snapshot(2, at));

        Assert.Equal(2, timeline.Count);
    }

    [Fact]
    public void Timeline_Range_FiltersWindow_AndDownsamplesIntoBuckets()
    {
        var timeline = new TelemetryTimeline();
        var t0 = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 600; i++) timeline.Add(Snapshot(i, t0.AddSeconds(i)));

        var window = timeline.Range(t0.AddSeconds(100), t0.AddSeconds(199), maxPoints: 1000);
        Assert.Equal(100, window.Count);
        Assert.Equal(100.0, window[0].Value);

        var bucketed = timeline.Range(t0, t0.AddSeconds(599), maxPoints: 60);
        Assert.Equal(60, bucketed.Count);
        Assert.All(bucketed, p => Assert.Equal(10, p.Samples));
        Assert.Equal(bucketed.Select(p => p.Timestamp).OrderBy(t => t), bucketed.Select(p => p.Timestamp));
    }

    // ---------- HashSet alert tracking ----------

    private static DeviceAlert Alert(string mac, AlertKind kind) => new() { MacAddress = mac, Kind = kind, Message = "test" };

    [Fact]
    public void AlertTracker_RejectsDuplicateErrorStates_PerDeviceAndKind()
    {
        var tracker = new AlertStateTracker();
        int raised = 0;
        tracker.AlertRaised += _ => raised++;

        Assert.True(tracker.TryRaise(Alert(Valve, AlertKind.ThresholdBreach)));
        Assert.False(tracker.TryRaise(Alert(Valve.ToLowerInvariant(), AlertKind.ThresholdBreach)));
        Assert.True(tracker.TryRaise(Alert(Valve, AlertKind.Anomaly)));
        Assert.True(tracker.TryRaise(Alert(Pump, AlertKind.ThresholdBreach)));

        Assert.Equal(3, raised);
        Assert.Equal(1, tracker.GetStats().DuplicateAlertsSuppressed);
        Assert.Equal(new[] { Pump, Valve }.ToHashSet(), tracker.GetAlertingDevices().ToHashSet());
    }

    [Fact]
    public void AlertTracker_AcknowledgeClearsState_SoTheConditionCanAlertAgain()
    {
        var tracker = new AlertStateTracker();
        tracker.TryRaise(Alert(Valve, AlertKind.ThresholdBreach));
        Assert.Single(tracker.Acknowledge(Valve, "op", "checked"));

        Assert.True(tracker.TryRaise(Alert(Valve, AlertKind.ThresholdBreach)));
        Assert.Equal(2, tracker.CountRaisedSince(Valve, AlertKind.ThresholdBreach, DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void AlertTracker_DisconnectedSet_StoresEachNodeOnce()
    {
        var tracker = new AlertStateTracker();
        Assert.True(tracker.MarkDisconnected(Valve));
        Assert.False(tracker.MarkDisconnected(Valve));
        Assert.False(tracker.MarkDisconnected(Valve.ToLowerInvariant()));

        Assert.Single(tracker.GetDisconnected());
        Assert.Equal(2, tracker.GetStats().DuplicateDisconnectsSuppressed);
        Assert.True(tracker.MarkReconnected(Valve));
        Assert.False(tracker.MarkReconnected(Valve));
    }
}
