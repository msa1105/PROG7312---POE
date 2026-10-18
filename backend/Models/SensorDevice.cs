using System.Text.Json.Serialization;

namespace SmartX.Api.Models;

public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}

public class SensorDevice
{
    public string MacAddress { get; set; } = string.Empty;
    public string DeploymentLocation { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }

    // Attachments tracking (server-managed; never accepted from clients)
    public List<DeviceAttachment> Attachments { get; set; } = new();
}

public class DeviceAttachment
{
    public string FileName { get; set; } = string.Empty;

    // Server-side location of the encrypted blob; never exposed to or accepted from clients
    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
