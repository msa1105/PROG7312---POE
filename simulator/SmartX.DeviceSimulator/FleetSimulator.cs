namespace SmartX.DeviceSimulator;

/// <summary>
/// Drives the simulated ESP32 fleet. Once per transmission window it:
///   1. claims fault-injection directives from the gateway,
///   2. advances the physical model (soil dries; open irrigation valves re-water their zone),
///   3. sends one telemetry packet per online device over HTTP,
///   4. polls each controllable device's command outbox, applies the commands and acknowledges them.
/// The loop uses PeriodicTimer for a drift-free cadence.
/// Reference: Microsoft, 2025g. PeriodicTimer class (System.Threading). [online] Available at: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer> [Accessed 19 October 2026].
/// </summary>
public class FleetSimulator
{
    public static readonly TimeSpan TransmissionInterval = TimeSpan.FromMilliseconds(1500);
    private const int FleetRefreshTicks = 8;

    public static readonly (string Mac, string Location, DeviceCategory Category)[] DefaultFleet =
    {
        ("24:6F:28:9A:12:01", "Facility Alpha > Greenhouse 1 > Hydroponic Rack A", DeviceCategory.Environmental),
        ("24:6F:28:9A:12:02", "Facility Alpha > Greenhouse 1 > Hydroponic Rack B", DeviceCategory.Environmental),
        ("24:6F:28:9A:12:03", "Facility Alpha > Substation Main", DeviceCategory.PowerConsumption),
        ("24:6F:28:9A:12:04", "Facility Alpha > Substation Grid 2", DeviceCategory.PowerConsumption),
        ("24:6F:28:9A:12:05", "Facility Alpha > Greenhouse 1 > Water Solenoid 1", DeviceCategory.Actuator),
        ("24:6F:28:9A:12:06", "Facility Alpha > HVAC Bay", DeviceCategory.Actuator),
    };

    private readonly GatewayApiClient _api;
    private readonly Random _random;
    private readonly Action<string> _log;
    private readonly Dictionary<string, SimulatedDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _dropped = new(StringComparer.OrdinalIgnoreCase);
    private long _tick;

    public FleetSimulator(GatewayApiClient api, Random? random = null, Action<string>? log = null)
    {
        _api = api;
        _random = random ?? new Random();
        _log = log ?? Console.WriteLine;
    }

    public IReadOnlyDictionary<string, SimulatedDevice> Devices => _devices;

