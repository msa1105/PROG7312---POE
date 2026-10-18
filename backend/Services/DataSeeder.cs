using SmartX.Api.Models;
using System.Text;

namespace SmartX.Api.Services;

/// <summary>
/// Seeds the demonstration fleet. Idempotent: re-running it (e.g. on simulation reset) restores the
/// seeded devices and attachments without creating duplicates.
/// </summary>
public class DataSeeder
{
    private readonly DeviceRegistryService _registry;
    private readonly JaggedTelemetryBuffer _buffer;
    private readonly AttachmentService _attachments;

    public DataSeeder(DeviceRegistryService registry, JaggedTelemetryBuffer buffer, AttachmentService attachments)
    {
        _registry = registry;
        _buffer = buffer;
        _attachments = attachments;
    }

    public async Task SeedAsync()
    {
        // 1. Seed 6 specific mock devices
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:01", Category = SensorCategory.Environmental, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Hydroponic Rack A" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:02", Category = SensorCategory.Environmental, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Hydroponic Rack B" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:03", Category = SensorCategory.PowerConsumption, DeploymentLocation = "Facility Alpha > Substation Main" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:04", Category = SensorCategory.PowerConsumption, DeploymentLocation = "Facility Alpha > Substation Grid 2" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:05", Category = SensorCategory.Actuator, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Water Solenoid 1" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:06", Category = SensorCategory.Actuator, DeploymentLocation = "Facility Alpha > HVAC Bay" });

        // 2. Pre-populate the JaggedTelemetryBuffer with 1,000 historical readings (4 channels x 250)
        var rand = new Random();
        for (int ch = 0; ch < _buffer.ChannelCount; ch++)
        {
            var readings = new double[250];
            for (int i = 0; i < readings.Length; i++)
            {
                readings[i] = 400.0 + (rand.NextDouble() * 20.0 - 10.0);
            }
            _buffer.LoadChannelBatch(ch, readings);
        }

        // 3. Create mock physical attachment files with AES-256 encryption at rest (replaced, not duplicated, on re-seed)
        await SeedAttachmentAsync("24:6F:28:9A:12:01", "esp32_firmware_v2.json",
            "{\"version\": \"v2.0-mesh\", \"built\": \"2026-09-12\", \"encryption\": \"AES-256-CBC\"}");
        await SeedAttachmentAsync("24:6F:28:9A:12:01", "hardware_diag.log",
            "[INFO] Hardware AES boot ok\n[INFO] Mesh node 24:6F:28:9A:12:01 online\n[WARN] High load transient recorded");
    }

    private async Task SeedAttachmentAsync(string macAddress, string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        await _attachments.SaveAsync(macAddress, fileName, stream, bytes.Length);
    }
}
