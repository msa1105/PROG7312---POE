using SmartX.Api.Models;
using SmartX.Api.Services.Ingestion;

namespace SmartX.Api.Services.Recommendations;

public record ActiveCondition(string Key, string Kind, string MacAddress, string DeviceName, string Label, AlertSeverity Severity);

public record ActionSuggestion(
    string Id,
    string Kind,
    string Title,
    string Reason,
    double Confidence,
    double Score,
    int Support,
    int Episodes,
    string MacAddress,
    string DeviceName,
    bool? TargetState,
    string TriggerLabel);

public record AutomatedInsight(
    string Id,
    AlertSeverity Severity,
    string Category,
    string MacAddress,
    string DeviceName,
    string Title,
    string Detail,
    string Recommendation);

public record RecommendationSnapshot(
    List<ActionSuggestion> Suggestions,
    List<AutomatedInsight> Insights,
    List<ActiveCondition> Conditions,
    PatternModelStats Model,
    DateTime GeneratedAt);

public record SuggestionAcceptance(ActionSuggestion Suggestion, CommandBatch? Batch);

/// <summary>
/// Predictive Action &amp; Recommendation Engine.
/// Detects live conditions from the registry and alert sets, feeds them to the <see cref="ActionPatternModel"/>,
/// and turns learned patterns into ranked "Suggested Actions" (a PriorityQueue ordered by score) alongside
/// rule- and trend-based "Automated Insights" about potentially problematic devices.
/// Reference: PriorityQueue<TElement,TPriority> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2
/// Reference: Least-squares trend: Montgomery, D.C., Peck, E.A. and Vining, G.G. (2012) Introduction to Linear Regression Analysis. 5th edn. Hoboken: Wiley.
/// </summary>
public class RecommendationService
{
    public const int MaxSuggestions = 5;
    public const int MaxInsights = 8;
    private static readonly TimeSpan DismissCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProjectionHorizon = TimeSpan.FromMinutes(10);

    private readonly DeviceRegistryService _registry;
    private readonly AlertStateTracker _alerts;
    private readonly CommandDispatcher _commands;
    private readonly ActionPatternModel _model;

    private readonly Dictionary<string, ActionSuggestion> _lastSuggestions = new();
    private readonly Dictionary<string, DateTime> _dismissedUntil = new();
    private readonly Dictionary<string, int> _dismissCounts = new();
    private readonly object _lock = new();

    public RecommendationService(DeviceRegistryService registry, AlertStateTracker alerts, CommandDispatcher commands, ActionPatternModel model)
    {
        _registry = registry;
        _alerts = alerts;
        _commands = commands;
        _model = model;
    }

    public ActionPatternModel Model => _model;

    public static string DeviceName(SensorDevice device)
    {
        var segments = device.DeploymentLocation.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 ? segments[^1] : device.MacAddress;
    }

    public static bool IsControllable(SensorDevice device) => device.Category is SensorCategory.Actuator or SensorCategory.PowerConsumption;

    public static string CommandKey(string mac, bool targetState) => $"CMD|{mac}|{(targetState ? "ON" : "OFF")}";
    public static string SearchKey(string mac) => $"SEARCH|{mac}";

    // ---------- Learning inputs ----------

    public void RecordCommandBatch(CommandBatch batch)
    {
        if (batch.Source == CommandSource.Undo) return; // corrections are not learned as intent

        foreach (var command in batch.Commands)
        {
            _model.Record(new OperatorEvent(command.IssuedAt, OperatorEventKind.Command, CommandKey(command.MacAddress, command.TargetState),
                $"{(command.TargetState ? "Turned ON" : "Turned OFF")} {command.DeviceName}", null, false));
        }
    }

    /// <summary>
    /// Records an operator search. Only specific searches (resolving to at most three devices) are learned,
    /// because a broad query says little about intent.
    /// </summary>
    public int RecordSearch(string query, IEnumerable<string> matchedMacs)
    {
        var matches = matchedMacs.Select(m => _registry.GetDevice(m)).Where(d => d != null).Cast<SensorDevice>().ToList();
        if (string.IsNullOrWhiteSpace(query) || matches.Count == 0 || matches.Count > 3) return 0;

        var now = DateTime.UtcNow;
        foreach (var device in matches)
        {
            _model.Record(new OperatorEvent(now, OperatorEventKind.Search, SearchKey(device.MacAddress), $"Searched for {DeviceName(device)}", query.Trim(), false));
        }
        return matches.Count;
    }

