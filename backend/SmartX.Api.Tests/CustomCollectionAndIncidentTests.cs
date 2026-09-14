using SmartX.Api.Models;
using SmartX.Api.Services;
using System.Text;
using Xunit;

namespace SmartX.Api.Tests;

public class CustomCollectionAndIncidentTests
{
    [Fact]
    public void BoundedTelemetryCollection_RespectsCapacity_AndTracksDrops()
    {
        var collection = new BoundedTelemetryCollection<int>(capacity: 5);

        for (int i = 1; i <= 8; i++)
        {
            collection.Add(i);
        }

        Assert.Equal(5, collection.Count);
        var metrics = collection.GetMetrics();
        Assert.Equal(8, metrics.TotalIngested);
        Assert.Equal(3, metrics.DroppedPackets);

        // Should retain the latest 5 items: 4, 5, 6, 7, 8
        var items = collection.ToList();
        Assert.Equal(new[] { 4, 5, 6, 7, 8 }, items);
    }

    [Fact]
    public void PowerMetric_EqualityOperators_ReturnExpectedResults()
    {
        var meterA = new PowerMetric(350.50);
        var meterB = new PowerMetric(350.50);
        var meterC = new PowerMetric(350.75);

        Assert.True(meterA == meterB);
        Assert.False(meterA != meterB);
        Assert.False(meterA == meterC);
        Assert.True(meterA != meterC);
    }

    [Fact]
    public void IncidentTriageService_MaintainsList_AndCalculatesOperatorXp()
    {
        var service = new IncidentTriageService();
        var initialCount = service.GetAllIssues().Count;

        var newIssue = new IncidentIssue
        {
            MacAddress = "24:6F:28:9A:12:03",
            Title = "Manual Triage Trigger",
            Description = "Tested sensor spike resolution",
            Severity = IncidentSeverity.High
        };

        var created = service.ReportIssue(newIssue);
        Assert.NotNull(created.Id);
        Assert.Equal(initialCount + 1, service.GetAllIssues().Count);
        Assert.Equal(IncidentStatus.Reported, created.Status);

        // Resolve the issue
        var resolved = service.UpdateStatus(created.Id, IncidentStatus.Resolved, "Re-calibrated telemetry gain.");
        Assert.NotNull(resolved);
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);

        var stats = service.GetEngagementStats();
        Assert.True(stats.OperatorXp > 0);
        Assert.True(stats.TotalResolved >= 1);
        Assert.Contains("First Responder", stats.Badges);
    }

    [Fact]
    public async Task FileEncryptionService_EncryptsAndDecryptsAccurately()
    {
        var encService = new FileEncryptionService();
        var tempFile = Path.GetTempFileName();
        var testContent = "Test payload for AES-256 sensor log encryption at rest.";

        try
        {
            using (var src = new MemoryStream(Encoding.UTF8.GetBytes(testContent)))
            {
                await encService.EncryptStreamAsync(src, tempFile);
            }

            // Encrypted file on disk should NOT match plaintext
            var rawBytes = await File.ReadAllBytesAsync(tempFile);
            var rawString = Encoding.UTF8.GetString(rawBytes);
            Assert.DoesNotContain("Test payload for AES-256", rawString);

            // Decrypt stream should match original
            using (var decryptedStream = await encService.DecryptToStreamAsync(tempFile))
            using (var reader = new StreamReader(decryptedStream))
            {
                var decryptedText = await reader.ReadToEndAsync();
                Assert.Equal(testContent, decryptedText);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
