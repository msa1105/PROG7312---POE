using SmartX.DeviceSimulator;
using Xunit;

namespace SmartX.Api.Tests;

public class DeviceSimulatorTests
{
    [Fact]
    public void MoistureSensor_DriesOut_AndRisesWhileWatered()
    {
        var sensor = new MoistureSensor("AA:00:00:00:00:01", "F > Greenhouse 1 > Rack A", new Random(1)) { Moisture = 50 };

        sensor.Step(beingWatered: false);
        Assert.Equal(50 - MoistureSensor.DryingPerTick, sensor.Moisture, 6);

        sensor.Step(beingWatered: true);
        Assert.Equal(50 - MoistureSensor.DryingPerTick + MoistureSensor.WateringPerTick, sensor.Moisture, 6);
    }

    [Fact]
    public void MoistureSensor_ForcedReading_IsOneShot_AndDoesNotChangeTheSoil()
    {
        var sensor = new MoistureSensor("AA:00:00:00:00:01", "F > Z > Rack", new Random(1)) { Moisture = 50 };
        sensor.ForceNextReading(99);

        Assert.Equal(99, sensor.Read());
        Assert.InRange(sensor.Read(), 45, 55);
        Assert.Equal(50, sensor.Moisture);
    }

    [Fact]
    public void SmartPlug_RelayCommand_CutsAndRestoresLoad()
    {
        var plug = new SmartPlugMeter("AA:00:00:00:00:02", "F > Substation", new Random(1));
        Assert.InRange(plug.ReadWatts(), 380, 420);

        var (success, applied, _) = plug.ApplyCommand(false);
        Assert.True(success);
        Assert.False(applied);
        Assert.InRange(plug.ReadWatts(), 0, 2);
    }

    [Fact]
    public void Actuator_OnlyChangesStateByCommand_AndKnowsIfItIrrigates()
    {
        var valve = new Actuator("AA:00:00:00:00:03", "F > Greenhouse 1 > Water Solenoid 1", new Random(1));
        var hvac = new Actuator("AA:00:00:00:00:04", "F > HVAC Bay", new Random(1));

        Assert.False(valve.IsOn);
        Assert.True(valve.ApplyCommand(true).Success);
        Assert.True(valve.IsOn);
        Assert.True(valve.Irrigates);
        Assert.False(hvac.Irrigates);
        Assert.Equal("F > Greenhouse 1", valve.Zone);
    }

    [Fact]
    public void ReadOnlySensor_RejectsCommands()
    {
        var sensor = new MoistureSensor("AA:00:00:00:00:01", "F > Z > Rack", new Random(1));
        Assert.False(sensor.IsControllable);
        Assert.False(sensor.ApplyCommand(true).Success);
    }
}
