using SmartX.Api.Services.Realtime;
using SmartX.Api.Services.Recommendations;

namespace SmartX.Api.Endpoints;

/// <summary>Predictive Action &amp; Recommendation Engine: Suggested Actions, Automated Insights and feedback.</summary>
public static class RecommendationEndpoints
{
    public static void MapRecommendationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/recommendations", (RecommendationService recommendations) => Results.Ok(recommendations.Evaluate()));

        app.MapPost("/api/recommendations/{id}/accept", async (string id, RecommendationService recommendations, GatewayBroadcaster broadcaster) =>
        {
            var accepted = recommendations.Accept(Uri.UnescapeDataString(id));
            if (accepted == null) return Results.NotFound(new { error = "Suggestion is no longer available." });

            await broadcaster.PublishRecommendationsAsync(force: true);
            return Results.Ok(accepted);
        });

        app.MapPost("/api/recommendations/{id}/dismiss", async (string id, RecommendationService recommendations, GatewayBroadcaster broadcaster) =>
        {
            if (!recommendations.Dismiss(Uri.UnescapeDataString(id))) return Results.NotFound(new { error = "Suggestion is no longer available." });

            await broadcaster.PublishRecommendationsAsync(force: true);
            return Results.Ok(new { Dismissed = id });
        });
    }
}
