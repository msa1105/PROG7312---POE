using SmartX.Api.Models;

namespace SmartX.Api.Services;

/// <summary>
/// Stores device attachments as encrypted blobs under the uploads root and tracks them on the device registry.
/// Blob names are server-generated, so client-supplied file names never influence file-system paths.
/// </summary>
public class AttachmentService
{
    public const int MaxFileNameLength = 128;

    private static readonly Dictionary<string, string> MimeTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bin"] = "application/octet-stream",
        [".json"] = "application/json",
        [".log"] = "text/plain",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    public static IReadOnlyCollection<string> AllowedExtensions => MimeTypesByExtension.Keys;

    private readonly FileEncryptionService _encryption;
    private readonly DeviceRegistryService _registry;

    public string UploadsRoot { get; }

    public AttachmentService(string uploadsRoot, FileEncryptionService encryption, DeviceRegistryService registry)
    {
        UploadsRoot = Path.GetFullPath(uploadsRoot);
        _encryption = encryption;
        _registry = registry;
    }

    /// <summary>
    /// Validates a client-supplied file name: strips any directory components and rejects
    /// empty, over-long, reserved or unsupported names.
    /// </summary>
    public static bool TrySanitizeFileName(string? rawFileName, out string safeName, out string error)
    {
        safeName = string.Empty;
        error = string.Empty;

        var name = Path.GetFileName((rawFileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (string.IsNullOrEmpty(name) || name is "." or "..")
        {
            error = "File name is empty or invalid.";
            return false;
        }
        if (name.Length > MaxFileNameLength)
        {
            error = $"File name exceeds {MaxFileNameLength} characters.";
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl))
        {
            error = "File name contains invalid characters.";
            return false;
        }
        if (!MimeTypesByExtension.ContainsKey(Path.GetExtension(name)))
        {
            error = $"File format not supported. Allowed: {string.Join(", ", AllowedExtensions)}.";
            return false;
        }

        safeName = name;
        return true;
    }

    /// <summary>
    /// Streams <paramref name="content"/> through AES-256 encryption to disk and registers the attachment.
    /// An existing attachment with the same name is replaced and its old blob deleted.
    /// </summary>
    public async Task<DeviceAttachment> SaveAsync(string macAddress, string fileName, Stream content, long sizeBytes)
    {
        var device = _registry.GetDevice(macAddress) ?? throw new KeyNotFoundException($"Device '{macAddress}' is not registered.");
        if (!TrySanitizeFileName(fileName, out var safeName, out var error))
            throw new ArgumentException(error, nameof(fileName));

        var deviceDirectory = Path.Combine(UploadsRoot, device.MacAddress.Replace(":", ""));
        Directory.CreateDirectory(deviceDirectory);

        var blobPath = Path.Combine(deviceDirectory, $"{Guid.NewGuid():N}.enc");
        await _encryption.EncryptStreamAsync(content, blobPath);

        var attachment = new DeviceAttachment
        {
            FileName = safeName,
            FilePath = blobPath,
            SizeBytes = sizeBytes,
            MimeType = MimeTypesByExtension[Path.GetExtension(safeName)]
        };

        var replaced = _registry.AddAttachment(device.MacAddress, attachment);
        if (replaced != null && File.Exists(replaced.FilePath))
        {
            File.Delete(replaced.FilePath);
        }

        return attachment;
    }

    /// <summary>
    /// Deletes encrypted blobs that no registered attachment references. The registry is in-memory,
    /// so blobs left by a previous process run are unreachable and would otherwise accumulate.
    /// </summary>
    public int PurgeOrphanedBlobs()
    {
        if (!Directory.Exists(UploadsRoot)) return 0;

        var referenced = _registry.GetAllDevices()
            .SelectMany(d => { lock (d.Attachments) return d.Attachments.Select(a => Path.GetFullPath(a.FilePath)).ToList(); })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int removed = 0;
        foreach (var blob in Directory.EnumerateFiles(UploadsRoot, "*.enc", SearchOption.AllDirectories).ToList())
        {
            if (referenced.Contains(Path.GetFullPath(blob))) continue;
            File.Delete(blob);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// Opens a verified, decrypting stream for an attachment, or null when it does not exist.
    /// </summary>
    public async Task<(Stream Content, DeviceAttachment Attachment)?> OpenAsync(string macAddress, string fileName)
    {
        var attachment = _registry.GetAttachment(macAddress, fileName);
        if (attachment == null || !File.Exists(attachment.FilePath)) return null;

        var stream = await _encryption.DecryptToStreamAsync(attachment.FilePath);
        return (stream, attachment);
    }
}
