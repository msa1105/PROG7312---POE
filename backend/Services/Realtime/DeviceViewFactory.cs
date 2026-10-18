using SmartX.Api.Models;
using SmartX.Api.Services.Recommendations;

namespace SmartX.Api.Services.Realtime;

/// <summary>Everything the dashboard shows for one device, assembled from the registry, alert sets and command outbox.</summary>
public record DeviceView(
    SensorDevice Device,
    string DeviceName,
    TelemetrySnapshot? LatestTelemetry,
    DeviceConnectionStatus Status,
    List<DeviceAlert> ActiveAlerts,
    bool IsControllable,
    bool? ReportedState,
    int PendingCommands);

public class DeviceViewFactory
{
    private readonly DeviceRegistryService _registry;
    private readonly AlertStateTracker _alerts;
    private readonly CommandDispatcher _commands;

    public DeviceViewFactory(DeviceRegistryService registry, AlertStateTracker alerts, CommandDispatcher commands)
    {
        _registry = registry;
        _alerts = alerts;
        _commands = commands;
    }

    public DeviceView Build(SensorDevice device, DateTime nowUtc) => new(
        device,
        RecommendationService.DeviceName(device),
        _registry.GetLatestTelemetry(device.MacAddress),
        _registry.GetStatus(device.MacAddress, nowUtc),
        _alerts.GetActiveAlerts(device.MacAddress),
        RecommendationService.IsControllable(device),
        _registry.GetReportedState(device.MacAddress),
        _commands.PendingCount(device.MacAddress));

    public List<DeviceView> BuildAll(DateTime nowUtc) => _registry.GetAllDevices().Select(d => Build(d, nowUtc)).ToList();

    public List<DeviceView> BuildFor(IEnumerable<string> macAddresses, DateTime nowUtc) =>
        macAddresses.Select(_registry.GetDevice).Where(d => d != null).Select(d => Build(d!, nowUtc)).ToList();
}
