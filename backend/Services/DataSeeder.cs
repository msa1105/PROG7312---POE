using SmartX.Api.Models;
using System.IO;
using System.Text;

namespace SmartX.Api.Services;

public class DataSeeder
{
    private readonly DeviceRegistryService _registry;
    private readonly JaggedTelemetryBuffer _buffer;
    private readonly FileEncryptionService _encryptionService;

    public DataSeeder(DeviceRegistryService registry, JaggedTelemetryBuffer buffer, FileEncryptionService encryptionService)
    {
        _registry = registry;
        _buffer = buffer;
        _encryptionService = encryptionService;
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

        // 3. Create mock physical attachment files with AES-256 encryption at rest
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "246F289A1201");
        Directory.CreateDirectory(uploadsDir);
        
        var fwRawBytes = Encoding.UTF8.GetBytes("{\"version\": \"v2.0-mesh\", \"built\": \"2026-09-12\", \"encryption\": \"AES-256-CBC\"}");
        var fwEncPath = Path.Combine(uploadsDir, "esp32_firmware_v2.json.enc");
        using (var mem = new MemoryStream(fwRawBytes))
        {
            _encryptionService.EncryptStreamAsync(mem, fwEncPath).GetAwaiter().GetResult();
        }
        _registry.AddAttachment("24:6F:28:9A:12:01", new DeviceAttachment 
        { 
            FileName = "esp32_firmware_v2.json", 
            FilePath = fwEncPath, 
            SizeBytes = fwRawBytes.Length, 
            MimeType = "application/json" 
        });

        var logRawBytes = Encoding.UTF8.GetBytes("[INFO] Hardware AES boot ok\n[INFO] Mesh node 24:6F:28:9A:12:01 online\n[WARN] High load transient recorded");
        var logEncPath = Path.Combine(uploadsDir, "hardware_diag.log.enc");
        using (var mem = new MemoryStream(logRawBytes))
        {
            _encryptionService.EncryptStreamAsync(mem, logEncPath).GetAwaiter().GetResult();
        }
        _registry.AddAttachment("24:6F:28:9A:12:01", new DeviceAttachment 
        { 
            FileName = "hardware_diag.log", 
            FilePath = logEncPath, 
            SizeBytes = logRawBytes.Length, 
            MimeType = "text/plain" 
        });
    }
}
