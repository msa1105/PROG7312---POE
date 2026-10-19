using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SmartX.Api.Tests;

/// <summary>
/// Boots the real API (processing worker, lease monitor and SignalR broadcaster included) against isolated temp storage.
/// The tests act as devices themselves, so no simulator process is needed.
/// Reference: Microsoft, 2026f. Integration tests in ASP.NET Core. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests> [Accessed 19 October 2026].
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

internal static class Api
{
    public const string RackA = "24:6F:28:9A:12:01";
    public const string RackB = "24:6F:28:9A:12:02";
    public const string SubstationMain = "24:6F:28:9A:12:03";
    public const string Grid2 = "24:6F:28:9A:12:04";
    public const string Solenoid = "24:6F:28:9A:12:05";
    public const string Hvac = "24:6F:28:9A:12:06";

    public static string Escape(string mac) => Uri.EscapeDataString(mac);

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    public static Task<HttpResponseMessage> SendMoistureAsync(this HttpClient client, string mac, double value) =>
        client.PostAsJsonAsync("/api/telemetry/ingest/float", new { macAddress = mac, value, metricUnit = "%", timestamp = DateTime.UtcNow });

    public static Task<HttpResponseMessage> SendPowerAsync(this HttpClient client, string mac, double watts, bool relayOn = true) =>
        client.PostAsJsonAsync("/api/telemetry/ingest/power", new { macAddress = mac, value = new { watts }, metricUnit = "W", relayOn, timestamp = DateTime.UtcNow });

    public static Task<HttpResponseMessage> SendStateAsync(this HttpClient client, string mac, bool state) =>
        client.PostAsJsonAsync("/api/telemetry/ingest/bool", new { macAddress = mac, value = state, metricUnit = "State", timestamp = DateTime.UtcNow });

    public static async Task<JsonElement> DeviceViewAsync(this HttpClient client, string mac)
    {
        var all = await client.GetJsonAsync("/api/sensors");
        return all.EnumerateArray().Single(d => d.GetProperty("device").GetProperty("macAddress").GetString() == mac);
    }

    public static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null) return result;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Timed out waiting for: {description}");
    }

    public static Task<JsonElement> WaitForDeviceAsync(this HttpClient client, string mac, Func<JsonElement, bool> predicate, string description, int seconds = 8) =>
        WaitForAsync<JsonElement?>(async () =>
        {
            var view = await client.DeviceViewAsync(mac);
            return predicate(view) ? view : null;
        }, TimeSpan.FromSeconds(seconds), description).ContinueWith(t => t.Result!.Value);

    public static double? LatestValue(JsonElement view) =>
        view.GetProperty("latestTelemetry") is { ValueKind: JsonValueKind.Object } latest && latest.GetProperty("value").ValueKind == JsonValueKind.Number
            ? latest.GetProperty("value").GetDouble() : null;
}

/// <summary>Ingestion lanes, priority bypass, alert de-duplication, leases and real-time push.</summary>
public class ApiPipelineTests : IClassFixture<SmartXApiFactory>
{
    private readonly SmartXApiFactory _factory;
    private readonly HttpClient _client;

