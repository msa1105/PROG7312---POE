using SmartX.Api.Models;
using SmartX.Api.Services;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SmartX.Api.Tests;

public sealed class SecurityAndStorageTests : IDisposable
{
    private const string Mac = "24:6F:28:9A:12:01";
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "smartx-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private static FileEncryptionService NewEncryption() => new(RandomNumberGenerator.GetBytes(32));

    private async Task<string> EncryptToTempFileAsync(FileEncryptionService service, string content)
    {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N") + ".enc");
        using var src = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await service.EncryptStreamAsync(src, path);
        return path;
    }

    private static async Task<string> DecryptAsync(FileEncryptionService service, string path)
    {
        await using var stream = await service.DecryptToStreamAsync(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Encryption_RoundTripsLargePayload_AcrossManyBlocks()
    {
        var service = NewEncryption();
        var content = string.Concat(Enumerable.Repeat("telemetry-line-0123456789\n", 20_000));
        var path = await EncryptToTempFileAsync(service, content);

        Assert.Equal(content, await DecryptAsync(service, path));
    }

    [Fact]
    public async Task Encryption_DetectsTampering()
    {
        var service = NewEncryption();
        var path = await EncryptToTempFileAsync(service, "Sensitive hardware diagnostic log contents.");

        var bytes = await File.ReadAllBytesAsync(path);
        bytes[20] ^= 0x01; // flip one ciphertext bit
        await File.WriteAllBytesAsync(path, bytes);

        await Assert.ThrowsAsync<CryptographicException>(() => service.DecryptToStreamAsync(path));
    }

    [Fact]
    public async Task Encryption_RejectsWrongKey_AndTruncatedFiles()
    {
        var path = await EncryptToTempFileAsync(NewEncryption(), "payload");
        await Assert.ThrowsAsync<CryptographicException>(() => NewEncryption().DecryptToStreamAsync(path));

        var truncated = Path.Combine(_tempRoot, "truncated.enc");
        await File.WriteAllBytesAsync(truncated, new byte[20]);
        await Assert.ThrowsAsync<CryptographicException>(() => NewEncryption().DecryptToStreamAsync(truncated));
    }

    [Fact]
    public void MasterKey_IsGeneratedOnce_ThenReused_AndConfiguredKeyIsValidated()
    {
        var keyDir = Path.Combine(_tempRoot, "keys");
        var first = FileEncryptionService.LoadOrCreateMasterKey(null, keyDir);
        var second = FileEncryptionService.LoadOrCreateMasterKey(null, keyDir);
        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);

        var configured = RandomNumberGenerator.GetBytes(32);
        Assert.Equal(configured, FileEncryptionService.LoadOrCreateMasterKey(Convert.ToBase64String(configured), keyDir));
        Assert.Throws<InvalidOperationException>(() => FileEncryptionService.LoadOrCreateMasterKey(Convert.ToBase64String(new byte[16]), keyDir));
    }

    [Theory]
    [InlineData("../../etc/passwd.log", true, "passwd.log")]
    [InlineData(@"..\..\Windows\boot.txt", true, "boot.txt")]
    [InlineData("firmware.BIN", true, "firmware.BIN")]
    [InlineData("payload.exe", false, "")]
    [InlineData("..", false, "")]
    [InlineData("", false, "")]
    public void FileNameSanitizer_StripsPaths_AndEnforcesAllowList(string raw, bool expectedValid, string expectedName)
    {
        var valid = AttachmentService.TrySanitizeFileName(raw, out var safe, out _);
        Assert.Equal(expectedValid, valid);
        Assert.Equal(expectedName, safe);
    }

    [Fact]
    public async Task Attachments_SameNameReplacesPreviousBlob_AndMimeComesFromExtension()
    {
        var registry = new DeviceRegistryService();
        registry.RegisterDevice(new SensorDevice { MacAddress = Mac, DeploymentLocation = "F > Z > N" });
        var attachments = new AttachmentService(Path.Combine(_tempRoot, "Uploads"), NewEncryption(), registry);

        using (var v1 = new MemoryStream(Encoding.UTF8.GetBytes("version 1")))
            await attachments.SaveAsync(Mac, "diag.log", v1, 9);
        using (var v2 = new MemoryStream(Encoding.UTF8.GetBytes("version 2")))
            await attachments.SaveAsync(Mac, "diag.log", v2, 9);

        var stored = Assert.Single(registry.GetDevice(Mac)!.Attachments);
        Assert.Equal("text/plain", stored.MimeType);
        Assert.Single(Directory.GetFiles(Path.Combine(attachments.UploadsRoot, "246F289A1201")));

        var opened = await attachments.OpenAsync(Mac, "DIAG.LOG");
        Assert.NotNull(opened);
        await using var content = opened.Value.Content;
        using var reader = new StreamReader(content);
        Assert.Equal("version 2", await reader.ReadToEndAsync());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => attachments.SaveAsync("00:00:00:00:00:00", "x.log", new MemoryStream(), 0));
    }

    [Fact]
    public async Task PurgeOrphanedBlobs_RemovesBlobsFromPreviousRun_KeepsReferencedOnes()
    {
        var uploads = Path.Combine(_tempRoot, "Uploads");
        var encryption = NewEncryption();

        // Previous process run: seed, then "restart" with a fresh in-memory registry
        var oldRegistry = new DeviceRegistryService();
        await new DataSeeder(oldRegistry, new JaggedTelemetryBuffer(), new AttachmentService(uploads, encryption, oldRegistry)).SeedAsync();

        var registry = new DeviceRegistryService();
        var attachments = new AttachmentService(uploads, encryption, registry);
        await new DataSeeder(registry, new JaggedTelemetryBuffer(), attachments).SeedAsync();
        Assert.Equal(4, Directory.GetFiles(uploads, "*.enc", SearchOption.AllDirectories).Length);

        Assert.Equal(2, attachments.PurgeOrphanedBlobs());
        Assert.Equal(2, Directory.GetFiles(uploads, "*.enc", SearchOption.AllDirectories).Length);
        var opened = await attachments.OpenAsync(Mac, "hardware_diag.log");
        Assert.NotNull(opened);
        await opened.Value.Content.DisposeAsync();
    }

    [Fact]
    public async Task Seeder_IsIdempotent_NoDuplicateAttachmentsOnReseed()
    {
        var registry = new DeviceRegistryService();
        var buffer = new JaggedTelemetryBuffer();
        var attachments = new AttachmentService(Path.Combine(_tempRoot, "Uploads"), NewEncryption(), registry);
        var seeder = new DataSeeder(registry, buffer, attachments);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Equal(6, registry.GetAllDevices().Count());
        Assert.Equal(2, registry.GetDevice(Mac)!.Attachments.Count);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(attachments.UploadsRoot, "246F289A1201")).Length);
        Assert.Equal(1000, buffer.GetChannelStatus().TotalBufferedReadings);
    }
}
