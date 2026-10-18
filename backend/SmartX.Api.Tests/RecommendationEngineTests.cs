using SmartX.Api.Models;
using SmartX.Api.Services;
using SmartX.Api.Services.Recommendations;
using Xunit;

namespace SmartX.Api.Tests;

public class RecommendationEngineTests
{
    private const string RackB = "24:6F:28:9A:12:02";
    private const string Solenoid = "24:6F:28:9A:12:05";

    private static OperatorEvent Event(DateTime at, string key) => new(at, OperatorEventKind.Command, key, key, null, false);

    // ---------- Pattern model ----------

    [Fact]
    public void PatternModel_LearnsConditionToAction_WithConfidenceFromEpisodes()
    {
        var model = new ActionPatternModel();
        var t = DateTime.UtcNow.AddHours(-1);

        for (int episode = 0; episode < 4; episode++)
        {
            var onset = t.AddMinutes(episode * 10);
            model.ObserveConditions(new[] { "LOW|B" }, onset);
            if (episode < 3) model.Record(Event(onset.AddSeconds(30), "CMD|pump|ON"));
            model.ObserveConditions(Array.Empty<string>(), onset.AddMinutes(5));
        }

        var now = DateTime.UtcNow;
        model.ObserveConditions(new[] { "LOW|B" }, now); // 5th, live episode
        var candidate = Assert.Single(model.GetCandidates(now));
        Assert.Equal("CMD|pump|ON", candidate.ActionKey);
        Assert.Equal(3, candidate.Support);
        Assert.Equal(4, candidate.Episodes);
        Assert.Equal(0.75, candidate.Confidence);
    }

    [Fact]
    public void PatternModel_ActionInTheLiveEpisode_CountsTowardsTheDenominator()
    {
        var model = new ActionPatternModel();
        var t = DateTime.UtcNow.AddHours(-1);
        for (int episode = 0; episode < 2; episode++)
        {
            var onset = t.AddMinutes(episode * 10);
            model.ObserveConditions(new[] { "LOW|B" }, onset);
            model.Record(Event(onset.AddSeconds(30), "CMD|pump|ON"));
            model.ObserveConditions(Array.Empty<string>(), onset.AddMinutes(5));
        }

        var now = DateTime.UtcNow;
        model.ObserveConditions(new[] { "LOW|B" }, now);
        model.Record(Event(now.AddSeconds(5), "CMD|pump|ON")); // taken again during the live episode

        var candidate = Assert.Single(model.GetCandidates(now.AddSeconds(6)), c => c.FromCondition);
        Assert.Equal(3, candidate.Support);
        Assert.Equal(3, candidate.Episodes);
        Assert.Equal(1.0, candidate.Confidence);
    }

    [Fact]
    public void PatternModel_CountsAnActionOncePerEpisode()
    {
        var model = new ActionPatternModel();
        var now = DateTime.UtcNow;
        model.ObserveConditions(new[] { "LOW|B" }, now);
        for (int i = 0; i < 5; i++) model.Record(Event(now.AddSeconds(i), "CMD|pump|ON"));

        // One episode, one counted action: below the minimum support of 2
        model.ObserveConditions(Array.Empty<string>(), now.AddMinutes(10));
        model.ObserveConditions(new[] { "LOW|B" }, now.AddMinutes(11));
        Assert.Empty(model.GetCandidates(now.AddMinutes(11)));
    }

    [Fact]
    public void PatternModel_ActionRightAfterConditionEnds_StillCounts()
    {
        var model = new ActionPatternModel();
        var t = DateTime.UtcNow.AddMinutes(-30);
        for (int i = 0; i < 2; i++)
        {
            var onset = t.AddMinutes(i * 10);
            model.ObserveConditions(new[] { "LOW|B" }, onset);
            model.ObserveConditions(Array.Empty<string>(), onset.AddMinutes(1));
            model.Record(Event(onset.AddMinutes(2), "SEARCH|pump")); // 1 minute after it cleared
        }

        model.ObserveConditions(new[] { "LOW|B" }, DateTime.UtcNow);
        Assert.Contains(model.GetCandidates(DateTime.UtcNow), c => c.ActionKey == "SEARCH|pump");
    }

    [Fact]
    public void PatternModel_LearnsActionSequences()
    {
        var model = new ActionPatternModel();
        var t = DateTime.UtcNow.AddMinutes(-20);
        for (int i = 0; i < 3; i++)
        {
            model.Record(Event(t.AddMinutes(i * 5), "SEARCH|pump"));
            model.Record(Event(t.AddMinutes(i * 5).AddSeconds(20), "CMD|pump|ON"));
        }

        var now = DateTime.UtcNow;
        model.Record(Event(now, "SEARCH|pump"));
        var next = Assert.Single(model.GetCandidates(now));
        Assert.False(next.FromCondition);
        Assert.Equal("CMD|pump|ON", next.ActionKey);
        Assert.Equal(3, next.Support);
    }

    // ---------- Recommendation service ----------