    public ApiPipelineTests(SmartXApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReportsTheSeededFleet()
    {
        var health = await _client.GetJsonAsync("/api/health");
        Assert.Equal("ONLINE", health.GetProperty("status").GetString());
        Assert.True(health.GetProperty("registeredDeviceCount").GetInt32() >= 6);
        Assert.Equal(3.0, health.GetProperty("leaseSeconds").GetDouble());
        Assert.Contains("v2.5", health.GetProperty("gatewayVersion").GetString());
    }

    [Fact]
    public async Task Ingest_IsAcceptedIntoTheRightLane_AndProcessedAsynchronously()
    {
        var routine = await _client.SendMoistureAsync(Api.RackA, 48.5);
        Assert.Equal(HttpStatusCode.Accepted, routine.StatusCode);
        Assert.Equal("Standard", (await routine.ReadJsonAsync()).GetProperty("lane").GetString());

        var view = await _client.WaitForDeviceAsync(Api.RackA, v => Api.LatestValue(v) == 48.5, "routine packet processed");
        Assert.Equal("float", view.GetProperty("latestTelemetry").GetProperty("payloadType").GetString());
        Assert.True(view.GetProperty("status").GetProperty("isOnline").GetBoolean());

        var power = await _client.SendPowerAsync(Api.Grid2, 1250);
        var powerBody = await power.ReadJsonAsync();
        Assert.Equal("Priority", powerBody.GetProperty("lane").GetString());
        Assert.Contains("Power surge", powerBody.GetProperty("priorityReason").GetString());

        var surged = await _client.WaitForDeviceAsync(Api.Grid2, v => v.GetProperty("activeAlerts").EnumerateArray().Any(a => a.GetProperty("kind").GetString() == "ThresholdBreach"), "threshold alert");
        Assert.Equal("High", surged.GetProperty("activeAlerts").EnumerateArray().First(a => a.GetProperty("kind").GetString() == "ThresholdBreach").GetProperty("severity").GetString());

        // Repeated breaches are absorbed by the HashSet: still exactly one active alert
        await _client.SendPowerAsync(Api.Grid2, 1300);
        await _client.SendPowerAsync(Api.Grid2, 1350);
        await Api.WaitForAsync<object>(async () =>
        {
            var stats = await _client.GetJsonAsync("/api/pipeline/stats");
            return stats.GetProperty("alerts").GetProperty("duplicateAlertsSuppressed").GetInt64() >= 2 ? new object() : null;
        }, TimeSpan.FromSeconds(5), "duplicate alerts suppressed");
        var after = await _client.DeviceViewAsync(Api.Grid2);
        Assert.Single(after.GetProperty("activeAlerts").EnumerateArray(), a => a.GetProperty("kind").GetString() == "ThresholdBreach");
    }

    [Fact]
    public async Task CriticalReading_BypassesAStandardBacklog()
    {
        await _client.PostAsJsonAsync("/api/pipeline/processing-cost", new { milliseconds = 5 });
        try
        {
            var readings = Enumerable.Range(0, 800).Select(i => new { timestamp = DateTime.UtcNow.AddMilliseconds(-800 + i), value = 400.0 + i % 10 });
            var flood = await _client.PostAsJsonAsync("/api/telemetry/ingest/bulk", new { macAddress = Api.SubstationMain, metricUnit = "W", readings });
            Assert.Equal(HttpStatusCode.Accepted, flood.StatusCode);
            Assert.Equal(800, (await flood.ReadJsonAsync()).GetProperty("queuedStandard").GetInt32());

            var critical = await _client.SendMoistureAsync(Api.RackB, 9.5);
            Assert.Equal("Priority", (await critical.ReadJsonAsync()).GetProperty("lane").GetString());

            var bypass = await Api.WaitForAsync<JsonElement?>(async () =>
            {
                var stats = await _client.GetJsonAsync("/api/pipeline/stats");
                var last = stats.GetProperty("queue").GetProperty("lastBypass");
                return last.ValueKind == JsonValueKind.Object && last.GetProperty("macAddress").GetString() == Api.RackB ? last : null;
            }, TimeSpan.FromSeconds(10), "priority bypass recorded");

            Assert.True(bypass!.Value.GetProperty("overtookStandardPackets").GetInt32() > 100);
            Assert.True(bypass.Value.GetProperty("waitMs").GetDouble() < 1000);
        }
        finally
        {
            await _client.PostAsJsonAsync("/api/pipeline/processing-cost", new { milliseconds = 0 });
        }

        await Api.WaitForAsync<object>(async () =>
            (await _client.GetJsonAsync("/api/pipeline/stats")).GetProperty("queue").GetProperty("standard").GetProperty("depth").GetInt32() == 0 ? new object() : null,
            TimeSpan.FromSeconds(15), "backlog drained");

        // Backlog readings landed in the SortedDictionary timeline in timestamp order
        var history = await _client.GetJsonAsync($"/api/sensors/{Api.Escape(Api.SubstationMain)}/history?count=800");
        var stamps = history.EnumerateArray().Select(s => s.GetProperty("timestamp").GetDateTime()).ToList();
        Assert.Equal(stamps.OrderBy(t => t), stamps);
    }

    [Fact]
    public async Task Timeline_ReturnsDownsampledPointsForGraphs()
    {
        for (int i = 0; i < 30; i++) await _client.SendMoistureAsync(Api.RackA, 50 + i * 0.1);
        await _client.WaitForDeviceAsync(Api.RackA, v => Api.LatestValue(v) is > 52.8, "readings processed");

        var points = await _client.GetJsonAsync($"/api/sensors/{Api.Escape(Api.RackA)}/timeline?minutes=5&maxPoints=10");
        Assert.InRange(points.GetArrayLength(), 1, 10);
        Assert.True(points.EnumerateArray().Sum(p => p.GetProperty("samples").GetInt32()) >= 30);
    }

    [Fact]
    public async Task Lease_ExpiresAfterTwoWindows_AndDisconnectedSetHoldsTheNodeOnce()
    {
        const string mac = "AA:BB:CC:00:10:01";
        await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = mac, category = 0, deploymentLocation = "Facility Alpha > Lease Zone > Probe" });
        await _client.SendMoistureAsync(mac, 50);

