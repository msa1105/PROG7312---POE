using Microsoft.AspNetCore.SignalR;

namespace SmartX.Api.Services.Realtime;

/// <summary>
/// Server-to-client push channel for the dashboard. Clients only listen; all actions go through the REST API.
/// Events: devicesUpdated, alertRaised, commandsUpdated, pipelineUpdated, recommendationsUpdated.
/// Reference: Overview of ASP.NET Core SignalR (Microsoft Learn) - https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction
/// </summary>
public class GatewayHub : Hub
{
}
