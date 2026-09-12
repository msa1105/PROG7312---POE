namespace SmartX.Api.Services;

using SmartX.Api.Models;
using System.Collections.Concurrent;

public class DeviceRegistryService
{
    private readonly ConcurrentDictionary<string, SensorDevice> _devices = new();
    
    // Store latest state of any device (mac address -> latest packet)
    private readonly ConcurrentDictionary<string, object> _latestTelemetry = new();

    public SensorDevice RegisterDevice(SensorDevice device)
    {
        _devices.AddOrUpdate(device.MacAddress, device, (key, existing) => 
        {
            existing.DeploymentLocation = device.DeploymentLocation;
            existing.Category = device.Category;
            return existing;
        });
        return _devices[device.MacAddress];
    }

    public void AddAttachment(string macAddress, DeviceAttachment attachment)
    {
        if (_devices.TryGetValue(macAddress, out var device))
        {
            device.Attachments.Add(attachment);
        }
    }

    public SensorDevice? GetDevice(string macAddress)
    {
        _devices.TryGetValue(macAddress, out var device);
        return device;
    }

    public IEnumerable<SensorDevice> GetAllDevices()
    {
        return _devices.Values;
    }

    public void UpdateLatestTelemetry<T>(TelemetryPacket<T> packet)
    {
        _latestTelemetry[packet.MacAddress] = packet;
    }

    public object? GetLatestTelemetry(string macAddress)
    {
        _latestTelemetry.TryGetValue(macAddress, out var telemetry);
        return telemetry;
    }
}