        var offline = await _client.WaitForDeviceAsync(mac, v => !v.GetProperty("status").GetProperty("isOnline").GetBoolean()
            && v.GetProperty("activeAlerts").EnumerateArray().Any(a => a.GetProperty("kind").GetString() == "HeartbeatLost"), "lease expiry", seconds: 10);
        Assert.Single(offline.GetProperty("activeAlerts").EnumerateArray(), a => a.GetProperty("kind").GetString() == "HeartbeatLost");

        await Task.Delay(1200); // several more lease checks
        var stats = await _client.GetJsonAsync("/api/pipeline/stats");
        Assert.Contains(stats.GetProperty("disconnectedNodes").EnumerateArray(), m => m.GetString() == mac);
        Assert.True(stats.GetProperty("alerts").GetProperty("duplicateDisconnectsSuppressed").GetInt64() > 0);

        var offlineSearch = await _client.GetJsonAsync("/api/devices/search?alertState=offline");
        Assert.Contains(offlineSearch.EnumerateArray(), d => d.GetProperty("device").GetProperty("macAddress").GetString() == mac);

        // Telemetry resumes -> removed from the disconnected set
        await _client.SendMoistureAsync(mac, 51);
        var stats2 = await _client.GetJsonAsync("/api/pipeline/stats");
        Assert.DoesNotContain(stats2.GetProperty("disconnectedNodes").EnumerateArray(), m => m.GetString() == mac);
    }

    [Fact]
    public async Task SignalR_PushesDeviceUpdates()
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/gateway"), options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();

        connection.On<JsonElement>("devicesUpdated", devices =>
        {
            foreach (var device in devices.EnumerateArray())
                if (device.GetProperty("device").GetProperty("macAddress").GetString() == Api.Hvac) received.TrySetResult(device);
        });
        await connection.StartAsync();

        await _client.SendStateAsync(Api.Hvac, true);

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(pushed.GetProperty("reportedState").GetBoolean());
    }

    [Fact]
    public async Task Search_FiltersByCategoryAndAlertState()
    {
        var actuators = await _client.GetJsonAsync("/api/devices/search?category=2");
        Assert.All(actuators.EnumerateArray(), d => Assert.Equal(2, d.GetProperty("device").GetProperty("category").GetInt32()));
        Assert.True(actuators.GetArrayLength() >= 2);

        var byText = await _client.GetJsonAsync("/api/devices/search?q=solenoid");
        Assert.Equal(Api.Solenoid, byText.EnumerateArray().Single().GetProperty("device").GetProperty("macAddress").GetString());

        await _client.SendMoistureAsync(Api.RackA, 8);
        await _client.WaitForDeviceAsync(Api.RackA, v => v.GetProperty("activeAlerts").GetArrayLength() > 0, "critical alert");
        var critical = await _client.GetJsonAsync("/api/devices/search?alertState=critical");
        Assert.Contains(critical.EnumerateArray(), d => d.GetProperty("device").GetProperty("macAddress").GetString() == Api.RackA);

        var invalid = await _client.GetAsync("/api/devices/search?alertState=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task UnknownDevicesAndBadInput_AreRejected_NotCrashing()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendMoistureAsync("00:00:00:00:00:99", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/telemetry/ingest/float", new StringContent("{not json", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/pipeline/processing-cost", new { milliseconds = 500 })).StatusCode);

        var health = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}

/// <summary>Command stream round-trip (device poll + ack), Stack undo, directives and recommendations.</summary>
public class ApiCommandStreamTests : IClassFixture<SmartXApiFactory>
{
    private readonly HttpClient _client;

    public ApiCommandStreamTests(SmartXApiFactory factory) => _client = factory.CreateClient();

    private async Task<JsonElement> IssueAsync(string mac, bool state, string? label = null)
    {
        var response = await _client.PostAsJsonAsync("/api/commands", new { targets = new[] { new { macAddress = mac, targetState = state } }, label });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private async Task<List<JsonElement>> PollAsDeviceAsync(string mac) =>
        (await _client.GetJsonAsync($"/api/devices/{Api.Escape(mac)}/commands")).EnumerateArray().ToList();

    /// <summary>Acts as the actuators: acknowledges every in-flight command so each test starts from a settled fleet.</summary>
    private async Task SettleActuatorsAsync()
    {
        foreach (var mac in new[] { Api.Solenoid, Api.Hvac, Api.Grid2, Api.SubstationMain })
            await PollAsDeviceAsync(mac);

        var history = await _client.GetJsonAsync("/api/commands/history?limit=200");
        foreach (var command in history.EnumerateArray().SelectMany(b => b.GetProperty("commands").EnumerateArray()).Where(c => c.GetProperty("isPending").GetBoolean()))
        {
            var mac = command.GetProperty("macAddress").GetString()!;
            await _client.PostAsJsonAsync($"/api/devices/{Api.Escape(mac)}/commands/{command.GetProperty("id").GetString()}/ack",
                new { success = true, appliedState = command.GetProperty("targetState").GetBoolean() });
        }
    }

    [Fact]
    public async Task Command_RoundTrip_QueuedDeliveredAcknowledged_ThenUndo()
    {
        await SettleActuatorsAsync();
        await _client.SendStateAsync(Api.Solenoid, false);
        await _client.WaitForDeviceAsync(Api.Solenoid, v => v.GetProperty("reportedState").ValueKind == JsonValueKind.False, "solenoid reports OFF");
        await PollAsDeviceAsync(Api.Solenoid); // drain commands left by other tests

        var batch = await IssueAsync(Api.Solenoid, true);
        var command = batch.GetProperty("commands")[0];
        Assert.Equal("Queued", command.GetProperty("status").GetString());
        Assert.False(command.GetProperty("previousState").GetBoolean());

        var delivered = Assert.Single(await PollAsDeviceAsync(Api.Solenoid));
        Assert.Equal("Delivered", delivered.GetProperty("status").GetString());
        Assert.Empty(await PollAsDeviceAsync(Api.Solenoid));

        var id = delivered.GetProperty("id").GetString();
        var ack = await _client.PostAsJsonAsync($"/api/devices/{Api.Escape(Api.Solenoid)}/commands/{id}/ack", new { success = true, appliedState = true, message = "energised" });
        Assert.Equal("Acknowledged", (await ack.ReadJsonAsync()).GetProperty("status").GetString());
        await _client.SendStateAsync(Api.Solenoid, true);

        var stack = await _client.GetJsonAsync("/api/commands/stack");
        Assert.True(stack.GetProperty("depth").GetInt32() >= 1);

        var undo = await _client.PostAsync("/api/commands/undo", null);
        var undoBody = await undo.ReadJsonAsync();
        Assert.Equal(batch.GetProperty("id").GetString(), undoBody.GetProperty("reverted").GetProperty("id").GetString());
        var inverse = Assert.Single(await PollAsDeviceAsync(Api.Solenoid));
        Assert.False(inverse.GetProperty("targetState").GetBoolean());
        Assert.Equal("Undo", inverse.GetProperty("source").GetString());

        var history = await _client.GetJsonAsync("/api/commands/history?limit=5");
        Assert.True(history[1].GetProperty("isReverted").GetBoolean());
    }

    [Fact]
    public async Task IrrigationAck_RestartsTheBaselineOfSensorsInTheSameZone()
    {
        await SettleActuatorsAsync();
        for (int i = 0; i < 25; i++) await _client.SendMoistureAsync(Api.RackA, 50 + i % 3);
        await _client.WaitForDeviceAsync(Api.RackA, v => v.GetProperty("latestTelemetry").GetProperty("rollingMean").ValueKind == JsonValueKind.Number, "baseline established");

        await _client.SendStateAsync(Api.Solenoid, false);
        await _client.WaitForDeviceAsync(Api.Solenoid, v => v.GetProperty("reportedState").ValueKind == JsonValueKind.False, "solenoid OFF");
        await PollAsDeviceAsync(Api.Solenoid);
        await IssueAsync(Api.Solenoid, true);
        var command = Assert.Single(await PollAsDeviceAsync(Api.Solenoid));
        await _client.PostAsJsonAsync($"/api/devices/{Api.Escape(Api.Solenoid)}/commands/{command.GetProperty("id").GetString()}/ack", new { success = true, appliedState = true });

        await _client.SendMoistureAsync(Api.RackA, 52.5);
        var view = await _client.WaitForDeviceAsync(Api.RackA, v => Api.LatestValue(v) == 52.5, "post-ack reading");
        Assert.Equal(JsonValueKind.Null, view.GetProperty("latestTelemetry").GetProperty("rollingMean").ValueKind); // warming up a fresh baseline
    }

    [Fact]
    public async Task BulkCommand_IsUndoneAsOneBatch()
    {
        await SettleActuatorsAsync();
        await _client.SendStateAsync(Api.Hvac, true);
        await _client.SendStateAsync(Api.Solenoid, true);
        await _client.WaitForDeviceAsync(Api.Hvac, v => v.GetProperty("reportedState").ValueKind == JsonValueKind.True, "hvac ON");
        await _client.WaitForDeviceAsync(Api.Solenoid, v => v.GetProperty("reportedState").ValueKind == JsonValueKind.True, "solenoid ON");

        var response = await _client.PostAsJsonAsync("/api/commands", new
        {
            label = "Shut down all valves",
            targets = new[] { new { macAddress = Api.Solenoid, targetState = false }, new { macAddress = Api.Hvac, targetState = false } }
        });
        Assert.Equal(2, (await response.ReadJsonAsync()).GetProperty("commands").GetArrayLength());

        var undo = await (await _client.PostAsync("/api/commands/undo", null)).ReadJsonAsync();
        Assert.Equal("Shut down all valves", undo.GetProperty("reverted").GetProperty("label").GetString());
        Assert.Equal(2, undo.GetProperty("undoBatch").GetProperty("commands").GetArrayLength());
        Assert.All(undo.GetProperty("undoBatch").GetProperty("commands").EnumerateArray(), c => Assert.True(c.GetProperty("targetState").GetBoolean()));
    }

    [Fact]
    public async Task RapidCommands_UseTheInFlightStateAsPreviousState_SoUndoRestoresIt()
    {
        await SettleActuatorsAsync();
        await _client.SendStateAsync(Api.Hvac, false);
        await _client.WaitForDeviceAsync(Api.Hvac, v => v.GetProperty("reportedState").ValueKind == JsonValueKind.False, "hvac OFF");
        await PollAsDeviceAsync(Api.Hvac);

        await IssueAsync(Api.Hvac, true);                      // not yet acknowledged by the device
        var off = await IssueAsync(Api.Hvac, false, "Shut down");
        Assert.True(off.GetProperty("commands")[0].GetProperty("previousState").GetBoolean());

        var undo = await (await _client.PostAsync("/api/commands/undo", null)).ReadJsonAsync();
        Assert.Equal("Shut down", undo.GetProperty("reverted").GetProperty("label").GetString());
        Assert.True(undo.GetProperty("undoBatch").GetProperty("commands")[0].GetProperty("targetState").GetBoolean());
    }

    [Fact]
    public async Task Commands_ValidateTargets()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/commands", new { targets = new[] { new { macAddress = Api.RackA, targetState = true } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/commands", new { targets = new[] { new { macAddress = "00:00:00:00:00:99", targetState = true } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/commands", new { targets = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/commands", new
        {
            targets = new[] { new { macAddress = Api.Hvac, targetState = true }, new { macAddress = Api.Hvac, targetState = false } }
        })).StatusCode);
    }

    [Fact]
    public async Task Directives_AreClaimedOnce_ByTheSimulator()
    {
        await _client.PostAsJsonAsync("/api/simulation/critical", new { macAddress = Api.RackB, kind = "MoistureCrash" });
        await _client.PostAsJsonAsync("/api/simulation/flood", new { macAddress = Api.Grid2, count = 1500 });
        await _client.PostAsJsonAsync("/api/simulation/drop-heartbeat", new { macAddress = Api.RackA });

        var claim = await (await _client.PostAsync("/api/simulation/directives/claim", null)).ReadJsonAsync();
        Assert.Equal(9.5, claim.GetProperty("criticalEvents")[0].GetProperty("value").GetDouble());
        Assert.Equal(1500, claim.GetProperty("floods")[0].GetProperty("count").GetInt32());
        Assert.Contains(claim.GetProperty("droppedMacs").EnumerateArray(), m => m.GetString() == Api.RackA);

        var second = await (await _client.PostAsync("/api/simulation/directives/claim", null)).ReadJsonAsync();
        Assert.Equal(0, second.GetProperty("criticalEvents").GetArrayLength());
        Assert.Contains(second.GetProperty("droppedMacs").EnumerateArray(), m => m.GetString() == Api.RackA); // still dropped until restored

        await _client.PostAsJsonAsync("/api/simulation/restore-heartbeat", new { macAddress = Api.RackA });
        var third = await (await _client.PostAsync("/api/simulation/directives/claim", null)).ReadJsonAsync();
        Assert.Equal(0, third.GetProperty("droppedMacs").GetArrayLength());

        var wrongKind = await _client.PostAsJsonAsync("/api/simulation/critical", new { macAddress = Api.RackB, kind = "PowerSurge" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
    }

    [Fact]
    public async Task Recommendations_SuggestIrrigation_WhenRackBDriesOut_AndAcceptIssuesTheCommand()
    {
        await SettleActuatorsAsync();
        await _client.SendStateAsync(Api.Solenoid, false);
        await _client.SendMoistureAsync(Api.RackB, 12);
        await _client.WaitForDeviceAsync(Api.RackB, v => Api.LatestValue(v) == 12, "low moisture processed");

        var recommendations = await _client.GetJsonAsync("/api/recommendations");
        var suggestion = recommendations.GetProperty("suggestions").EnumerateArray()
            .First(s => s.GetProperty("macAddress").GetString() == Api.Solenoid && s.GetProperty("kind").GetString() == "Command");
        Assert.True(suggestion.GetProperty("targetState").GetBoolean());
        Assert.Contains("Hydroponic Rack B", suggestion.GetProperty("triggerLabel").GetString());
        Assert.True(recommendations.GetProperty("model").GetProperty("seededEvents").GetInt32() > 0);
        Assert.Contains(recommendations.GetProperty("insights").EnumerateArray(), i => i.GetProperty("macAddress").GetString() == Api.RackB);

        var accept = await _client.PostAsync($"/api/recommendations/{Uri.EscapeDataString(suggestion.GetProperty("id").GetString()!)}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var batch = (await accept.ReadJsonAsync()).GetProperty("batch");
        Assert.Equal("Suggestion", batch.GetProperty("source").GetString());

        var search = await _client.PostAsJsonAsync("/api/operator/searches", new { query = "solenoid", matchedMacs = new[] { Api.Solenoid } });
        Assert.Equal(1, (await search.ReadJsonAsync()).GetProperty("learned").GetInt32());
        var events = await _client.GetJsonAsync("/api/operator/events?limit=1");
        Assert.Equal($"SEARCH|{Api.Solenoid}", events[0].GetProperty("actionKey").GetString());
    }
}

/// <summary>Part 1 features that must keep working: storage, registration, triage, topology, batch buffer.</summary>
public class ApiPart1RegressionTests : IClassFixture<SmartXApiFactory>
{
    private readonly SmartXApiFactory _factory;
    private readonly HttpClient _client;

    public ApiPart1RegressionTests(SmartXApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AttachmentUpload_IsEncryptedAtRest_AndDownloadVerifiesIntegrity()
    {
        const string mac = "AA:BB:CC:00:00:02";
        await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = mac, category = 1, deploymentLocation = "Facility Alpha > Lab > Meter" });
        var plaintext = "[INFO] Secret calibration constants 42.0";

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(plaintext));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        form.Add(file, "file", "../../escape.log");

        var uploaded = await (await _client.PostAsync($"/api/sensors/{Api.Escape(mac)}/attachments", form)).ReadJsonAsync();
        Assert.Equal("escape.log", uploaded.GetProperty("fileName").GetString());
        Assert.Equal("text/plain", uploaded.GetProperty("mimeType").GetString());

        var blob = Assert.Single(Directory.GetFiles(Path.Combine(_factory.UploadsRoot, "AABBCC000002")));
        Assert.DoesNotContain("Secret calibration", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(blob)));

        var download = await _client.GetAsync($"/api/sensors/{Api.Escape(mac)}/attachments/escape.log");
        Assert.Equal(plaintext, await download.Content.ReadAsStringAsync());

        var bytes = await File.ReadAllBytesAsync(blob);
        bytes[24] ^= 0xFF;
        await File.WriteAllBytesAsync(blob, bytes);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.GetAsync($"/api/sensors/{Api.Escape(mac)}/attachments/escape.log")).StatusCode);
    }

    [Fact]
    public async Task Register_ValidatesInput_AndIgnoresInjectedAttachments()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = "zz", category = 0, deploymentLocation = "F > Z > N" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/sensors/register", new { macAddress = "AA:BB:CC:00:00:03", category = 9, deploymentLocation = "F > Z > N" })).StatusCode);

        var injected = await _client.PostAsJsonAsync("/api/sensors/register", new
        {
            macAddress = "aa:bb:cc:00:00:04", category = 2, deploymentLocation = "Facility Alpha > Lab > Valve",
            attachments = new[] { new { fileName = "win.log", filePath = @"C:\Windows\win.ini" } }
        });
        var body = await injected.ReadJsonAsync();
        Assert.Equal("AA:BB:CC:00:00:04", body.GetProperty("macAddress").GetString());
        Assert.Equal(0, body.GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public async Task Incidents_FullTriageLifecycle()
    {
        var issue = await (await _client.PostAsJsonAsync("/api/issues", new { macAddress = Api.SubstationMain, title = "Harmonic drift", description = "Observed", severity = 0 })).ReadJsonAsync();
        var id = issue.GetProperty("id").GetString()!;
        Assert.Equal(25, issue.GetProperty("xpAwarded").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PatchAsJsonAsync($"/api/issues/{id}/status", new { status = 7 })).StatusCode);
        await _client.PatchAsJsonAsync($"/api/issues/{id}/status", new { status = 1 });
        var resolved = await (await _client.PatchAsJsonAsync($"/api/issues/{id}/status", new { status = 2, notes = "Recalibrated" })).ReadJsonAsync();
        Assert.Equal("Recalibrated", resolved.GetProperty("resolutionNotes").GetString());
    }

    [Fact]
    public async Task Topology_ValidateReturnsAuditTrail_AndLiveTreeIsBuilt()
    {
        var duplicate = await _client.PostAsJsonAsync("/api/topology/validate", new
        {
            nodeId = "Root", name = "Hub", isActive = true,
            subNodes = new[] { new { nodeId = "Root", name = "Loop", isActive = true, subNodes = Array.Empty<object>() } }
        });
        Assert.Contains("Cyclic loop", (await duplicate.ReadJsonAsync()).GetProperty("reason").GetString());

        var live = await _client.GetJsonAsync("/api/topology/live");
        Assert.Equal("Facility Alpha", live.GetProperty("root").GetProperty("name").GetString());
    }

    [Fact]
    public async Task JaggedBatch_AndOperatorOverloads_StillWork()
    {
        var batch = await _client.PostAsJsonAsync("/api/telemetry/batch", new { macAddress = Api.SubstationMain, channelIndex = 2, readings = new[] { 401.0, 402.0 }, metricUnit = "W" });
        Assert.Equal(2, (await batch.ReadJsonAsync()).GetProperty("processedCount").GetInt32());

        var agg = await (await _client.PostAsJsonAsync("/api/telemetry/power-metrics/aggregate", new { wattsMeter1 = 400.0, wattsMeter2 = 380.2 })).ReadJsonAsync();
        Assert.Equal(780.2, agg.GetProperty("aggregatedWatts").GetDouble());
        Assert.Equal(19.8, agg.GetProperty("deltaWatts").GetDouble());

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/openapi/v1.json")).StatusCode);
    }
}

