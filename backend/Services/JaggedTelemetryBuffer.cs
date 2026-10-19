namespace SmartX.Api.Services;

using SmartX.Api.Models;

/// <summary>
/// Manages multi-channel telemetry batches using advanced jagged arrays (double[][])
/// before transitioning sequential historical batches into optimized collections.
/// Reference: Microsoft, 2026j. The array reference type - C# reference. [online] Available at: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/arrays> [Accessed 19 October 2026].
/// </summary>
public class JaggedTelemetryBuffer
{
    // 4 parallel ingestion channels with dynamic packet batch sizes
    private readonly double[]?[] _rawChannels = new double[4][];
    private readonly object _lock = new();

    // Custom data structure to hold flushed ingestion states
    private readonly BoundedTelemetryCollection<TelemetryPacket<double>> _flushedBuffer = new(capacity: 10000);

    public int ChannelCount => _rawChannels.Length;

    public void LoadChannelBatch(int channelIndex, double[] readings)
    {
        if (channelIndex < 0 || channelIndex >= _rawChannels.Length)
            throw new ArgumentOutOfRangeException(nameof(channelIndex), $"Channel index must be between 0 and {_rawChannels.Length - 1}.");
        ArgumentNullException.ThrowIfNull(readings);

        lock (_lock)
        {
            _rawChannels[channelIndex] = readings;
        }
    }

    /// <summary>
    /// Flushes raw jagged array channels into standard List<TelemetryPacket<double>>
    /// and stores into the custom BoundedTelemetryCollection. Flushed channels are drained (released),
    /// so the same readings are never ingested twice. When <paramref name="channelIndex"/> is null, all channels flush.
    /// </summary>
    public List<TelemetryPacket<double>> FlushToList(string macAddress, string unit, int? channelIndex = null)
    {
        lock (_lock)
        {
            var result = new List<TelemetryPacket<double>>();
            for (int i = 0; i < _rawChannels.Length; i++)
            {
                if (channelIndex.HasValue && channelIndex.Value != i) continue;
                var channel = _rawChannels[i];
                if (channel == null) continue;
                for (int j = 0; j < channel.Length; j++)
                {
                    var packet = new TelemetryPacket<double>
                    {
                        MacAddress = macAddress,
                        Value = channel[j],
                        MetricUnit = unit
                    };
                    result.Add(packet);
                }
                _rawChannels[i] = null; // drain the flushed channel
            }

            _flushedBuffer.AddRange(result);
            return result;
        }
    }

    /// <summary>
    /// Returns the custom collection metrics and ingestion state tracking.
    /// </summary>
    public IngestionStateMetrics GetBufferMetrics()
    {
        return _flushedBuffer.GetMetrics();
    }

    /// <summary>
    /// Provides live inspection data of the jagged arrays for diagnostic triage.
    /// </summary>
    public JaggedBufferStatusDto GetChannelStatus()
    {
        lock (_lock)
        {
            var channels = new List<ChannelStatusDto>();
            int totalReadings = 0;

            for (int i = 0; i < _rawChannels.Length; i++)
            {
                var arr = _rawChannels[i];
                int count = arr?.Length ?? 0;
                totalReadings += count;

                double[] preview = Array.Empty<double>();
                if (arr != null && arr.Length > 0)
                {
                    preview = arr.Take(6).ToArray();
                }

                channels.Add(new ChannelStatusDto(
                    ChannelIndex: i,
                    AllocatedLength: count,
                    EstimatedBytes: count * sizeof(double),
                    SampleValues: preview
                ));
            }

            var metrics = _flushedBuffer.GetMetrics();

            return new JaggedBufferStatusDto(
                TotalChannels: _rawChannels.Length,
                TotalBufferedReadings: totalReadings,
                TotalEstimatedBytes: channels.Sum(c => c.EstimatedBytes),
                Channels: channels,
                CustomCollectionMetrics: metrics
            );
        }
    }
}

public record ChannelStatusDto(int ChannelIndex, int AllocatedLength, int EstimatedBytes, double[] SampleValues);
public record JaggedBufferStatusDto(
    int TotalChannels,
    int TotalBufferedReadings,
    int TotalEstimatedBytes,
    List<ChannelStatusDto> Channels,
    IngestionStateMetrics CustomCollectionMetrics
);
