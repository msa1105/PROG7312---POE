namespace SmartX.Api.Services;

using SmartX.Api.Models;
using System.Collections.Concurrent;

public class AnomalyDetectionService
{
    // MacAddress -> Queue of recent floats (max size e.g. 50)
    private readonly ConcurrentDictionary<string, Queue<double>> _rollingWindows = new();
    private readonly int _windowSize = 50;

    public bool IsAnomaly(string macAddress, double newValue)
    {
        var window = _rollingWindows.GetOrAdd(macAddress, _ => new Queue<double>());
        
        lock (window)
        {
            if (window.Count < 5) // Need a minimum sample size to calculate meaningful Z-score
            {
                window.Enqueue(newValue);
                return false;
            }

            double mean = window.Average();
            double sumOfSquares = window.Sum(x => Math.Pow(x - mean, 2));
            double stdDev = Math.Sqrt(sumOfSquares / window.Count);

            bool isOutlier = false;
            if (stdDev > 0)
            {
                double zScore = Math.Abs((newValue - mean) / stdDev);
                if (zScore > 3.0)
                {
                    isOutlier = true;
                }
            }

            window.Enqueue(newValue);
            if (window.Count > _windowSize)
            {
                window.Dequeue();
            }

            return isOutlier;
        }
    }
}
