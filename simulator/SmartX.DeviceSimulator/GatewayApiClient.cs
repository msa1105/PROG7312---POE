using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartX.DeviceSimulator;

public record RegisteredDevice(string MacAddress, string DeploymentLocation, DeviceCategory Category);
public record SensorListEntry(RegisteredDevice Device);
public record PendingCommand(Guid Id, string MacAddress, bool TargetState);

public record SpikeDirective(string MacAddress, double Value);
public record CriticalEventDirective(string MacAddress, string Kind, double Value);
public record FloodDirective(string MacAddress, int Count);
public record SimulationDirectives(List<string> DroppedMacs, List<SpikeDirective> Spikes, List<CriticalEventDirective> CriticalEvents, List<FloodDirective> Floods);

/// <summary>
/// The device-side HTTP contract with the Smart-X gateway. Every simulated reading and command acknowledgement
/// goes over the network exactly as a physical ESP32 node would send it.
/// Reference: Microsoft, 2026g. Make HTTP requests with the HttpClient - .NET. [online] Available at: <https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient> [Accessed 19 October 2026].
/// </summary>
public class GatewayApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public GatewayApiClient(HttpClient http) => _http = http;

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("api/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task RegisterAsync(string macAddress, string deploymentLocation, DeviceCategory category, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("api/sensors/register", new { macAddress, deploymentLocation, category = (int)category }, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<RegisteredDevice>> GetFleetAsync(CancellationToken ct)
    {
        var entries = await _http.GetFromJsonAsync<List<SensorListEntry>>("api/sensors", Json, ct) ?? new();
        return entries.Select(e => e.Device).ToList();
    }

    public async Task<SimulationDirectives> ClaimDirectivesAsync(CancellationToken ct)
    {
        using var response = await _http.PostAsync("api/simulation/directives/claim", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SimulationDirectives>(Json, ct)
               ?? new SimulationDirectives(new(), new(), new(), new());
    }

    public Task SendMoistureAsync(MoistureSensor sensor, double value, CancellationToken ct) =>
        PostAsync("api/telemetry/ingest/float", new
        {
            macAddress = sensor.MacAddress,
            timestamp = DateTime.UtcNow,
            value,
            metricUnit = "%",
            signalStrengthDbm = sensor.SignalStrengthDbm,
            batteryPercent = Math.Round(sensor.BatteryPercent, 1)
        }, ct);

    public Task SendPowerAsync(SmartPlugMeter meter, double watts, CancellationToken ct) =>
        PostAsync("api/telemetry/ingest/power", new
        {
            macAddress = meter.MacAddress,
            timestamp = DateTime.UtcNow,
            value = new { watts },
            metricUnit = "W",
            signalStrengthDbm = meter.SignalStrengthDbm,
            batteryPercent = Math.Round(meter.BatteryPercent, 1),
            relayOn = meter.RelayOn
        }, ct);

    public Task SendStateAsync(Actuator actuator, CancellationToken ct) =>
        PostAsync("api/telemetry/ingest/bool", new
        {
            macAddress = actuator.MacAddress,
            timestamp = DateTime.UtcNow,
            value = actuator.IsOn,
            metricUnit = "State",
            signalStrengthDbm = actuator.SignalStrengthDbm,
            batteryPercent = Math.Round(actuator.BatteryPercent, 1)
        }, ct);

    /// <summary>Uploads a buffered backlog of readings in one request (e.g. after a reconnect).</summary>
    public Task SendBacklogAsync(string macAddress, string metricUnit, IEnumerable<(DateTime Timestamp, double Value)> readings, CancellationToken ct) =>
        PostAsync("api/telemetry/ingest/bulk", new
        {
            macAddress,
            metricUnit,
            readings = readings.Select(r => new { timestamp = r.Timestamp, value = r.Value })
        }, ct);

    public async Task<List<PendingCommand>> PollCommandsAsync(string macAddress, CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<PendingCommand>>($"api/devices/{Uri.EscapeDataString(macAddress)}/commands", Json, ct) ?? new();

    public Task AcknowledgeAsync(string macAddress, Guid commandId, bool success, bool? appliedState, string message, CancellationToken ct) =>
        PostAsync($"api/devices/{Uri.EscapeDataString(macAddress)}/commands/{commandId}/ack", new { success, appliedState, message }, ct);

    private async Task PostAsync(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, Json, ct);
        response.EnsureSuccessStatusCode();
    }
}
