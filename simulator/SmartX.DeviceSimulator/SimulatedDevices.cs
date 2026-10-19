namespace SmartX.DeviceSimulator;

public enum DeviceCategory
{
    Environmental = 0,
    PowerConsumption = 1,
    Actuator = 2
}

/// <summary>
/// In-memory model of one edge device: its physical state, radio/battery metadata and command handling.
/// The fleet advances every device once per transmission window.
/// </summary>
public abstract class SimulatedDevice
{
    protected SimulatedDevice(string macAddress, string deploymentLocation, Random random)
    {
        MacAddress = macAddress;
        DeploymentLocation = deploymentLocation;
        Random = random;
        BatteryPercent = 70 + random.NextDouble() * 30;
    }

    protected Random Random { get; }
    public string MacAddress { get; }
    public string DeploymentLocation { get; set; }
    public abstract DeviceCategory Category { get; }
    public double BatteryPercent { get; private set; }
    public int SignalStrengthDbm { get; private set; } = -60;

    /// <summary>Zone the device belongs to: its deployment path without the final (device) segment.</summary>
    public string Zone
    {
        get
        {
            var segments = DeploymentLocation.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return segments.Length > 1 ? string.Join(" > ", segments[..^1]) : string.Empty;
        }
    }

    public string Name
    {
        get
        {
            var segments = DeploymentLocation.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return segments.Length > 0 ? segments[^1] : MacAddress;
        }
    }

    public virtual bool IsControllable => false;

    /// <summary>Advances radio and battery state by one transmission window.</summary>
    public void TickMetadata()
    {
        BatteryPercent = Math.Max(5, BatteryPercent - 0.002);
        SignalStrengthDbm = -55 - Random.Next(0, 18);
    }

    /// <summary>Applies a manual override. Returns the state the device ended up in.</summary>
    public virtual (bool Success, bool? AppliedState, string Message) ApplyCommand(bool targetState) =>
        (false, null, $"{Name} is a read-only sensor");
}

/// <summary>Soil-moisture probe. Moisture dries out slowly and rises while an irrigation valve in its zone is open.</summary>
public sealed class MoistureSensor : SimulatedDevice
{
    public const double DryingPerTick = 0.12;
    public const double WateringPerTick = 1.0;
    public const double MaxMoisture = 85.0;

    private double? _forcedNext;

    public MoistureSensor(string macAddress, string deploymentLocation, Random random) : base(macAddress, deploymentLocation, random)
    {
        Moisture = 50 + random.NextDouble() * 12;
    }

    public override DeviceCategory Category => DeviceCategory.Environmental;
    public double Moisture { get; set; }

    public void Step(bool beingWatered)
    {
        Moisture += beingWatered ? WateringPerTick : -DryingPerTick;
        Moisture = Math.Clamp(Moisture, 3, MaxMoisture);
    }

    /// <summary>One-shot outlier reading (sensor glitch) that does not change the physical moisture.</summary>
    public void ForceNextReading(double value) => _forcedNext = value;

    public double Read()
    {
        if (_forcedNext is double forced)
        {
            _forcedNext = null;
            return forced;
        }
        return Math.Round(Math.Clamp(Moisture + Gaussian(Random) * 0.8, 0, 100), 2);
    }

    /// <summary>Standard normal deviate via the Box-Muller transform.</summary>
    /// <remarks>Reference: Box, G.E.P. and Muller, M.E., 1958. A note on the generation of random normal deviates. The Annals of Mathematical Statistics, 29(2), pp.610-611.</remarks>
    internal static double Gaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}

/// <summary>Smart plug with an energy meter and a switchable relay.</summary>
public sealed class SmartPlugMeter : SimulatedDevice
{
    private double? _forcedNext;

    public SmartPlugMeter(string macAddress, string deploymentLocation, Random random) : base(macAddress, deploymentLocation, random) { }

    public override DeviceCategory Category => DeviceCategory.PowerConsumption;
    public override bool IsControllable => true;
    public bool RelayOn { get; private set; } = true;

    public void ForceNextReading(double watts) => _forcedNext = watts;

    public double ReadWatts()
    {
        if (_forcedNext is double forced)
        {
            _forcedNext = null;
            return forced;
        }
        return Math.Round(RelayOn ? 380.0 + Random.NextDouble() * 40.0 : 0.5 + Random.NextDouble() * 1.5, 2);
    }

    public override (bool Success, bool? AppliedState, string Message) ApplyCommand(bool targetState)
    {
        RelayOn = targetState;
        return (true, RelayOn, RelayOn ? "Relay closed - load energised" : "Relay opened - load isolated");
    }
}

/// <summary>Digital actuator (water solenoid, pump, HVAC damper). Its state only changes by command.</summary>
public sealed class Actuator : SimulatedDevice
{
    public Actuator(string macAddress, string deploymentLocation, Random random) : base(macAddress, deploymentLocation, random) { }

    public override DeviceCategory Category => DeviceCategory.Actuator;
    public override bool IsControllable => true;
    public bool IsOn { get; private set; }

    /// <summary>Irrigation actuators water the moisture sensors that share their zone.</summary>
    public bool Irrigates => Name.Contains("Solenoid", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Pump", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Valve", StringComparison.OrdinalIgnoreCase);

    public override (bool Success, bool? AppliedState, string Message) ApplyCommand(bool targetState)
    {
        IsOn = targetState;
        return (true, IsOn, IsOn ? $"{Name} energised" : $"{Name} de-energised");
    }
}

public static class SimulatedDeviceFactory
{
    public static SimulatedDevice Create(string macAddress, string deploymentLocation, DeviceCategory category, Random random) => category switch
    {
        DeviceCategory.Environmental => new MoistureSensor(macAddress, deploymentLocation, random),
        DeviceCategory.PowerConsumption => new SmartPlugMeter(macAddress, deploymentLocation, random),
        _ => new Actuator(macAddress, deploymentLocation, random)
    };
}
