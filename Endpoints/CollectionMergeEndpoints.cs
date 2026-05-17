using Microsoft.AspNetCore.Mvc;
using MyHomePage.Abstractions;

namespace MyHomePage.Endpoints;

/// <summary>
/// Minimal-API surface for the multi-collection merge feature. Single
/// endpoint that drives the heavy lifting in
/// <see cref="ICollectionMergeService"/>. Authentication required — only
/// signed-in admins may rearrange the gallery.
/// </summary>
public static class CollectionMergeEndpoints
{
    /// <summary>Registers <c>POST /api/collections/merge</c> on the supplied builder.</summary>
    /// <param name="endpoints">Route builder.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapCollectionMergeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/collections/merge", MergeAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> MergeAsync(
        [FromBody] MergeRequest request,
        [FromServices] ICollectionMergeService service,
        CancellationToken cancellationToken)
    {
        if (request is null || request.SourceIds is null)
        {
            return Results.BadRequest(new { error = "sourceIds is required." });
        }

        var result = await service.MergeAsync(
            request.SourceIds,
            request.Title ?? string.Empty,
            request.Description ?? string.Empty,
            cancellationToken);

        return result.IsSuccess
            ? Results.Ok(new { newId = result.Value })
            : Results.BadRequest(new { error = result.Message });
    }

    /// <summary>Body of <c>POST /api/collections/merge</c>.</summary>
    /// <param name="SourceIds">Collection ids to combine (at least 2, no duplicates).</param>
    /// <param name="Title">Title for the new collection.</param>
    /// <param name="Description">Optional description for the new collection.</param>
    public sealed record MergeRequest(IReadOnlyList<int> SourceIds, string? Title, string? Description);
}