    private sealed class Engine
    {
        public DeviceRegistryService Registry { get; } = new();
        public AlertStateTracker Alerts { get; } = new();
        public CommandDispatcher Commands { get; } = new();
        public RecommendationService Service { get; }

        public Engine()
        {
            Registry.RegisterDevice(new SensorDevice { MacAddress = RackB, Category = SensorCategory.Environmental, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Hydroponic Rack B" });
            Registry.RegisterDevice(new SensorDevice { MacAddress = Solenoid, Category = SensorCategory.Actuator, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Water Solenoid 1" });
            Service = new RecommendationService(Registry, Alerts, Commands, new ActionPatternModel());
            Service.SeedTrainingHistory();
        }

        public void Moisture(double value, DateTime? at = null)
        {
            Registry.RecordHeartbeat(RackB);
            Registry.RecordTelemetry(new TelemetryPacket<float> { MacAddress = RackB, Value = (float)value, MetricUnit = "%", Timestamp = at ?? DateTime.UtcNow });
        }

        public void SolenoidState(bool on)
        {
            Registry.RecordHeartbeat(Solenoid);
            Registry.RecordTelemetry(new TelemetryPacket<bool> { MacAddress = Solenoid, Value = on, MetricUnit = "State" });
        }
    }

    [Fact]
    public void Recommendations_SuggestOpeningTheSolenoid_WhenRackBDriesOut()
    {
        var engine = new Engine();
        engine.SolenoidState(false);
        engine.Moisture(14);

        var snapshot = engine.Service.Evaluate();

        Assert.Contains(snapshot.Conditions, c => c.Key == $"LOW_MOISTURE|{RackB}" && c.Severity == AlertSeverity.Critical);
        var top = snapshot.Suggestions[0];
        Assert.Equal("Command", top.Kind);
        Assert.Equal(Solenoid, top.MacAddress);
        Assert.True(top.TargetState);
        Assert.Contains("5 of 6", top.Reason);
        Assert.DoesNotContain(snapshot.Suggestions, s => s.Kind == "Inspect" && s.MacAddress == Solenoid); // command supersedes inspect
        Assert.Contains(snapshot.Insights, i => i.Id == $"LOW_MOISTURE|{RackB}");
    }

    [Fact]
    public void Recommendations_SkipCommandsThatAreAlreadySatisfied()
    {
        var engine = new Engine();
        engine.SolenoidState(true);
        engine.Moisture(14);

        Assert.DoesNotContain(engine.Service.Evaluate().Suggestions, s => s.Kind == "Command" && s.MacAddress == Solenoid);
    }

    [Fact]
    public void Recommendations_Accept_IssuesTheCommand_AndDismissSuppressesIt()
    {
        var engine = new Engine();
        engine.SolenoidState(false);
        engine.Moisture(14);

        var suggestion = engine.Service.Evaluate().Suggestions[0];
        var accepted = engine.Service.Accept(suggestion.Id)!;
        Assert.NotNull(accepted.Batch);
        Assert.Equal(CommandSource.Suggestion, accepted.Batch!.Source);
        Assert.Equal(1, engine.Commands.PendingCount(Solenoid));

        // Pending command -> no duplicate suggestion
        Assert.DoesNotContain(engine.Service.Evaluate().Suggestions, s => s.Id == suggestion.Id);

        var other = new Engine();
        other.SolenoidState(false);
        other.Moisture(14);
        var id = other.Service.Evaluate().Suggestions[0].Id;
        Assert.True(other.Service.Dismiss(id));
        Assert.DoesNotContain(other.Service.Evaluate().Suggestions, s => s.Id == id);
    }

    [Fact]
    public void Recommendations_LearnFromLiveSearches()
    {
        var engine = new Engine();
        Assert.Equal(1, engine.Service.RecordSearch("solenoid", new[] { Solenoid }));
        Assert.Equal(0, engine.Service.RecordSearch("   ", new[] { Solenoid }));
        Assert.Equal("SEARCH|" + Solenoid, engine.Service.Model.LastEvent!.ActionKey);
    }

    [Fact]
    public void Insights_ProjectWhenMoistureWillCrossTheThreshold()
    {
        var engine = new Engine();
        var start = DateTime.UtcNow.AddSeconds(-60);
        for (int i = 0; i < 40; i++) engine.Moisture(35 - i * 0.25, start.AddSeconds(i * 1.5)); // falling ~10%/min

        var projection = Assert.Single(engine.Service.Evaluate().Insights, i => i.Category == "Prediction");
        Assert.Contains("projected below 20%", projection.Title);
    }

    [Fact]
    public void Insights_AreOrderedBySeverity()
    {
        var engine = new Engine();
        engine.Moisture(9);
        engine.Alerts.MarkDisconnected(Solenoid);

        var insights = engine.Service.Evaluate().Insights;
        Assert.Equal(insights.OrderBy(i => (int)i.Severity).Select(i => i.Id), insights.Select(i => i.Id));
        Assert.Equal(AlertSeverity.Critical, insights[0].Severity);
    }
}
