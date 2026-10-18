using SmartX.Api.Models;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

internal static class EndpointHelpers
{
    /// <summary>Resolves a route/body MAC address to a registered device (case-insensitive, validated format).</summary>
    public static SensorDevice? FindDevice(DeviceRegistryService registry, string? macAddress) =>
        DeviceRegistryService.TryNormalizeMac(macAddress, out var mac) ? registry.GetDevice(mac) : null;

    public static IResult DeviceNotFound(string? macAddress) =>
        Results.NotFound(new { error = $"Device '{macAddress}' is not registered on this gateway." });

    public static IResult BadRequest(string error) => Results.BadRequest(new { error });

    /// <summary>Removes the " (Parameter 'x')" suffix .NET appends to ArgumentException messages.</summary>
    public static string CleanMessage(ArgumentException ex) => ex.Message.Split(" (Parameter")[0];
}
