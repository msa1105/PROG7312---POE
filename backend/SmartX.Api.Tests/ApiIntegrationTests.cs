using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SmartX.Api.Tests;

/// <summary>
/// Boots the real API (including the background telemetry worker) against isolated temp storage.
/// </summary>
public sealed class SmartXApiFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "smartx-api-" + Guid.NewGuid().ToString("N"));
    public string UploadsRoot => Path.Combine(StorageRoot, "Uploads");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:UploadsPath", UploadsRoot);
        builder.UseSetting("Storage:KeysPath", Path.Combine(StorageRoot, "keys"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true); } catch (IOException) { }
    }
}

internal static class ApiTestHelpers
{
    public const string PowerMac = "24:6F:28:9A:12:03";
    public const string EnvMac = "24:6F:28:9A:12:01";

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<JsonElement> WaitForAsync(Func<Task<JsonElement?>> probe, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result.HasValue) return result.Value;
            await Task.Delay(250);
        }
        throw new TimeoutException($"Timed out waiting for: {description}");
    }

    public static async Task<JsonElement> GetDeviceViewAsync(this HttpClient client, string mac)
    {
        var all = await client.GetJsonAsync("/api/sensors");
        return all.EnumerateArray().Single(d => d.GetProperty("device").GetProperty("macAddress").GetString() == mac);
    }

    public static string Escape(string mac) => Uri.EscapeDataString(mac);
}

public class ApiIntegrationTests : IClassFixture<SmartXApiFactory>
{
    private readonly SmartXApiFactory _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(SmartXApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReportsSeededFleet_AndReliabilityIndices()
    {
        await ApiTestHelpers.WaitForAsync(async () =>
        {
            var h = await _client.GetJsonAsync("/api/health");
            return h.GetProperty("activeDeviceCount").GetInt32() >= 6 ? h : null;
        }, TimeSpan.FromSeconds(8), "all seeded devices online");

        var health = await _client.GetJsonAsync("/api/health");
        Assert.Equal("ONLINE", health.GetProperty("status").GetString());
        Assert.True(health.GetProperty("registeredDeviceCount").GetInt32() >= 6);
        Assert.Equal(3.0, health.GetProperty("leaseSeconds").GetDouble());
        Assert.True(health.GetProperty("totalTelemetryPackets").GetInt64() > 0);
        Assert.True(health.GetProperty("uptimeSeconds").GetDouble() > 0);
        Assert.True(health.TryGetProperty("bufferMetrics", out _));
        Assert.True(health.GetProperty("operatorEngagement").TryGetProperty("meanTimeToTriageSeconds", out _));
    }

    [Fact]
    public async Task Sensors_ExposeStatusAndSnapshot_WithoutServerFilePaths()
    {
        var view = await ApiTestHelpers.WaitForAsync(async () =>
        {
            var v = await _client.GetDeviceViewAsync(ApiTestHelpers.PowerMac);
            return v.GetProperty("latestTelemetry").ValueKind == JsonValueKind.Object ? v : null;
        }, TimeSpan.FromSeconds(8), "first power packet");

        var latest = view.GetProperty("latestTelemetry");
        Assert.Equal("PowerMetric", latest.GetProperty("payloadType").GetString());
        Assert.Equal(JsonValueKind.Number, latest.GetProperty("value").ValueKind);
        Assert.True(view.GetProperty("status").GetProperty("isOnline").GetBoolean());

        var env = await _client.GetDeviceViewAsync(ApiTestHelpers.EnvMac);
        var attachment = env.GetProperty("device").GetProperty("attachments").EnumerateArray().First();
        Assert.False(attachment.TryGetProperty("filePath", out _));
    }

    [Fact]
    public async Task SpikeEndToEnd_FlagsOutlier_LatchesAlert_AndAcknowledgementRecordsMttd()
    {
        var mac = ApiTestHelpers.PowerMac;
        await ApiTestHelpers.WaitForAsync(async () =>
        {
            var history = await _client.GetJsonAsync($"/api/sensors/{ApiTestHelpers.Escape(mac)}/history");
            return history.GetArrayLength() >= 22 ? history : null;
        }, TimeSpan.FromSeconds(45), "rolling baseline warm-up");

        var spike = await _client.PostAsJsonAsync("/api/simulation/spike", new { macAddress = mac, multiplier = 4.5 });
        Assert.Equal(HttpStatusCode.OK, spike.StatusCode);

        var view = await ApiTestHelpers.WaitForAsync(async () =>
        {
            var v = await _client.GetDeviceViewAsync(mac);
            return v.GetProperty("activeAlerts").EnumerateArray().Any(a => a.GetProperty("kind").GetString() == "Anomaly") ? v : null;
        }, TimeSpan.FromSeconds(8), "latched anomaly alert");

        var alert = view.GetProperty("activeAlerts").EnumerateArray().First(a => a.GetProperty("kind").GetString() == "Anomaly");
        Assert.True(alert.GetProperty("zScore").GetDouble() > 3.0);
        Assert.True(alert.GetProperty("deviation").GetDouble() > 0);

        var health = await _client.GetJsonAsync("/api/health");
        Assert.True(health.GetProperty("anomaliesLastMinute").GetInt32() >= 1);

        var ack = await _client.PostAsJsonAsync($"/api/sensors/{ApiTestHelpers.Escape(mac)}/alerts/acknowledge", new { notes = "Verified surge on substation" });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        var ackBody = await ack.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ackBody.GetProperty("acknowledgedCount").GetInt32() >= 1);

        var after = await _client.GetDeviceViewAsync(mac);
        Assert.DoesNotContain(after.GetProperty("activeAlerts").EnumerateArray(), a => a.GetProperty("kind").GetString() == "Anomaly");

        var stats = await _client.GetJsonAsync("/api/issues/stats");
        Assert.Equal(JsonValueKind.Number, stats.GetProperty("meanTimeToDetectSeconds").ValueKind);
    }

