using SmartX.Api.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Service implementing the dynamic engagement strategy for user-reported issues.
/// Manages a sequential List<IncidentIssue> with dynamic operator progression,
/// real-time status updates, and interactive feedback loops.
/// </summary>
public class IncidentTriageService
{
    // The required List collection to store user-reported issues
    private readonly List<IncidentIssue> _issues = new();
    private readonly object _lock = new();

    public IncidentTriageService()
    {
        // Pre-seed some realistic mock issues for instant demonstration
        SeedInitialIssues();
    }

    private void SeedInitialIssues()
    {
        _issues.Add(new IncidentIssue
        {
            Id = "ISS-1001",
            MacAddress = "24:6F:28:9A:12:03",
            Title = "Substation Main Voltage Harmonic Fluctuation",
            Description = "Phase 1 telemetry indicated transient spikes during cooling cycle shift.",
            Severity = IncidentSeverity.High,
            Status = IncidentStatus.InTriage,
            ReportedBy = "Operator-Alpha",
            ReportedAt = DateTime.UtcNow.AddMinutes(-45),
            XpAwarded = 100
        });

        _issues.Add(new IncidentIssue
        {
            Id = "ISS-1002",
            MacAddress = "24:6F:28:9A:12:01",
            Title = "Hydroponic Rack A Moisture Calibration Drift",
            Description = "Rolling mean shifted +12% following nutrient flush cycle.",
            Severity = IncidentSeverity.Medium,
            Status = IncidentStatus.Reported,
            ReportedBy = "Operator-Beta",
            ReportedAt = DateTime.UtcNow.AddMinutes(-20),
            XpAwarded = 50
        });

        _issues.Add(new IncidentIssue
        {
            Id = "ISS-1003",
            MacAddress = "24:6F:28:9A:12:05",
            Title = "Greenhouse Solenoid Actuator Relay Latch Test",
            Description = "Scheduled actuation response latency verified within 15ms threshold.",
            Severity = IncidentSeverity.Low,
            Status = IncidentStatus.Resolved,
            ReportedBy = "Operator-Alpha",
            ReportedAt = DateTime.UtcNow.AddHours(-2),
            ResolvedAt = DateTime.UtcNow.AddMinutes(-30),
            ResolutionNotes = "Relay contacts inspected and verified nominal.",
            XpAwarded = 200
        });
    }

    public IncidentIssue ReportIssue(IncidentIssue issue)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(issue.Id))
            {
                issue.Id = $"ISS-{Random.Shared.Next(1004, 9999)}";
            }
            issue.ReportedAt = DateTime.UtcNow;
            issue.Status = IncidentStatus.Reported;
            issue.XpAwarded = issue.Severity switch
            {
                IncidentSeverity.Critical => 150,
                IncidentSeverity.High => 100,
                IncidentSeverity.Medium => 50,
                _ => 25
            };

            _issues.Insert(0, issue);
            return issue;
        }
    }

    public List<IncidentIssue> GetAllIssues(string? macAddress = null, IncidentStatus? status = null)
    {
        lock (_lock)
        {
            var query = _issues.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(macAddress))
            {
                query = query.Where(i => i.MacAddress.Equals(macAddress, StringComparison.OrdinalIgnoreCase));
            }

            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            return query.ToList();
        }
    }

    public IncidentIssue? GetIssueById(string id)
    {
        lock (_lock)
        {
            return _issues.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IncidentIssue? UpdateStatus(string id, IncidentStatus newStatus, string? notes = null)
    {
        lock (_lock)
        {
            var issue = _issues.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (issue == null) return null;

            issue.Status = newStatus;
            if (newStatus == IncidentStatus.Resolved)
            {
                issue.ResolvedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    issue.ResolutionNotes = notes;
                }
            }
            return issue;
        }
    }

    public OperatorEngagementStats GetEngagementStats()
    {
        lock (_lock)
        {
            int totalReported = _issues.Count;
            int totalInTriage = _issues.Count(i => i.Status == IncidentStatus.InTriage);
            int totalResolved = _issues.Count(i => i.Status == IncidentStatus.Resolved);

            // Compute dynamic XP and badges
            int totalXp = _issues.Sum(i => i.XpAwarded) + (totalResolved * 150);
            int operatorLevel = Math.Max(1, 1 + (totalXp / 250));

            string rank = operatorLevel switch
            {
                >= 5 => "Master Systems Sentinel",
                >= 4 => "Mesh Incident Vanguard",
                >= 3 => "Network Triage Specialist",
                >= 2 => "Telemetry Technician",
                _ => "Operations Apprentice"
            };

            var badges = new List<string> { "First Responder" };
            if (totalResolved >= 1) badges.Add("Bug Hunter");
            if (totalResolved >= 3) badges.Add("Zero Downtime Guardian");
            if (_issues.Any(i => i.Severity == IncidentSeverity.Critical && i.Status == IncidentStatus.Resolved))
                badges.Add("High-Voltage Hero");
            if (totalReported >= 5) badges.Add("Eagle Eye Monitor");

            double resolutionRate = totalReported > 0 ? ((double)totalResolved / totalReported) * 100.0 : 100.0;

            return new OperatorEngagementStats(
                TotalReported: totalReported,
                TotalInTriage: totalInTriage,
                TotalResolved: totalResolved,
                OperatorXp: totalXp,
                OperatorLevel: operatorLevel,
                OperatorRank: rank,
                ResolutionRatePercent: Math.Round(resolutionRate, 1),
                Badges: badges
            );
        }
    }
}
