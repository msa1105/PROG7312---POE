using System.Text.Json.Serialization;

namespace SmartX.Api.Services.Recommendations;

[JsonConverter(typeof(JsonStringEnumConverter<OperatorEventKind>))]
public enum OperatorEventKind
{
    Search,
    Command
}

/// <summary>
/// One operator interaction, normalised to an action key:
/// "CMD|{mac}|ON" / "CMD|{mac}|OFF" for override commands, "SEARCH|{mac}" for a search that resolved to a device.
/// </summary>
public record OperatorEvent(DateTime At, OperatorEventKind Kind, string ActionKey, string Label, string? Query, bool Seeded);

/// <summary>A learned association between an observed condition (or a previous action) and an action.</summary>
public record PatternCandidate(string ActionKey, string TriggerKey, bool FromCondition, int Support, int Episodes, double Confidence);

public record PatternModelStats(int EventsLearned, int SeededEvents, int ConditionPatterns, int SequencePatterns, int ActiveConditions);

/// <summary>
/// Learns which operator actions follow which sensor conditions and which actions follow each other.
///
/// Data structures:
/// - Dictionary&lt;condition, Dictionary&lt;action, count&gt;&gt;: condition → action co-occurrence (e.g. "Rack B moisture low" → "Water Solenoid 1 ON").
/// - Dictionary&lt;condition, episodes&gt;: how many separate times each condition has occurred (the confidence denominator).
/// - Dictionary&lt;action, Dictionary&lt;action, count&gt;&gt;: first-order Markov transitions between consecutive actions.
/// - HashSet&lt;string&gt; of (condition episode, action) pairs: an action is counted at most once per episode,
///   so repeatedly clicking a button cannot inflate a pattern.
/// - LinkedList&lt;OperatorEvent&gt;: bounded, newest-first operator history.
/// Reference: Agrawal, R. and Srikant, R., 1994. Fast algorithms for mining association rules. Proceedings of the 20th International Conference on Very Large Data Bases. Santiago, Chile, 12-15 September 1994. San Francisco: Morgan Kaufmann, pp.487-499.
/// Reference: Microsoft, 2025f. LinkedList<T> class (System.Collections.Generic). [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.linkedlist-1> [Accessed 19 October 2026].
/// </summary>
public class ActionPatternModel
{
    /// <summary>An action counts towards a condition that ended at most this long ago ("right after it dropped").</summary>
    public static readonly TimeSpan ConditionAftermath = TimeSpan.FromMinutes(2);

    /// <summary>Two actions this close together form a sequence transition.</summary>
    public static readonly TimeSpan SequenceWindow = TimeSpan.FromMinutes(3);

    private const int MaxHistory = 300;

    private readonly Dictionary<string, Dictionary<string, int>> _conditionActions = new();
    private readonly Dictionary<string, int> _conditionEpisodes = new();
    private readonly Dictionary<string, Dictionary<string, int>> _transitions = new();
    private readonly HashSet<string> _countedEpisodeActions = new();

    // Currently active conditions (key -> onset) and recently ended ones (key -> (onset, ended))
    private readonly Dictionary<string, DateTime> _activeConditions = new();
    private readonly Dictionary<string, (DateTime Onset, DateTime Ended)> _recentlyEnded = new();

    private readonly LinkedList<OperatorEvent> _history = new();
    private readonly object _lock = new();
    private int _eventsLearned;
    private int _seededEvents;

    /// <summary>
    /// Updates the set of live conditions. A condition that was not active before starts a new episode.
    /// </summary>
    public void ObserveConditions(IEnumerable<string> conditionKeys, DateTime nowUtc)
    {
        lock (_lock)
        {
            var current = conditionKeys.ToHashSet();

            foreach (var key in current)
            {
                if (_activeConditions.ContainsKey(key)) continue;

                _activeConditions[key] = nowUtc;
                _conditionEpisodes[key] = _conditionEpisodes.GetValueOrDefault(key) + 1;
                _recentlyEnded.Remove(key);
            }

            foreach (var key in _activeConditions.Keys.Where(k => !current.Contains(k)).ToList())
            {
                _recentlyEnded[key] = (_activeConditions[key], nowUtc);
                _activeConditions.Remove(key);
            }

            foreach (var key in _recentlyEnded.Where(e => nowUtc - e.Value.Ended > ConditionAftermath).Select(e => e.Key).ToList())
            {
                _recentlyEnded.Remove(key);
            }
        }
    }