    [Fact]
    public async Task DropHeartbeat_TakesNodeOffline_AndDegradesLiveTopology()
    {
        const string mac = "AA:BB:CC:00:00:01";
        var register = await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = mac, category = 0, deploymentLocation = "Facility Alpha > Test Zone > Probe 1" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        await ApiTestHelpers.WaitForAsync(async () =>
        {
            var v = await _client.GetDeviceViewAsync(mac);
            return v.GetProperty("status").GetProperty("isOnline").GetBoolean() ? v : null;
        }, TimeSpan.FromSeconds(8), "new device online");

        var drop = await _client.PostAsJsonAsync("/api/simulation/drop-heartbeat", new { macAddress = mac });
        Assert.Equal(HttpStatusCode.OK, drop.StatusCode);

        var offline = await ApiTestHelpers.WaitForAsync(async () =>
        {
            var v = await _client.GetDeviceViewAsync(mac);
            return !v.GetProperty("status").GetProperty("isOnline").GetBoolean()
                && v.GetProperty("activeAlerts").EnumerateArray().Any(a => a.GetProperty("kind").GetString() == "HeartbeatLost") ? v : null;
        }, TimeSpan.FromSeconds(8), "lease expiry and heartbeat alert");
        Assert.True(offline.GetProperty("status").GetProperty("secondsSinceLastPacket").GetDouble() > 3.0);

        var live = await _client.GetJsonAsync("/api/topology/live");
        var root = live.GetProperty("root");
        Assert.True(root.GetProperty("isDegraded").GetBoolean());
        var zone = root.GetProperty("subNodes").EnumerateArray().Single(n => n.GetProperty("name").GetString() == "Test Zone");
        Assert.True(zone.GetProperty("isDegraded").GetBoolean());
        Assert.Equal(0.0, zone.GetProperty("availabilityPercent").GetDouble());
        Assert.False(live.GetProperty("validation").GetProperty("isValid").GetBoolean());

        var ping = await _client.PostAsync($"/api/sensors/{ApiTestHelpers.Escape(mac)}/ping", null);
        var pingBody = await ping.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(pingBody.GetProperty("reachable").GetBoolean());
        Assert.Equal(JsonValueKind.Number, pingBody.GetProperty("lastSignalStrengthDbm").ValueKind);
    }

