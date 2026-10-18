namespace SmartX.Api.Services;

using System.Collections.Concurrent;

public record AnomalyEvaluation(bool IsAnomaly, double? ZScore, double? BaselineMean, double? BaselineStdDev, int SampleCount);

public record RollingBaseline(double Mean, double StdDev, int SampleCount);

/// <summary>
/// Reference: Z-score outlier detection: Chandola, V., Banerjee, A. and Kumar, V. (2009) 'Anomaly detection: A survey', ACM Computing Surveys, 41(3), pp. 1-58.
/// </summary>
public class AnomalyDetectionService
{
    public const double ZScoreThreshold = 3.0;
    // Warm-up before scoring: small windows underestimate σ and would raise false alerts at start-up
    public const int MinimumSamples = 20;

    // MacAddress -> Queue of recent readings (sliding window)
    private readonly ConcurrentDictionary<string, Queue<double>> _rollingWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _windowSize = 50;

    // Timestamps of detected outliers, used for the fleet-wide anomaly rate
    private readonly ConcurrentQueue<DateTime> _anomalyLog = new();

    public bool IsAnomaly(string macAddress, double newValue) => Evaluate(macAddress, newValue).IsAnomaly;

    /// <summary>
    /// Scores a reading against the rolling baseline (μ, σ) of the preceding window, then adds it to the window.
    /// </summary>
    public AnomalyEvaluation Evaluate(string macAddress, double newValue)
    {
        var window = _rollingWindows.GetOrAdd(macAddress, _ => new Queue<double>());

        lock (window)
        {
            if (window.Count < MinimumSamples)
            {
                window.Enqueue(newValue);
                return new AnomalyEvaluation(false, null, null, null, window.Count);
            }

            var (mean, stdDev) = ComputeStats(window);

            double? zScore = null;
            bool isOutlier = false;
            if (stdDev > 0)
            {
                zScore = (newValue - mean) / stdDev;
                isOutlier = Math.Abs(zScore.Value) > ZScoreThreshold;
            }

            window.Enqueue(newValue);
            if (window.Count > _windowSize)
            {
                window.Dequeue();
            }

            if (isOutlier)
            {
                _anomalyLog.Enqueue(DateTime.UtcNow);
            }

            return new AnomalyEvaluation(isOutlier, zScore, mean, stdDev, window.Count);
        }
    }

    public RollingBaseline? GetBaseline(string macAddress)
    {
        if (!_rollingWindows.TryGetValue(macAddress, out var window)) return null;
        lock (window)
        {
            if (window.Count < MinimumSamples) return null;
            var (mean, stdDev) = ComputeStats(window);
            return new RollingBaseline(mean, stdDev, window.Count);
        }
    }

    public int CountAnomaliesSince(DateTime sinceUtc)
    {
        while (_anomalyLog.TryPeek(out var oldest) && oldest < DateTime.UtcNow.AddMinutes(-5))
        {
            _anomalyLog.TryDequeue(out _);
        }
        return _anomalyLog.Count(t => t >= sinceUtc);
    }

    public void ResetWindow(string macAddress) => _rollingWindows.TryRemove(macAddress, out _);

    public void ResetAll()
    {
        _rollingWindows.Clear();
        _anomalyLog.Clear();
    }

    private static (double Mean, double StdDev) ComputeStats(Queue<double> window)
    {
        double mean = window.Average();
        double sumOfSquares = window.Sum(x => Math.Pow(x - mean, 2));
        return (mean, Math.Sqrt(sumOfSquares / window.Count));
    }
}