    /// <summary>
    /// Learns from an operator action: links it to every live (or just-ended) condition and to the previous action.
    /// </summary>
    public void Record(OperatorEvent operatorEvent)
    {
        lock (_lock)
        {
            // 1. Condition -> action co-occurrence, once per condition episode
            var episodes = _activeConditions.Select(c => (Key: c.Key, Onset: c.Value))
                .Concat(_recentlyEnded.Select(c => (Key: c.Key, Onset: c.Value.Onset)));

            foreach (var (conditionKey, onset) in episodes)
            {
                if (!_countedEpisodeActions.Add($"{conditionKey}#{onset.Ticks}#{operatorEvent.ActionKey}")) continue;
                Increment(_conditionActions, conditionKey, operatorEvent.ActionKey);
            }

            // 2. Previous action -> this action (Markov transition)
            var previous = _history.First?.Value;
            if (previous != null && previous.ActionKey != operatorEvent.ActionKey && operatorEvent.At - previous.At <= SequenceWindow)
            {
                Increment(_transitions, previous.ActionKey, operatorEvent.ActionKey);
            }

            _history.AddFirst(operatorEvent);
            if (_history.Count > MaxHistory) _history.RemoveLast();

            _eventsLearned++;
            if (operatorEvent.Seeded) _seededEvents++;
        }
    }

    /// <summary>
    /// Candidate actions for the current situation: learned responses to every active condition, plus the usual
    /// next step after the operator's last action. Patterns below the support / confidence floor are ignored.
    /// </summary>
    public List<PatternCandidate> GetCandidates(DateTime nowUtc, int minimumSupport = 2, double minimumConfidence = 0.4)
    {
        var candidates = new List<PatternCandidate>();

        lock (_lock)
        {
            foreach (var (conditionKey, onset) in _activeConditions)
            {
                if (!_conditionActions.TryGetValue(conditionKey, out var actions)) continue;
                int totalEpisodes = _conditionEpisodes.GetValueOrDefault(conditionKey);

                foreach (var (actionKey, count) in actions)
                {
                    // The live episode only counts towards the denominator once the action has been taken in it
                    bool takenInLiveEpisode = _countedEpisodeActions.Contains($"{conditionKey}#{onset.Ticks}#{actionKey}");
                    int episodes = Math.Max(1, takenInLiveEpisode ? totalEpisodes : totalEpisodes - 1);
                    double confidence = Math.Min(1.0, (double)count / episodes);
                    if (count >= minimumSupport && confidence >= minimumConfidence)
                        candidates.Add(new PatternCandidate(actionKey, conditionKey, true, count, episodes, confidence));
                }
            }

            var last = _history.First?.Value;
            if (last != null && nowUtc - last.At <= SequenceWindow && _transitions.TryGetValue(last.ActionKey, out var next))
            {
                int total = next.Values.Sum();
                foreach (var (actionKey, count) in next)
                {
                    double confidence = (double)count / total;
                    if (count >= minimumSupport && confidence >= minimumConfidence)
                        candidates.Add(new PatternCandidate(actionKey, last.ActionKey, false, count, total, confidence));
                }
            }
        }

        return candidates;
    }

    public bool IsConditionActive(string conditionKey)
    {
        lock (_lock) return _activeConditions.ContainsKey(conditionKey);
    }

    public List<OperatorEvent> GetHistory(int limit = 50)
    {
        lock (_lock) return _history.Take(limit).ToList();
    }

    public OperatorEvent? LastEvent
    {
        get { lock (_lock) return _history.First?.Value; }
    }

    public PatternModelStats GetStats()
    {
        lock (_lock)
        {
            return new PatternModelStats(
                _eventsLearned,
                _seededEvents,
                _conditionActions.Values.Sum(a => a.Count),
                _transitions.Values.Sum(t => t.Count),
                _activeConditions.Count);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _conditionActions.Clear();
            _conditionEpisodes.Clear();
            _transitions.Clear();
            _countedEpisodeActions.Clear();
            _activeConditions.Clear();
            _recentlyEnded.Clear();
            _history.Clear();
            _eventsLearned = 0;
            _seededEvents = 0;
        }
    }

    private static void Increment(Dictionary<string, Dictionary<string, int>> table, string from, string to)
    {
        if (!table.TryGetValue(from, out var row))
        {
            row = new Dictionary<string, int>();
            table[from] = row;
        }
        row[to] = row.GetValueOrDefault(to) + 1;
    }
}