    public async Task RunAsync(CancellationToken ct)
    {
        await WaitForGatewayAsync(ct);
        await RegisterDefaultFleetAsync(ct);
        await RefreshFleetAsync(ct);
        _log($"Simulating {_devices.Count} device(s) every {TransmissionInterval.TotalSeconds:F1}s. Press Ctrl+C to stop.");

        using var timer = new PeriodicTimer(TransmissionInterval);
        do
        {
            try
            {
                await TickAsync(ct);
            }
            catch (HttpRequestException ex)
            {
                _log($"[WARN] Gateway unreachable ({ex.Message}); retrying next window.");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task TickAsync(CancellationToken ct)
    {
        if (++_tick % FleetRefreshTicks == 0) await RefreshFleetAsync(ct);

        await ApplyDirectivesAsync(ct);
        StepPhysics();

        var online = _devices.Values.Where(d => !_dropped.Contains(d.MacAddress)).ToList();
        await Task.WhenAll(online.Select(d => SendTelemetryAsync(d, ct)));
        await Task.WhenAll(online.Where(d => d.IsControllable).Select(d => ProcessCommandsAsync(d, ct)));
    }

    /// <summary>Open irrigation actuators water every moisture sensor in the same zone; all other sensors dry out.</summary>
    public void StepPhysics()
    {
        var wateredZones = _devices.Values.OfType<Actuator>()
            .Where(a => a.IsOn && a.Irrigates && !_dropped.Contains(a.MacAddress))
            .Select(a => a.Zone)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var device in _devices.Values)
        {
            device.TickMetadata();
            if (device is MoistureSensor sensor) sensor.Step(wateredZones.Contains(sensor.Zone));
        }
    }

    private async Task ApplyDirectivesAsync(CancellationToken ct)
    {
        var directives = await _api.ClaimDirectivesAsync(ct);
        _dropped = directives.DroppedMacs.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Backlog uploads are sent first so any critical reading in this window arrives behind them
        foreach (var flood in directives.Floods)
        {
            if (!_devices.TryGetValue(flood.MacAddress, out var device)) continue;
            await SendBacklogAsync(device, flood.Count, ct);
            _log($"[FLOOD] {device.Name}: uploaded {flood.Count:N0} buffered readings");
        }

        foreach (var spike in directives.Spikes)
        {
            switch (_devices.GetValueOrDefault(spike.MacAddress))
            {
                case MoistureSensor sensor: sensor.ForceNextReading(spike.Value); break;
                case SmartPlugMeter meter: meter.ForceNextReading(spike.Value); break;
            }
            _log($"[SPIKE] {spike.MacAddress}: next reading {spike.Value}");
        }

        foreach (var critical in directives.CriticalEvents)
        {
            switch (_devices.GetValueOrDefault(critical.MacAddress))
            {
                case MoistureSensor sensor:
                    sensor.Moisture = critical.Value; // physical drop: stays low until the zone is watered
                    break;
                case SmartPlugMeter meter:
                    meter.ForceNextReading(critical.Value);
                    break;
            }
            _log($"[CRITICAL] {critical.Kind} on {critical.MacAddress}: {critical.Value}");
        }
    }

    private Task SendTelemetryAsync(SimulatedDevice device, CancellationToken ct) => device switch
    {
        MoistureSensor sensor => _api.SendMoistureAsync(sensor, sensor.Read(), ct),
        SmartPlugMeter meter => _api.SendPowerAsync(meter, meter.ReadWatts(), ct),
        Actuator actuator => _api.SendStateAsync(actuator, ct),
        _ => Task.CompletedTask
    };

    private async Task ProcessCommandsAsync(SimulatedDevice device, CancellationToken ct)
    {
        foreach (var command in await _api.PollCommandsAsync(device.MacAddress, ct))
        {
            var (success, applied, message) = device.ApplyCommand(command.TargetState);
            await _api.AcknowledgeAsync(device.MacAddress, command.Id, success, applied, message, ct);
            _log($"[COMMAND] {device.Name} -> {(command.TargetState ? "ON" : "OFF")}: {message}");
        }
    }

    private Task SendBacklogAsync(SimulatedDevice device, int count, CancellationToken ct)
    {
        // Readings buffered over the last count x 50 ms, in chronological order
        var start = DateTime.UtcNow - TimeSpan.FromMilliseconds(count * 50.0);
        IEnumerable<(DateTime, double)> readings = device switch
        {
            MoistureSensor sensor => Enumerable.Range(0, count).Select(i => (start.AddMilliseconds(i * 50.0), Math.Round(sensor.Moisture + MoistureSensor.Gaussian(_random) * 0.8, 2))),
            SmartPlugMeter meter => Enumerable.Range(0, count).Select(i => (start.AddMilliseconds(i * 50.0), meter.ReadWatts())),
            _ => Enumerable.Empty<(DateTime, double)>()
        };
        return _api.SendBacklogAsync(device.MacAddress, device is SmartPlugMeter ? "W" : "%", readings, ct);
    }

    private async Task WaitForGatewayAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!await _api.IsHealthyAsync(ct))
        {
            if (attempt++ % 5 == 0) _log("Waiting for the Smart-X gateway API...");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private async Task RegisterDefaultFleetAsync(CancellationToken ct)
    {
        foreach (var (mac, location, category) in DefaultFleet)
            await _api.RegisterAsync(mac, location, category, ct);
    }

    /// <summary>Picks up devices registered from the dashboard so they are simulated too.</summary>
    public async Task RefreshFleetAsync(CancellationToken ct)
    {
        foreach (var registered in await _api.GetFleetAsync(ct))
        {
            if (_devices.TryGetValue(registered.MacAddress, out var existing))
            {
                if (existing.Category == registered.Category)
                {
                    existing.DeploymentLocation = registered.DeploymentLocation;
                    continue;
                }
            }

            _devices[registered.MacAddress] = SimulatedDeviceFactory.Create(registered.MacAddress, registered.DeploymentLocation, registered.Category, _random);
            if (existing == null && _tick > 0) _log($"[FLEET] Now simulating {registered.MacAddress} ({registered.DeploymentLocation})");
        }
    }
}