    // ---------- Evaluation ----------

    public RecommendationSnapshot Evaluate(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var devices = _registry.GetAllDevices().ToDictionary(d => d.MacAddress, StringComparer.OrdinalIgnoreCase);

        var conditions = DetectConditions(devices.Values, now);
        _model.ObserveConditions(conditions.Select(c => c.Key), now);

        var conditionLabels = conditions.ToDictionary(c => c.Key, c => c);
        var suggestions = BuildSuggestions(devices, conditionLabels, now);
        var insights = BuildInsights(devices.Values, conditions, now);

        lock (_lock)
        {
            _lastSuggestions.Clear();
            foreach (var suggestion in suggestions) _lastSuggestions[suggestion.Id] = suggestion;
        }

        return new RecommendationSnapshot(suggestions, insights, conditions, _model.GetStats(), now);
    }

    public List<ActiveCondition> DetectConditions(IEnumerable<SensorDevice> devices, DateTime now)
    {
        var conditions = new List<ActiveCondition>();
        var disconnected = _alerts.GetDisconnected().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            var mac = device.MacAddress;
            var name = DeviceName(device);
            var latest = _registry.GetLatestTelemetry(mac);

            if (disconnected.Contains(mac))
                conditions.Add(new($"OFFLINE|{mac}", "OFFLINE", mac, name, $"{name} offline", AlertSeverity.High));

            if (device.Category == SensorCategory.Environmental && latest?.Value is double moisture && moisture < CriticalReadingClassifier.MoistureLowPercent)
            {
                var severity = moisture < CriticalReadingClassifier.MoistureCriticalPercent ? AlertSeverity.Critical : AlertSeverity.High;
                conditions.Add(new($"LOW_MOISTURE|{mac}", "LOW_MOISTURE", mac, name, $"{name} moisture below {CriticalReadingClassifier.MoistureLowPercent}%", severity));
            }

            if (device.Category == SensorCategory.PowerConsumption && _alerts.IsActive(mac, AlertKind.ThresholdBreach))
                conditions.Add(new($"POWER_SURGE|{mac}", "POWER_SURGE", mac, name, $"Power surge on {name}", AlertSeverity.Critical));

            if (_alerts.IsActive(mac, AlertKind.Anomaly))
                conditions.Add(new($"ANOMALY|{mac}", "ANOMALY", mac, name, $"Outlier reading on {name}", AlertSeverity.Warning));

            if (latest?.BatteryPercent is double battery && battery < 20)
                conditions.Add(new($"LOW_BATTERY|{mac}", "LOW_BATTERY", mac, name, $"{name} battery at {battery:F0}%", AlertSeverity.Warning));
        }

