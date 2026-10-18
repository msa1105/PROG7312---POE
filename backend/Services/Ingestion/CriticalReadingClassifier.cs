using SmartX.Api.Models;

namespace SmartX.Api.Services.Ingestion;

public record CriticalClassification(AlertSeverity Severity, string Reason);

/// <summary>
/// Gate-keeper rules evaluated at the API boundary, before queuing. Readings that breach a safety threshold
/// are routed to the priority lane so they bypass the standard FIFO backlog.
/// </summary>
public static class CriticalReadingClassifier
{
    public const double MoistureCriticalPercent = 15.0;
    public const double MoistureLowPercent = 20.0;
    public const double PowerSurgeWatts = 1000.0;
    public const double PowerCriticalWatts = 1500.0;

    public static CriticalClassification? Classify(SensorCategory category, TelemetryWorkItem item)
    {
        if (item.NumericValue is not double value) return null;

        return category switch
        {
            SensorCategory.Environmental when value < MoistureCriticalPercent
                => new(AlertSeverity.Critical, $"Critical moisture drop: {value:F1}% (< {MoistureCriticalPercent}%)"),
            SensorCategory.Environmental when value < MoistureLowPercent
                => new(AlertSeverity.High, $"Moisture below {MoistureLowPercent}%: {value:F1}%"),
            SensorCategory.PowerConsumption when value > PowerCriticalWatts
                => new(AlertSeverity.Critical, $"Severe power spike: {value:F0} W (> {PowerCriticalWatts} W)"),
            SensorCategory.PowerConsumption when value > PowerSurgeWatts
                => new(AlertSeverity.High, $"Power surge: {value:F0} W (> {PowerSurgeWatts} W)"),
            _ => null
        };
    }
}
