namespace SmartX.Api.Models;

public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum IncidentStatus
{
    Reported,
    InTriage,
    Resolved
}

public class IncidentIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MacAddress { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;
    public IncidentStatus Status { get; set; } = IncidentStatus.Reported;
    public string ReportedBy { get; set; } = "Operator-1";
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public int XpAwarded { get; set; } = 50;
}

public record OperatorEngagementStats(
    int TotalReported,
    int TotalInTriage,
    int TotalResolved,
    int OperatorXp,
    int OperatorLevel,
    string OperatorRank,
    double ResolutionRatePercent,
    List<string> Badges
);
