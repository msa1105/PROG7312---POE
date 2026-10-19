using Microsoft.AspNetCore.SignalR;

namespace SmartX.Api.Services.Realtime;

/// <summary>
/// Server-to-client push channel for the dashboard. Clients only listen; all actions go through the REST API.
/// Events: devicesUpdated, alertRaised, commandsUpdated, pipelineUpdated, recommendationsUpdated.
/// Reference: Microsoft, 2026i. Overview of ASP.NET Core SignalR. [online] Available at: <https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction> [Accessed 19 October 2026].
/// </summary>
public class GatewayHub : Hub
{
}
