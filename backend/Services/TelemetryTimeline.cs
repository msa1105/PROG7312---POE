using SmartX.Api.Models;

namespace SmartX.Api.Services;

public static class DateTimeUtcExtensions
{
    /// <summary>Treats unspecified-kind timestamps (no offset in the JSON) as UTC instead of local time.</summary>
    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };
}

/// <summary>Timeline ordering key: device timestamp first, then a gateway sequence number to break ties.</summary>
public readonly record struct TimelineKey(long TimestampTicks, long Sequence) : IComparable<TimelineKey>
{
    public int CompareTo(TimelineKey other)
    {
        int byTime = TimestampTicks.CompareTo(other.TimestampTicks);
        return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
    }
}

public record TimelinePoint(DateTime Timestamp, double? Value, double? Min, double? Max, int Samples, bool IsAnomaly);

/// <summary>
/// Per-device historical sensor log backed by SortedDictionary&lt;TimelineKey, TelemetrySnapshot&gt;.
/// Readings stay in timestamp order even when they are processed out of arrival order (critical packets
/// bypass the standard queue, and devices may upload delayed backlogs), so timeline graphs never need re-sorting.
/// Reference: Microsoft, 2025k. SortedDictionary<TKey,TValue> class (System.Collections.Generic). [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sorteddictionary-2> [Accessed 19 October 2026].
/// Reference: Microsoft, 2025c. IComparable<T> interface (System). [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.icomparable-1> [Accessed 19 October 2026].
/// </summary>
public class TelemetryTimeline
{
    private readonly SortedDictionary<TimelineKey, TelemetrySnapshot> _readings = new();
    private readonly int _capacity;
    private long _sequence;

    public TelemetryTimeline(int capacity = 900)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _readings.Count;

    public void Add(TelemetrySnapshot snapshot)
    {
        _readings.Add(new TimelineKey(snapshot.Timestamp.AsUtc().Ticks, ++_sequence), snapshot);

        // Evict the oldest readings: the smallest key is always the first element of the sorted dictionary
        while (_readings.Count > _capacity)
        {
            _readings.Remove(_readings.Keys.First());
        }
    }

    /// <summary>The chronologically newest reading (by device timestamp, not by processing order).</summary>
    public TelemetrySnapshot? Latest => _readings.Count == 0 ? null : _readings.Values.Last();

    /// <summary>The most recent <paramref name="count"/> readings in chronological order.</summary>
    public List<TelemetrySnapshot> TakeLast(int count) =>
        _readings.Values.Skip(Math.Max(0, _readings.Count - count)).ToList();

    /// <summary>
    /// Readings between <paramref name="fromUtc"/> and <paramref name="toUtc"/> in order. Enumeration stops as soon
    /// as the upper bound is passed, because the dictionary is already sorted.
    /// When more than <paramref name="maxPoints"/> readings match, they are averaged into equal time buckets
    /// so the dashboard graph stays light regardless of the window size.
    /// </summary>
    public List<TimelinePoint> Range(DateTime fromUtc, DateTime toUtc, int maxPoints = 120)
    {
        long fromTicks = fromUtc.AsUtc().Ticks;
        long toTicks = toUtc.AsUtc().Ticks;

        var window = new List<TelemetrySnapshot>();
        foreach (var (key, snapshot) in _readings)
        {
            if (key.TimestampTicks < fromTicks) continue;
            if (key.TimestampTicks > toTicks) break;
            window.Add(snapshot);
        }

        if (window.Count <= maxPoints || maxPoints <= 0)
        {
            return window.Select(s => new TimelinePoint(s.Timestamp, AsNumber(s.Value), null, null, 1, s.IsAnomaly)).ToList();
        }

        // Bucket by time so gaps (offline periods) remain visible as gaps
        long span = Math.Max(1, toTicks - fromTicks);
        var buckets = new SortedDictionary<int, List<TelemetrySnapshot>>();
        foreach (var snapshot in window)
        {
            int bucket = (int)Math.Min(maxPoints - 1, (snapshot.Timestamp.AsUtc().Ticks - fromTicks) * maxPoints / span);
            if (!buckets.TryGetValue(bucket, out var list)) buckets[bucket] = list = new List<TelemetrySnapshot>();
            list.Add(snapshot);
        }

        return buckets.Values.Select(bucket =>
        {
            var numbers = bucket.Select(s => AsNumber(s.Value)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            var midpoint = new DateTime(bucket[bucket.Count / 2].Timestamp.AsUtc().Ticks, DateTimeKind.Utc);
            return numbers.Count == 0
                ? new TimelinePoint(midpoint, null, null, null, bucket.Count, bucket.Any(s => s.IsAnomaly))
                : new TimelinePoint(midpoint, Math.Round(numbers.Average(), 2), numbers.Min(), numbers.Max(), bucket.Count, bucket.Any(s => s.IsAnomaly));
        }).ToList();
    }

    public void Clear() => _readings.Clear();

    private static double? AsNumber(object? value) => value switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        _ => null
    };
}
