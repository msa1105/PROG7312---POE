using System.Collections;

namespace SmartX.Api.Models;

/// <summary>
/// A custom, high-performance bounded generic collection optimized for gateway telemetry pipelines.
/// Fulfills the requirement for a custom collection data structure that tracks ingestion states.
/// Reference: Microsoft, 2025e. IReadOnlyCollection<T> interface (System.Collections.Generic). [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.ireadonlycollection-1> [Accessed 19 October 2026].
/// </summary>
/// <typeparam name="T">The telemetry packet or reading type.</typeparam>
public class BoundedTelemetryCollection<T> : IReadOnlyCollection<T>
{
    private readonly T[] _buffer;
    private readonly object _lock = new();
    private int _head = 0;
    private int _count = 0;

    // Ingestion state telemetry
    public int Capacity { get; }
    public long TotalIngestedCount { get; private set; }
    public long DroppedPacketsCount { get; private set; }
    public DateTime? LastIngestedAtUtc { get; private set; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public BoundedTelemetryCollection(int capacity = 1000)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");

        Capacity = capacity;
        _buffer = new T[capacity];
    }

    /// <summary>
    /// Adds a single telemetry item into the bounded ring buffer.
    /// Overwrites the oldest item when capacity is reached and increments the dropped count.
    /// </summary>
    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % Capacity;

            if (_count < Capacity)
            {
                _count++;
            }
            else
            {
                DroppedPacketsCount++;
            }

            TotalIngestedCount++;
            LastIngestedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Efficiently adds a batch of items into the collection.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        lock (_lock)
        {
            foreach (var item in items)
            {
                _buffer[_head] = item;
                _head = (_head + 1) % Capacity;

                if (_count < Capacity)
                {
                    _count++;
                }
                else
                {
                    DroppedPacketsCount++;
                }

                TotalIngestedCount++;
            }
            LastIngestedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns snapshot copy of the items in chronological order.
    /// </summary>
    public List<T> ToList()
    {
        lock (_lock)
        {
            var list = new List<T>(_count);
            int start = (_head - _count + Capacity) % Capacity;
            for (int i = 0; i < _count; i++)
            {
                list.Add(_buffer[(start + i) % Capacity]);
            }
            return list;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Snapshot summary of collection ingestion metrics.
    /// </summary>
    public IngestionStateMetrics GetMetrics()
    {
        lock (_lock)
        {
            return new IngestionStateMetrics(
                CurrentCount: _count,
                Capacity: Capacity,
                TotalIngested: TotalIngestedCount,
                DroppedPackets: DroppedPacketsCount,
                LastIngestedAt: LastIngestedAtUtc
            );
        }
    }
}

public record IngestionStateMetrics(
    int CurrentCount,
    int Capacity,
    long TotalIngested,
    long DroppedPackets,
    DateTime? LastIngestedAt
);