public class ApiResetTests : IClassFixture<SmartXApiFactory>
{
    private readonly HttpClient _client;

    public ApiResetTests(SmartXApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Reset_ClearsRuntimeState_KeepsSeededAttachments_AndReseedsTheModel()
    {
        await _client.SendStateAsync(Api.Hvac, false);
        await _client.WaitForDeviceAsync(Api.Hvac, v => v.GetProperty("reportedState").ValueKind == JsonValueKind.False, "hvac reported");
        await _client.PostAsJsonAsync("/api/commands", new { targets = new[] { new { macAddress = Api.Hvac, targetState = true } } });

        for (int i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/simulation/reset", null)).StatusCode);

        Assert.Equal(0, (await _client.GetJsonAsync("/api/commands/history")).GetArrayLength());
        Assert.Equal(0, (await _client.GetJsonAsync("/api/commands/stack")).GetProperty("depth").GetInt32());

        var rackA = await _client.DeviceViewAsync(Api.RackA);
        Assert.Equal(2, rackA.GetProperty("device").GetProperty("attachments").GetArrayLength());
        var download = await _client.GetAsync($"/api/sensors/{Api.Escape(Api.RackA)}/attachments/hardware_diag.log");
        Assert.Contains("Hardware AES boot ok", await download.Content.ReadAsStringAsync());

        var model = (await _client.GetJsonAsync("/api/recommendations")).GetProperty("model");
        Assert.Equal(model.GetProperty("eventsLearned").GetInt32(), model.GetProperty("seededEvents").GetInt32());
        Assert.Equal(1000, (await _client.GetJsonAsync("/api/telemetry/buffer/status")).GetProperty("totalBufferedReadings").GetInt32());
    }
}
