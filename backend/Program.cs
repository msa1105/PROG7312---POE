using SmartX.Api.Endpoints;
using SmartX.Api.Services;
using SmartX.Api.Services.Ingestion;
using SmartX.Api.Services.Realtime;
using SmartX.Api.Services.Recommendations;

var builder = WebApplication.CreateBuilder(args);

var uploadsRoot = builder.Configuration["Storage:UploadsPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "Uploads");
var keysDirectory = builder.Configuration["Storage:KeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "keys");

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Malformed request bodies become 400 responses (instead of exceptions surfacing as 500s in Development)
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddSignalR();

// Part 1: registry, analytics, buffers, triage, storage
builder.Services.AddSingleton<DeviceRegistryService>();
builder.Services.AddSingleton<AnomalyDetectionService>();
builder.Services.AddSingleton<JaggedTelemetryBuffer>();
builder.Services.AddSingleton<IncidentTriageService>();
builder.Services.AddSingleton<SimulationControlService>();
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<FileEncryptionService>>();
    var masterKey = FileEncryptionService.LoadOrCreateMasterKey(
        builder.Configuration[FileEncryptionService.MasterKeyConfigKey], keysDirectory, message => logger.LogWarning("{Message}", message));
    return new FileEncryptionService(masterKey);
});
builder.Services.AddSingleton(sp => new AttachmentService(
    uploadsRoot, sp.GetRequiredService<FileEncryptionService>(), sp.GetRequiredService<DeviceRegistryService>()));
builder.Services.AddTransient<DataSeeder>();

// Part 2: real-time processing, command stream, alert sets and recommendations
builder.Services.AddSingleton<TelemetryIngestionQueue>();
builder.Services.AddSingleton<AlertStateTracker>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddSingleton<ActionPatternModel>();
builder.Services.AddSingleton<RecommendationService>();
builder.Services.AddSingleton<DeviceViewFactory>();
builder.Services.AddSingleton<GatewayBroadcaster>();
builder.Services.AddSingleton<IDeviceChangeNotifier>(sp => sp.GetRequiredService<GatewayBroadcaster>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayBroadcaster>());
builder.Services.AddHostedService<TelemetryProcessingWorker>();
builder.Services.AddHostedService<LeaseMonitorWorker>();

// Configure CORS for frontend (credentials are required by the SignalR negotiate handshake)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();

    var purged = scope.ServiceProvider.GetRequiredService<AttachmentService>().PurgeOrphanedBlobs();
    if (purged > 0) app.Logger.LogInformation("Removed {Count} orphaned attachment blob(s) from a previous run.", purged);

    scope.ServiceProvider.GetRequiredService<RecommendationService>().SeedTrainingHistory();
}

// Unhandled exceptions become RFC 7807 problem responses instead of crashing the request pipeline
// Reference: Microsoft, 2026e. Handle errors in ASP.NET Core APIs. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api> [Accessed 19 October 2026].
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

app.MapGatewayEndpoints();
app.MapTelemetryEndpoints();
app.MapDeviceEndpoints();
app.MapCommandEndpoints();
app.MapRecommendationEndpoints();
app.MapSimulationEndpoints();
app.MapHub<GatewayHub>("/hubs/gateway");

app.Run();

// Exposes the entry point to WebApplicationFactory-based integration tests
public partial class Program { }
