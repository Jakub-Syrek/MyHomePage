using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyHomePage.Abstractions;
using MyHomePage.Models;
using MyHomePage.Options;
using MyHomePage.Services;

namespace MyHomePage.Endpoints;

/// <summary>
/// Minimal-API endpoints for the Strava integration: OAuth login + callback
/// (interactive, protected by the existing cookie authentication so only an
/// admin can initiate consent), the public webhook handshake / event POST
/// required by Strava and a manual-import action used from the admin UI.
/// </summary>
public static class StravaEndpoints
{
    /// <summary>
    /// Registers all Strava routes on the supplied builder. Keeps wiring
    /// out of <c>Program.cs</c> so the file remains focused on lifetime
    /// concerns.
    /// </summary>
    /// <param name="endpoints">Route builder to register on.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapStravaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/strava/login", LoginAsync).RequireAuthorization();
        endpoints.MapGet("/auth/strava/callback", CallbackAsync).RequireAuthorization();
        endpoints.MapPost("/auth/strava/disconnect", DisconnectAsync).RequireAuthorization();

        endpoints.MapGet("/api/strava/webhook", VerifyWebhookAsync).AllowAnonymous();
        endpoints.MapPost("/api/strava/webhook", HandleWebhookAsync).AllowAnonymous();

        endpoints.MapPost("/api/strava/import/{activityId:long}", ImportActivityAsync)
            .RequireAuthorization();
        endpoints.MapPost("/api/strava/attach/{videoId:int}/{activityId:long}", AttachActivityAsync)
            .RequireAuthorization();

        return endpoints;
    }

    private static IResult LoginAsync(
        HttpContext context,
        [FromServices] IOptions<StravaOptions> options)
    {
        var state = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(
            StateCookieName,
            state,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(10)
            });

        var url = StravaApiClient.BuildAuthorizeUrl(options.Value, state);
        return Results.Redirect(url);
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext context,
        [FromServices] StravaTokenService tokens,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error))
            return Results.BadRequest(new { error });

        var expected = context.Request.Cookies[StateCookieName];
        context.Response.Cookies.Delete(StateCookieName);
        if (string.IsNullOrEmpty(state) || state != expected)
            return Results.BadRequest(new { error = "state_mismatch" });

        if (string.IsNullOrEmpty(code))
            return Results.BadRequest(new { error = "missing_code" });

        var result = await tokens.CompleteAuthorizationAsync(code, cancellationToken);
        return result.IsSuccess
            ? Results.Redirect("/admin/strava")
            : Results.BadRequest(new { error = result.Message });
    }

    private static async Task<IResult> DisconnectAsync(
        [FromServices] StravaTokenService tokens,
        CancellationToken cancellationToken)
    {
        var result = await tokens.DisconnectAsync(cancellationToken);
        return Results.Ok(new { ok = result.IsSuccess, message = result.Message });
    }

    private static IResult VerifyWebhookAsync(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        [FromServices] IOptions<StravaOptions> options)
    {
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal))
            return Results.BadRequest(new { error = "invalid_mode" });
        if (verifyToken != options.Value.WebhookVerifyToken)
            return Results.Unauthorized();
        return Results.Ok(new WebhookChallengeResponse(challenge ?? string.Empty));
    }

    private static async Task<IResult> HandleWebhookAsync(
        [FromBody] StravaWebhookEvent payload,
        [FromServices] IStravaSyncService sync,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("StravaWebhook");
        if (payload is null)
            return Results.BadRequest(new { error = "empty_payload" });

        logger.LogInformation(
            "Strava webhook received: {ObjectType} {AspectType} id={ObjectId}",
            payload.ObjectType, payload.AspectType, payload.ObjectId);

        var skip = payload.ObjectType != "activity"
                   || (payload.AspectType != "create" && payload.AspectType != "update");
        if (skip) return Results.Ok();

        var result = await sync.ImportActivityAsync(payload.ObjectId, cancellationToken);
        if (!result.IsSuccess)
            logger.LogWarning("Strava webhook import failed: {Message}", result.Message);
        return Results.Ok();
    }

    private static async Task<IResult> ImportActivityAsync(
        long activityId,
        [FromServices] IStravaSyncService sync,
        CancellationToken cancellationToken)
    {
        var result = await sync.ImportActivityAsync(activityId, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(new { ok = true, videoId = result.Value!.Id })
            : Results.BadRequest(new { ok = false, error = result.Message });
    }

    private static async Task<IResult> AttachActivityAsync(
        int videoId,
        long activityId,
        [FromServices] IStravaSyncService sync,
        CancellationToken cancellationToken)
    {
        var result = await sync.AttachActivityToVideoAsync(videoId, activityId, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(new { ok = true })
            : Results.BadRequest(new { ok = false, error = result.Message });
    }

    /// <summary>Cookie name used to round-trip the OAuth state parameter.</summary>
    private const string StateCookieName = "strava_oauth_state";

    /// <summary>Response shape required by Strava's webhook verification handshake.</summary>
    private sealed record WebhookChallengeResponse(
        [property: JsonPropertyName("hub.challenge")] string Challenge);
}
