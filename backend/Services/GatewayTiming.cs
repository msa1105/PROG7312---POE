namespace SmartX.Api.Services;

/// <summary>
/// Heartbeat lease configuration shared by the simulator, registry and API.
/// A node is considered offline when no telemetry arrives within two expected transmission windows.
/// </summary>
public static class GatewayTiming
{
    public static readonly TimeSpan TransmissionInterval = TimeSpan.FromMilliseconds(1500);
    public const int LeaseWindows = 2;
    public static readonly TimeSpan LeaseDuration = TransmissionInterval * LeaseWindows;

    // Sliding window used for packet-delivery success scoring
    public static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(60);
}
