using System.Text.Json.Serialization;

namespace SmartX.Api.Services.Ingestion;

[JsonConverter(typeof(JsonStringEnumConverter<IngestionLane>))]
public enum IngestionLane
{
    Standard,
    Priority
}

public sealed class IngestionEnvelope
{
    public required long Sequence { get; init; }
    public required TelemetryWorkItem Item { get; init; }
    public required IngestionLane Lane { get; init; }
    public CriticalClassification? Classification { get; init; }
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}

public record EnqueueResult(bool Accepted, long Sequence, IngestionLane Lane, int StandardDepth, int PriorityDepth);

/// <summary>A critical packet that was processed ahead of standard packets that arrived before it.</summary>
public record PriorityBypass(long Sequence, string MacAddress, string Reason, int OvertookStandardPackets, double WaitMs, DateTime ProcessedAt);

public record LaneStats(int Depth, int PeakDepth, long Enqueued, long Processed, double AverageWaitMs, double LastWaitMs);

public record IngestionQueueStats(
    LaneStats Standard,
    LaneStats Priority,
    long Rejected,
    int StandardCapacity,
    int SimulatedProcessingCostMs,
    PriorityBypass? LastBypass,
    PriorityBypass? LargestBypass,
    List<PriorityBypass> RecentBypasses);

/// <summary>
/// Two-lane ingestion buffer between the HTTP endpoints and the processing worker.
/// - Queue&lt;T&gt;: standard First-In-First-Out lane for routine telemetry.
/// - PriorityQueue&lt;T, (severity, sequence)&gt;: min-heap lane for critical readings; always drained first,
///   ordered by severity, then by arrival sequence (FIFO among equally severe alerts).
/// Reference: Queue<T> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.queue-1
/// Reference: PriorityQueue<TElement,TPriority> Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2
/// Reference: SemaphoreSlim Class (Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim
/// </summary>
public class TelemetryIngestionQueue
{
    public const int DefaultStandardCapacity = 50_000;
    private const int MaxRecentBypasses = 10;

    private readonly Queue<IngestionEnvelope> _standard = new();
    private readonly PriorityQueue<IngestionEnvelope, (int Severity, long Sequence)> _priority = new();
    private readonly LinkedList<PriorityBypass> _recentBypasses = new();
    private PriorityBypass? _largestBypass;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _lock = new();

    private readonly LaneCounters _standardCounters = new();
    private readonly LaneCounters _priorityCounters = new();
    private long _sequence;
    private long _rejected;

    public int StandardCapacity { get; }

    /// <summary>Demo knob: artificial per-packet cost for standard packets so a backlog becomes visible.</summary>
    public int SimulatedProcessingCostMs { get; set; }

    public TelemetryIngestionQueue(int standardCapacity = DefaultStandardCapacity)
    {
        StandardCapacity = standardCapacity;
    }

    public EnqueueResult Enqueue(TelemetryWorkItem item, CriticalClassification? classification)
    {
        IngestionEnvelope envelope;
        lock (_lock)
        {
            var sequence = ++_sequence;
            var lane = classification != null ? IngestionLane.Priority : IngestionLane.Standard;

            if (lane == IngestionLane.Standard && _standard.Count >= StandardCapacity)
            {
                _rejected++;
                return new EnqueueResult(false, sequence, lane, _standard.Count, _priority.Count);
            }

            envelope = new IngestionEnvelope { Sequence = sequence, Item = item, Lane = lane, Classification = classification };
            if (lane == IngestionLane.Priority)
            {
                _priority.Enqueue(envelope, ((int)classification!.Severity, sequence));
                _priorityCounters.OnEnqueue(_priority.Count);
            }
            else
            {
                _standard.Enqueue(envelope);
                _standardCounters.OnEnqueue(_standard.Count);
            }

            var result = new EnqueueResult(true, sequence, lane, _standard.Count, _priority.Count);
            _signal.Release();
            return result;
        }
    }

    /// <summary>
    /// Dequeues the next envelope: the priority lane always wins over the standard lane.
    /// Records a bypass whenever a critical packet overtakes older standard packets.
    /// </summary>
    public bool TryDequeue(out IngestionEnvelope envelope)
    {
        lock (_lock)
        {
            if (_priority.TryDequeue(out envelope!, out _))
            {
                var waitMs = (DateTime.UtcNow - envelope.EnqueuedAt).TotalMilliseconds;
                _priorityCounters.OnProcessed(waitMs);

                long sequence = envelope.Sequence;
                int overtook = _standard.Count(e => e.Sequence < sequence);
                if (overtook > 0)
                {
                    var bypass = new PriorityBypass(sequence, envelope.Item.MacAddress, envelope.Classification?.Reason ?? "Critical reading",
                        overtook, Math.Round(waitMs, 2), DateTime.UtcNow);
                    _recentBypasses.AddFirst(bypass);
                    if (_largestBypass == null || bypass.OvertookStandardPackets >= _largestBypass.OvertookStandardPackets) _largestBypass = bypass;
                    if (_recentBypasses.Count > MaxRecentBypasses) _recentBypasses.RemoveLast();
                }
                return true;
            }

            if (_standard.TryDequeue(out envelope!))
            {
                _standardCounters.OnProcessed((DateTime.UtcNow - envelope.EnqueuedAt).TotalMilliseconds);
                return true;
            }

            return false;
        }
    }

    /// <summary>Waits until at least one envelope is available (or cancellation).</summary>
    public Task WaitForWorkAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);

    public IngestionQueueStats GetStats()
    {
        lock (_lock)
        {
            return new IngestionQueueStats(
                _standardCounters.Snapshot(_standard.Count),
                _priorityCounters.Snapshot(_priority.Count),
                _rejected,
                StandardCapacity,
                SimulatedProcessingCostMs,
                _recentBypasses.First?.Value,
                _largestBypass,
                _recentBypasses.ToList());
        }
    }

    public void ResetStatistics()
    {
        lock (_lock)
        {
            _standardCounters.Reset();
            _priorityCounters.Reset();
            _recentBypasses.Clear();
            _largestBypass = null;
            _rejected = 0;
        }
    }

    private sealed class LaneCounters
    {
        private long _enqueued;
        private long _processed;
        private int _peakDepth;
        private double _averageWaitMs;
        private double _lastWaitMs;

        public void OnEnqueue(int depth)
        {
            _enqueued++;
            if (depth > _peakDepth) _peakDepth = depth;
        }

        public void OnProcessed(double waitMs)
        {
            _processed++;
            _lastWaitMs = waitMs;
            // Exponential moving average keeps the latency figure responsive to the current load
            _averageWaitMs = _processed == 1 ? waitMs : _averageWaitMs * 0.9 + waitMs * 0.1;
        }

        public LaneStats Snapshot(int depth) =>
            new(depth, _peakDepth, _enqueued, _processed, Math.Round(_averageWaitMs, 2), Math.Round(_lastWaitMs, 2));

        public void Reset()
        {
            _enqueued = _processed = 0;
            _peakDepth = 0;
            _averageWaitMs = _lastWaitMs = 0;
        }
    }
}