        return conditions;
    }

    private List<ActionSuggestion> BuildSuggestions(Dictionary<string, SensorDevice> devices, Dictionary<string, ActiveCondition> conditions, DateTime now)
    {
        var candidates = _model.GetCandidates(now);
        var lastEvent = _model.LastEvent;

        // Merge candidates that recommend the same action (keep the strongest evidence)
        var best = new Dictionary<string, ActionSuggestion>();
        foreach (var candidate in candidates)
        {
            var suggestion = ToSuggestion(candidate, devices, conditions, lastEvent, now);
            if (suggestion == null) continue;

            if (!best.TryGetValue(suggestion.Id, out var existing) || suggestion.Score > existing.Score)
                best[suggestion.Id] = suggestion;
        }

        // An executable command beats "inspect" for the same device
        var commandMacs = best.Values.Where(s => s.Kind == "Command").Select(s => s.MacAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var inspect in best.Values.Where(s => s.Kind == "Inspect" && commandMacs.Contains(s.MacAddress)).ToList())
            best.Remove(inspect.Id);

        // Rank with a max-priority queue (PriorityQueue is a min-heap, so the score is negated) and take the top N
        var ranking = new PriorityQueue<ActionSuggestion, double>();
        foreach (var suggestion in best.Values) ranking.Enqueue(suggestion, -suggestion.Score);

        var top = new List<ActionSuggestion>();
        while (top.Count < MaxSuggestions && ranking.TryDequeue(out var next, out _)) top.Add(next);
        return top;
    }

    private ActionSuggestion? ToSuggestion(PatternCandidate candidate, Dictionary<string, SensorDevice> devices,
        Dictionary<string, ActiveCondition> conditions, OperatorEvent? lastEvent, DateTime now)
    {
        var parts = candidate.ActionKey.Split('|');
        if (parts.Length < 2 || !devices.TryGetValue(parts[1], out var device)) return null;

        var id = candidate.ActionKey;
        lock (_lock)
        {
            if (_dismissedUntil.TryGetValue(id, out var until) && until > now) return null;
        }

        var name = DeviceName(device);
        string kind, title;
        bool? targetState = null;
        double kindWeight = 1.0;

        if (parts[0] == "CMD" && parts.Length == 3)
        {
            targetState = parts[2] == "ON";
            var status = _registry.GetStatus(device.MacAddress, now);
            var reported = _registry.GetReportedState(device.MacAddress);

            // Skip suggestions that are already satisfied, impossible, or in flight
            if (!IsControllable(device) || !status.IsOnline || reported == targetState || _commands.PendingCount(device.MacAddress) > 0) return null;

            kind = "Command";
            title = $"Turn {(targetState.Value ? "ON" : "OFF")} {name}";
        }
        else if (parts[0] == "SEARCH")
        {
            kind = "Inspect";
            kindWeight = 0.6;
            title = $"Inspect {name}";
        }
        else
        {
            return null;
        }

        string triggerLabel;
        string reason;
        double weight;

        if (candidate.FromCondition)
        {
            var condition = conditions.GetValueOrDefault(candidate.TriggerKey);
            triggerLabel = condition?.Label ?? candidate.TriggerKey;
            weight = condition?.Severity switch { AlertSeverity.Critical => 1.5, AlertSeverity.High => 1.25, _ => 1.0 };
            reason = $"When \"{triggerLabel}\" happened before, operators chose this in {candidate.Support} of {candidate.Episodes} episode(s) ({candidate.Confidence:P0}).";
        }
        else
        {
            triggerLabel = lastEvent?.Label ?? candidate.TriggerKey;
            weight = 0.7;
            reason = $"After \"{triggerLabel}\" this usually follows ({candidate.Support} of {candidate.Episodes} times, {candidate.Confidence:P0}).";
        }

        int dismissals;
        lock (_lock) dismissals = _dismissCounts.GetValueOrDefault(id);

        double score = candidate.Confidence * (1 + Math.Log2(candidate.Support)) * weight * kindWeight * Math.Pow(0.5, dismissals);
        return new ActionSuggestion(id, kind, title, reason, Math.Round(candidate.Confidence, 2), Math.Round(score, 3),
            candidate.Support, candidate.Episodes, device.MacAddress, name, targetState, triggerLabel);
    }

    private List<AutomatedInsight> BuildInsights(IEnumerable<SensorDevice> devices, List<ActiveCondition> conditions, DateTime now)
    {
        // Ordered by severity first, then by a per-rule urgency score
        var queue = new PriorityQueue<AutomatedInsight, (int Severity, double Urgency)>();
        var recentCommands = _commands.GetHistory(30).SelectMany(b => b.Commands).Where(c => now - c.IssuedAt <= TimeSpan.FromMinutes(10)).ToList();

        foreach (var condition in conditions)
        {
            var insight = condition.Kind switch
            {
                "OFFLINE" => new AutomatedInsight($"OFFLINE|{condition.MacAddress}", AlertSeverity.High, "Connectivity", condition.MacAddress, condition.DeviceName,
                    $"{condition.DeviceName} has stopped reporting", "No telemetry within two transmission windows; downstream readings are stale.", "Ping the node and check its power supply."),
                "LOW_MOISTURE" => new AutomatedInsight($"LOW_MOISTURE|{condition.MacAddress}", condition.Severity, "Out of range", condition.MacAddress, condition.DeviceName,
                    $"{condition.DeviceName} moisture is too low", $"Latest reading is below {CriticalReadingClassifier.MoistureLowPercent}%.", "Open the zone irrigation valve."),
                "POWER_SURGE" => new AutomatedInsight($"POWER_SURGE|{condition.MacAddress}", AlertSeverity.Critical, "Out of range", condition.MacAddress, condition.DeviceName,
                    $"Power surge on {condition.DeviceName}", $"Reading exceeded {CriticalReadingClassifier.PowerSurgeWatts} W.", "Shed load on the affected circuit, then acknowledge the alert."),
                "LOW_BATTERY" => new AutomatedInsight($"LOW_BATTERY|{condition.MacAddress}", AlertSeverity.Warning, "Maintenance", condition.MacAddress, condition.DeviceName,
                    condition.Label, "Battery is below 20%.", "Schedule a battery swap."),
                _ => null
            };
            if (insight != null) queue.Enqueue(insight, ((int)insight.Severity, 0));
        }

        foreach (var device in devices)
        {
            var mac = device.MacAddress;
            var name = DeviceName(device);
            var status = _registry.GetStatus(mac, now);

            int anomalies = _alerts.CountRaisedSince(mac, AlertKind.Anomaly, now.AddMinutes(-10));
            if (anomalies >= 2)
            {
                queue.Enqueue(new AutomatedInsight($"ANOMALY_BURST|{mac}", AlertSeverity.High, "Fault pattern", mac, name,
                    $"Repeated outliers on {name}", $"{anomalies} anomaly alerts in the last 10 minutes.", "Possible calibration fault: restart the ingestion buffer or inspect the sensor."),
                    ((int)AlertSeverity.High, -anomalies));
            }

            if (status.IsOnline && status.PacketDeliveryPercent is double delivery && delivery < 90)
            {
                queue.Enqueue(new AutomatedInsight($"DELIVERY|{mac}", AlertSeverity.Warning, "Link quality", mac, name,
                    $"Packet loss on {name}", $"Delivery success is {delivery:F0}% over the last 60 s.", "Check signal strength and interference."),
                    ((int)AlertSeverity.Warning, delivery));
            }

            var failed = recentCommands.Count(c => c.MacAddress.Equals(mac, StringComparison.OrdinalIgnoreCase) && c.Status is CommandStatus.Failed or CommandStatus.Expired);
            if (failed > 0)
            {
                queue.Enqueue(new AutomatedInsight($"COMMANDS|{mac}", AlertSeverity.High, "Actuation", mac, name,
                    $"{name} is not acknowledging commands", $"{failed} command(s) failed or expired in the last 10 minutes.", "Verify the device is online before retrying."),
                    ((int)AlertSeverity.High, -failed));
            }

            var projection = ProjectMoistureCrossing(device, now);
            if (projection is TimeSpan eta)
            {
                queue.Enqueue(new AutomatedInsight($"PROJECTED|{mac}", AlertSeverity.Warning, "Prediction", mac, name,
                    $"{name} projected below {CriticalReadingClassifier.MoistureLowPercent}% in ~{Math.Max(1, Math.Round(eta.TotalMinutes))} min",
                    "Linear trend over the recent timeline is falling.", "Pre-emptively open the irrigation valve."),
                    ((int)AlertSeverity.Warning, eta.TotalSeconds));
            }
        }

        var insights = new List<AutomatedInsight>();
        while (insights.Count < MaxInsights && queue.TryDequeue(out var insight, out _)) insights.Add(insight);
        return insights;
    }

    /// <summary>
    /// Least-squares trend over the recent moisture timeline; returns the time until it crosses the low threshold
    /// when that is falling and within the projection horizon.
    /// </summary>
    private TimeSpan? ProjectMoistureCrossing(SensorDevice device, DateTime now)
    {
        if (device.Category != SensorCategory.Environmental) return null;
        if (_registry.GetLatestTelemetry(device.MacAddress)?.Value is not double latest || latest <= CriticalReadingClassifier.MoistureLowPercent) return null;

        var points = _registry.GetHistory(device.MacAddress, 40)
            .Where(s => s.Value is double)
            .Select(s => (X: (s.Timestamp.AsUtc() - now).TotalSeconds, Y: (double)s.Value!))
            .ToList();
        if (points.Count < 20) return null;

        double meanX = points.Average(p => p.X), meanY = points.Average(p => p.Y);
        double sxx = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        if (sxx <= 0) return null;
        double slope = points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / sxx; // % per second
        if (slope >= -0.01) return null;

        // Project from the fitted value at "now" (less noisy than the raw latest reading)
        double current = Math.Min(latest, meanY + slope * (0 - meanX));
        if (current <= CriticalReadingClassifier.MoistureLowPercent) return null;

        var eta = TimeSpan.FromSeconds((current - CriticalReadingClassifier.MoistureLowPercent) / -slope);
        return eta <= ProjectionHorizon ? eta : null;
    }

    // ---------- Feedback ----------

    public SuggestionAcceptance? Accept(string id, string issuedBy = "Operator-Gateway")
    {
        ActionSuggestion? suggestion;
        lock (_lock) _lastSuggestions.TryGetValue(id, out suggestion);
        if (suggestion == null) return null;

        if (suggestion.Kind == "Command" && suggestion.TargetState is bool target)
        {
            var batch = _commands.Issue(suggestion.Title, CommandSource.Suggestion,
                new[] { new CommandRequest(suggestion.MacAddress, suggestion.DeviceName, target, _registry.GetReportedState(suggestion.MacAddress)) }, issuedBy);
            RecordCommandBatch(batch);
            return new SuggestionAcceptance(suggestion, batch);
        }

        _model.Record(new OperatorEvent(DateTime.UtcNow, OperatorEventKind.Search, SearchKey(suggestion.MacAddress), $"Inspected {suggestion.DeviceName}", null, false));
        return new SuggestionAcceptance(suggestion, null);
    }

    public bool Dismiss(string id)
    {
        lock (_lock)
        {
            if (!_lastSuggestions.ContainsKey(id)) return false;
            _dismissedUntil[id] = DateTime.UtcNow + DismissCooldown;
            _dismissCounts[id] = _dismissCounts.GetValueOrDefault(id) + 1;
            _lastSuggestions.Remove(id);
            return true;
        }
    }

    // ---------- Training data ----------

    /// <summary>
    /// Seeds the model with prior operator sessions so suggestions are available on first launch.
    /// Seeded events are flagged and counted separately in the model statistics.
    /// </summary>
    public void SeedTrainingHistory(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        const string rackA = "24:6F:28:9A:12:01", rackB = "24:6F:28:9A:12:02", substationMain = "24:6F:28:9A:12:03",
            substationGrid2 = "24:6F:28:9A:12:04", solenoid = "24:6F:28:9A:12:05";

        string Name(string mac) => _registry.GetDevice(mac) is SensorDevice d ? DeviceName(d) : mac;
        var start = now.AddHours(-6);
        int episode = 0;

        void Episode(string conditionKey, params (TimeSpan Offset, OperatorEventKind Kind, string Key, string Label)[] actions)
        {
            var onset = start.AddMinutes(20 * episode++);
            _model.ObserveConditions(new[] { conditionKey }, onset);
            foreach (var (offset, kind, key, label) in actions)
                _model.Record(new OperatorEvent(onset + offset, kind, key, label, kind == OperatorEventKind.Search ? label : null, true));
            _model.ObserveConditions(Array.Empty<string>(), onset.AddMinutes(10));
        }

        var searchSolenoid = (TimeSpan.FromSeconds(40), OperatorEventKind.Search, SearchKey(solenoid), $"Searched for {Name(solenoid)}");
        var openSolenoid = (TimeSpan.FromSeconds(75), OperatorEventKind.Command, CommandKey(solenoid, true), $"Turned ON {Name(solenoid)}");

        // Rack B dries out -> operator looks up the solenoid and opens it (5 of 6 episodes)
        for (int i = 0; i < 5; i++) Episode($"LOW_MOISTURE|{rackB}", searchSolenoid, openSolenoid);
        Episode($"LOW_MOISTURE|{rackB}", searchSolenoid);

        // Rack A dries out -> solenoid opened directly (4 of 5)
        for (int i = 0; i < 4; i++) Episode($"LOW_MOISTURE|{rackA}", openSolenoid);
        Episode($"LOW_MOISTURE|{rackA}");

        // Surge on the main substation -> load shed on grid 2 (3 of 4)
        var shedGrid2 = (TimeSpan.FromSeconds(30), OperatorEventKind.Command, CommandKey(substationGrid2, false), $"Turned OFF {Name(substationGrid2)}");
        for (int i = 0; i < 3; i++) Episode($"POWER_SURGE|{substationMain}", shedGrid2);
        Episode($"POWER_SURGE|{substationMain}");

        // Rack A goes offline -> operator inspects it (3 of 3)
        for (int i = 0; i < 3; i++) Episode($"OFFLINE|{rackA}", (TimeSpan.FromSeconds(20), OperatorEventKind.Search, SearchKey(rackA), $"Searched for {Name(rackA)}"));
    }

    public void Reset(bool reseed = true)
    {
        _model.Reset();
        lock (_lock)
        {
            _lastSuggestions.Clear();
            _dismissedUntil.Clear();
            _dismissCounts.Clear();
        }
        if (reseed) SeedTrainingHistory();
    }
}
