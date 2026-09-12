using SmartX.Api.Models;
using System.IO;
using System.Collections.Generic;

namespace SmartX.Api.Services;

public class DataSeeder
{
    private readonly DeviceRegistryService _registry;
    private readonly JaggedTelemetryBuffer _buffer;

    public DataSeeder(DeviceRegistryService registry, JaggedTelemetryBuffer buffer)
    {
        _registry = registry;
        _buffer = buffer;
    }

    public void Seed()
    {
        // 1. Seed 6 specific mock devices
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:01", Category = SensorCategory.Environmental, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Hydroponic Rack A" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:02", Category = SensorCategory.Environmental, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Hydroponic Rack B" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:03", Category = SensorCategory.PowerConsumption, DeploymentLocation = "Facility Alpha > Substation Main" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:04", Category = SensorCategory.PowerConsumption, DeploymentLocation = "Facility Alpha > Substation Grid 2" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:05", Category = SensorCategory.Actuator, DeploymentLocation = "Facility Alpha > Greenhouse 1 > Water Solenoid 1" });
        _registry.RegisterDevice(new SensorDevice { MacAddress = "24:6F:28:9A:12:06", Category = SensorCategory.Actuator, DeploymentLocation = "Facility Alpha > HVAC Bay" });

        // 2. Pre-populate the JaggedTelemetryBuffer with 1,000 historical readings
        var rand = new Random();
        for (int ch = 0; ch < 4; ch++)
        {
            var readings = new double[250];
            for (int i = 0; i < 250; i++)
            {
                readings[i] = 400.0 + (rand.NextDouble() * 20.0 - 10.0);
            }
            _buffer.LoadChannelBatch(ch, readings);
        }

        // 3. Create mock physical attachment files on disk
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "246F289A1201");
        Directory.CreateDirectory(uploadsDir);
        
        var fwPath = Path.Combine(uploadsDir, "esp32_firmware_v2.json");
        File.WriteAllText(fwPath, "{\"version\": \"v2.0\", \"built\": \"2026-09-12\"}");
        _registry.AddAttachment("24:6F:28:9A:12:01", new DeviceAttachment { FileName = "esp32_firmware_v2.json", FilePath = fwPath, SizeBytes = new FileInfo(fwPath).Length, MimeType = "application/json" });

        var logPath = Path.Combine(uploadsDir, "hardware_diag.log");
        File.WriteAllText(logPath, "[INFO] Hardware init ok\n[WARN] High load detected");
        _registry.AddAttachment("24:6F:28:9A:12:01", new DeviceAttachment { FileName = "hardware_diag.log", FilePath = logPath, SizeBytes = new FileInfo(logPath).Length, MimeType = "text/plain" });
    }
}