    [Fact]
    public async Task AttachmentUpload_IsEncryptedAtRest_SanitizesNames_AndDownloadVerifiesIntegrity()
    {
        const string mac = "AA:BB:CC:00:00:02";
        await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = mac, category = 1, deploymentLocation = "Facility Alpha > Lab > Meter" });
        var plaintext = "[INFO] Secret calibration constants 42.0";

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(plaintext));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html"); // client MIME must not be trusted
        form.Add(file, "file", "../../escape.log");

        var upload = await _client.PostAsync($"/api/sensors/{ApiTestHelpers.Escape(mac)}/attachments", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("escape.log", uploaded.GetProperty("fileName").GetString());
        Assert.Equal("text/plain", uploaded.GetProperty("mimeType").GetString());

        // Only server-named, encrypted blobs exist and none contain the plaintext
        var blobs = Directory.GetFiles(Path.Combine(_factory.UploadsRoot, "AABBCC000002"));
        var blob = Assert.Single(blobs);
        Assert.EndsWith(".enc", blob);
        Assert.DoesNotContain("Secret calibration", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(blob)));
        Assert.False(File.Exists(Path.Combine(_factory.StorageRoot, "escape.log")));

        var download = await _client.GetAsync($"/api/sensors/{ApiTestHelpers.Escape(mac)}/attachments/escape.log");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("text/plain", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(plaintext, await download.Content.ReadAsStringAsync());

        // Tamper with the blob: the download must be refused
        var bytes = await File.ReadAllBytesAsync(blob);
        bytes[24] ^= 0xFF;
        await File.WriteAllBytesAsync(blob, bytes);
        var tampered = await _client.GetAsync($"/api/sensors/{ApiTestHelpers.Escape(mac)}/attachments/escape.log");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tampered.StatusCode);
    }

    [Theory]
    [InlineData("AA:BB:CC:00:00:02", "payload.exe", HttpStatusCode.BadRequest)]
    [InlineData("00:00:00:00:00:99", "diag.log", HttpStatusCode.NotFound)]
    public async Task AttachmentUpload_RejectsBadInput(string mac, string fileName, HttpStatusCode expected)
    {
        await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = "AA:BB:CC:00:00:02", category = 1, deploymentLocation = "Facility Alpha > Lab > Meter" });
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", fileName);

        var response = await _client.PostAsync($"/api/sensors/{ApiTestHelpers.Escape(mac)}/attachments", form);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Register_ValidatesInput_AndIgnoresInjectedAttachments()
    {
        var bad = await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = "zz-not-mac", category = 0, deploymentLocation = "F > Z > N" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var badCategory = await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = "AA:BB:CC:00:00:03", category = 9, deploymentLocation = "F > Z > N" });
        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);

        var injected = await _client.PostAsJsonAsync("/api/sensors/register", new
        {
            macAddress = "aa:bb:cc:00:00:04",
            category = 2,
            deploymentLocation = "Facility Alpha > Lab > Valve",
            attachments = new[] { new { fileName = "win.log", filePath = @"C:\Windows\win.ini" } }
        });
        Assert.Equal(HttpStatusCode.OK, injected.StatusCode);
        var body = await injected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AA:BB:CC:00:00:04", body.GetProperty("macAddress").GetString());
        Assert.Equal(0, body.GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public async Task Incidents_FullTriageLifecycle_WithValidation()
    {
        var unknown = await _client.PostAsJsonAsync("/api/issues", new { macAddress = "00:00:00:00:00:99", title = "x", description = "y", severity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var created = await _client.PostAsJsonAsync("/api/issues", new { macAddress = ApiTestHelpers.PowerMac, title = "Harmonic drift", description = "Observed", severity = 0 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var issue = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = issue.GetProperty("id").GetString()!;
        Assert.StartsWith("ISS-", id);
        Assert.Equal(25, issue.GetProperty("xpAwarded").GetInt32());

        var invalid = await _client.PatchAsJsonAsync($"/api/issues/{id}/status", new { status = 7 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var triage = await _client.PatchAsJsonAsync($"/api/issues/{id}/status", new { status = 1 });
        Assert.NotEqual(JsonValueKind.Null, (await triage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("triagedAt").ValueKind);

        var resolve = await _client.PatchAsJsonAsync($"/api/issues/{id}/status", new { status = 2, notes = "Recalibrated" });
        var resolved = await resolve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, resolved.GetProperty("status").GetInt32());
        Assert.Equal("Recalibrated", resolved.GetProperty("resolutionNotes").GetString());

        var filtered = await _client.GetJsonAsync($"/api/issues?macAddress={ApiTestHelpers.Escape(ApiTestHelpers.PowerMac)}&status=Resolved");
        Assert.Contains(filtered.EnumerateArray(), i => i.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task TopologyValidate_ReturnsRealAuditTrail()
    {
        var valid = await _client.PostAsJsonAsync("/api/topology/validate", new
        {
            nodeId = "Root", name = "Hub", isActive = true,
            subNodes = new[] { new { nodeId = "Zone-7", name = "Router", isActive = true, subNodes = Array.Empty<object>() } }
        });
        var validBody = await valid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(validBody.GetProperty("isValid").GetBoolean());
        Assert.Contains(validBody.GetProperty("auditTrail").EnumerateArray(), l => l.GetString()!.Contains("Zone-7"));

        var duplicate = await _client.PostAsJsonAsync("/api/topology/validate", new
        {
            nodeId = "Root", name = "Hub", isActive = true,
            subNodes = new[] { new { nodeId = "Root", name = "Loop", isActive = true, subNodes = Array.Empty<object>() } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var dupBody = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Cyclic loop", dupBody.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task BatchAndPowerIngestion_ValidateInput_AndDoNotReingestStaleChannels()
    {
        var badChannel = await _client.PostAsJsonAsync("/api/telemetry/batch", new { macAddress = ApiTestHelpers.PowerMac, channelIndex = 9, readings = new[] { 1.0 }, metricUnit = "W" });
        Assert.Equal(HttpStatusCode.BadRequest, badChannel.StatusCode);

        var batch = await _client.PostAsJsonAsync("/api/telemetry/batch", new { macAddress = ApiTestHelpers.PowerMac, channelIndex = 2, readings = new[] { 401.0, 402.0, 399.5 }, metricUnit = "W" });
        var batchBody = await batch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, batchBody.GetProperty("processedCount").GetInt32());

        var status = await _client.GetJsonAsync("/api/telemetry/buffer/status");
        Assert.Equal(0, status.GetProperty("channels")[2].GetProperty("allocatedLength").GetInt32());

        var power = await _client.PostAsJsonAsync("/api/telemetry/ingest/power", new { macAddress = ApiTestHelpers.PowerMac, value = new { watts = 405.5 }, metricUnit = "W" });
        Assert.Equal(HttpStatusCode.OK, power.StatusCode);

        var unknown = await _client.PostAsJsonAsync("/api/telemetry/ingest/float", new { macAddress = "00:00:00:00:00:99", value = 1.5, metricUnit = "%" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var aggregate = await _client.PostAsJsonAsync("/api/telemetry/power-metrics/aggregate", new { wattsMeter1 = 400.0, wattsMeter2 = 380.2 });
        var agg = await aggregate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(780.2, agg.GetProperty("aggregatedWatts").GetDouble());
        Assert.Equal(19.8, agg.GetProperty("deltaWatts").GetDouble());
        Assert.True(agg.GetProperty("isMeter1Greater").GetBoolean());
    }

    [Fact]
    public async Task OpenApiDocument_IsServedInDevelopment()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class ApiResetTests : IClassFixture<SmartXApiFactory>
{
    private readonly HttpClient _client;

    public ApiResetTests(SmartXApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Reset_IsIdempotent_RestoresBaseline_AndNeverDuplicatesAttachments()
    {
        await _client.PostAsJsonAsync("/api/simulation/drop-heartbeat", new { macAddress = ApiTestHelpers.EnvMac });

        for (int i = 0; i < 3; i++)
        {
            var reset = await _client.PostAsync("/api/simulation/reset", null);
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        }

        var view = await ApiTestHelpers.WaitForAsync(async () =>
        {
            var v = await _client.GetDeviceViewAsync(ApiTestHelpers.EnvMac);
            return v.GetProperty("status").GetProperty("isOnline").GetBoolean() ? v : null;
        }, TimeSpan.FromSeconds(8), "dropped node restored by reset");

        Assert.Equal(2, view.GetProperty("device").GetProperty("attachments").GetArrayLength());
        var download = await _client.GetAsync($"/api/sensors/{ApiTestHelpers.Escape(ApiTestHelpers.EnvMac)}/attachments/hardware_diag.log");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Contains("Hardware AES boot ok", await download.Content.ReadAsStringAsync());

        var buffer = await _client.GetJsonAsync("/api/telemetry/buffer/status");
        Assert.Equal(1000, buffer.GetProperty("totalBufferedReadings").GetInt32());
    }
}
