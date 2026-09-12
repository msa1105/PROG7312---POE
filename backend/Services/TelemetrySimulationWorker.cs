using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartX.Api.Models;

namespace SmartX.Api.Services;

public class TelemetrySimulationWorker : BackgroundService
{
    private readonly DeviceRegistryService _registry;
    private readonly AnomalyDetectionService _anomalyService;
    private readonly Random _rand = new Random();

    // Map to keep track of dropped heartbeats for simulation controls
    public static readonly HashSet<string> DroppedMacs = new HashSet<string>();
    
    // Map to keep track of injected spikes for simulation controls
    public static readonly HashSet<string> SpikeMacs = new HashSet<string>();

    public TelemetrySimulationWorker(DeviceRegistryService registry, AnomalyDetectionService anomalyService)
    {
        _registry = registry;
        _anomalyService = anomalyService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var devices = _registry.GetAllDevices();

            foreach (var device in devices)
            {
                if (DroppedMacs.Contains(device.MacAddress))
                {
                    continue; // Skip simulation to simulate a dropped heartbeat
                }

                bool injectSpike = false;
                if (SpikeMacs.Contains(device.MacAddress))
                {
                    injectSpike = true;
                    SpikeMacs.Remove(device.MacAddress); // One-shot spike
                }

                if (device.Category == SensorCategory.Environmental)
                {
                    // Float moisture/temp
                    double value = injectSpike ? 950.0 : 45.0 + (_rand.NextDouble() * 15.0) + (Math.Clamp(_rand.NextGaussian(), -2, 2));
                    var packet = new TelemetryPacket<float>
                    {
                        MacAddress = device.MacAddress,
                        Value = (float)value,
                        MetricUnit = "%"
                    };
                    _registry.UpdateLatestTelemetry(packet);
                    _anomalyService.IsAnomaly(packet.MacAddress, packet.Value);
                }
                else if (device.Category == SensorCategory.PowerConsumption)
                {
                    // Power metrics 380W - 420W
                    double value = injectSpike ? 1500.0 : 380.0 + (_rand.NextDouble() * 40.0);
                    var packet = new TelemetryPacket<double>
                    {
                        MacAddress = device.MacAddress,
                        Value = value,
                        MetricUnit = "W"
                    };
                    _registry.UpdateLatestTelemetry(packet);
                    _anomalyService.IsAnomaly(packet.MacAddress, packet.Value);
                }
                else if (device.Category == SensorCategory.Actuator)
                {
                    // Actuator states
                    bool value = _rand.NextDouble() > 0.8; // mostly false, occasionally true
                    var packet = new TelemetryPacket<bool>
                    {
                        MacAddress = device.MacAddress,
                        Value = value,
                        MetricUnit = "State"
                    };
                    _registry.UpdateLatestTelemetry(packet);
                }
            }

            await Task.Delay(1500, stoppingToken);
        }
    }
}

public static class RandomExtensions
{
    public static double NextGaussian(this Random rand)
    {
        double u1 = 1.0 - rand.NextDouble();
        double u2 = 1.0 - rand.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
