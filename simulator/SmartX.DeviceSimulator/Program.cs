using SmartX.DeviceSimulator;

// Smart-X ESP32 fleet simulator: every reading and command acknowledgement reaches the gateway over HTTP,
// exactly as a physical device would send it.
var apiUrl = Environment.GetEnvironmentVariable("SMARTX_API_URL") ?? (args.Length > 0 ? args[0] : "http://localhost:5240");
if (!apiUrl.EndsWith('/')) apiUrl += "/";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var http = new HttpClient { BaseAddress = new Uri(apiUrl), Timeout = TimeSpan.FromSeconds(10) };
var simulator = new FleetSimulator(new GatewayApiClient(http), log: message => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {message}"));

Console.WriteLine($"Smart-X device simulator targeting {apiUrl}");
try
{
    await simulator.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Simulator stopped.");
}
