using Microsoft.Extensions.Hosting;
using SmartX.Api.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Simulates the edge fleet: every transmission window each registered node emits one TelemetryPacket&lt;T&gt;
/// (float for environmental, PowerMetric for power, bool for actuators) unless its heartbeat has been dropped.
/// </summary>
public class TelemetrySimulationWorker : BackgroundService
{
    private readonly DeviceRegistryService _registry;
    private readonly AnomalyDetectionService _anomalyService;
    private readonly SimulationControlService _simulation;
    private readonly Random _rand = new Random();

    // Simulated battery level per node (only touched by the worker thread)
    private readonly Dictionary<string, double> _batteryLevels = new(StringComparer.OrdinalIgnoreCase);

    public TelemetrySimulationWorker(DeviceRegistryService registry, AnomalyDetectionService anomalyService, SimulationControlService simulation)
    {
        _registry = registry;
        _anomalyService = anomalyService;
        _simulation = simulation;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer keeps a fixed cadence (no drift), so packet-delivery scoring stays accurate
        using var timer = new PeriodicTimer(GatewayTiming.TransmissionInterval);
        do
        {
            Tick();
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Emits one transmission window of telemetry for the whole fleet, then checks heartbeat leases.
    /// </summary>
    public void Tick()
    {
        foreach (var device in _registry.GetAllDevices().ToList())
        {
            if (_simulation.IsHeartbeatDropped(device.MacAddress))
            {
                continue; // Skip simulation to simulate a dropped heartbeat
            }

            bool injectSpike = _simulation.TryTakeSpike(device.MacAddress, out double sigmaMultiplier); // One-shot spike
            int rssi = -55 - _rand.Next(0, 18);
            double battery = DrainBattery(device.MacAddress);

            if (device.Category == SensorCategory.Environmental)
            {
                // Float moisture/temp
                double value = injectSpike
                    ? SpikeValue(device.MacAddress, sigmaMultiplier, fallback: 950.0)
                    : 45.0 + (_rand.NextDouble() * 15.0) + Math.Clamp(_rand.NextGaussian(), -2, 2);
                var packet = new TelemetryPacket<float>
                {
                    MacAddress = device.MacAddress,
                    Value = (float)value,
                    MetricUnit = "%",
                    SignalStrengthDbm = rssi,
                    BatteryPercent = battery
                };
                _registry.RecordTelemetry(packet, _anomalyService.Evaluate(packet.MacAddress, packet.Value));
            }
            else if (device.Category == SensorCategory.PowerConsumption)
            {
                // Power metrics 380W - 420W, carried as the PowerMetric value-type struct
                double watts = injectSpike
                    ? SpikeValue(device.MacAddress, sigmaMultiplier, fallback: 1500.0)
                    : 380.0 + (_rand.NextDouble() * 40.0);
                var packet = new TelemetryPacket<PowerMetric>
                {
                    MacAddress = device.MacAddress,
                    Value = new PowerMetric(Math.Round(watts, 2)),
                    MetricUnit = "W",
                    SignalStrengthDbm = rssi,
                    BatteryPercent = battery
                };
                _registry.RecordTelemetry(packet, _anomalyService.Evaluate(packet.MacAddress, packet.Value.Watts));
            }
            else if (device.Category == SensorCategory.Actuator)
            {
                // Actuator states
                bool value = _rand.NextDouble() > 0.8; // mostly false, occasionally true
                var packet = new TelemetryPacket<bool>
                {
                    MacAddress = device.MacAddress,
                    Value = value,
                    MetricUnit = "State",
                    SignalStrengthDbm = rssi,
                    BatteryPercent = battery
                };
                _registry.RecordTelemetry(packet);
            }
        }

        _registry.CheckHeartbeatLeases(DateTime.UtcNow);
    }

    /// <summary>
    /// Produces a reading exactly <paramref name="sigmaMultiplier"/> standard deviations above the node's rolling mean.
    /// </summary>
    private double SpikeValue(string macAddress, double sigmaMultiplier, double fallback)
    {
        var baseline = _anomalyService.GetBaseline(macAddress);
        if (baseline == null) return fallback;

        double sigma = Math.Max(baseline.StdDev, 0.5);
        return baseline.Mean + sigmaMultiplier * sigma;
    }

    private double DrainBattery(string macAddress)
    {
        if (!_batteryLevels.TryGetValue(macAddress, out var level))
        {
            level = 70 + _rand.NextDouble() * 30;
        }
        level = Math.Max(5, level - 0.002);
        _batteryLevels[macAddress] = level;
        return Math.Round(level, 1);
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
