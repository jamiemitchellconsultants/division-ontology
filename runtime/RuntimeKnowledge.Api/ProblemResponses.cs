using Microsoft.AspNetCore.Diagnostics;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Api;

internal static class ProblemResponses
{
    internal sealed record Refusal(string Code, string Title);

    /// <summary>The fixed bodies of B22. A 401 body never says why the token was rejected.</summary>
    public static IReadOnlyDictionary<int, Refusal> Refusals { get; } = new Dictionary<int, Refusal>
    {
        [StatusCodes.Status400BadRequest] = new("unreadableRequest", "The request body or parameters could not be read."),
        [StatusCodes.Status401Unauthorized] = new("authenticationRequired", "Sign in to use the workspace API."),
        [StatusCodes.Status403Forbidden] = new("roleRequired", "Your account does not have the role this operation needs.")
    };

    /// <summary>
    /// Writes the refusal body for a 400, 401 or 403 that has none. Every other status, and any response that
    /// already has a body, is left alone (ADR 0018).
    /// </summary>
    public static IApplicationBuilder UseRefusalResponses(this IApplicationBuilder app) =>
        app.UseStatusCodePages(context =>
        {
            var status = context.HttpContext.Response.StatusCode;
            if (!Refusals.TryGetValue(status, out var refusal)) return Task.CompletedTask;
            return Results.Problem(statusCode: status, title: refusal.Title,
                extensions: new Dictionary<string, object?> { ["code"] = refusal.Code }).ExecuteAsync(context.HttpContext);
        });

    public static Task WriteAsync(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (error is not WorkspaceException problem)
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "The workspace could not complete the request.").ExecuteAsync(context);

        var status = StatusFor(problem.Code);
        var fields = new Dictionary<string, object?> { ["code"] = problem.Code };
        if (problem.Current is not null) fields["current"] = problem.Current;
        return Results.Problem(statusCode: status, title: problem.Message, extensions: fields).ExecuteAsync(context);
    }

    /// <summary>The single map from a workspace error code to its status. The spec derives its statuses from it.</summary>
    public static int StatusFor(string code) => code switch
    {
        "entityNotFound" or "relationshipNotFound" or "contributionNotFound" => StatusCodes.Status404NotFound,
        "versionConflict" or "staleCursor" => StatusCodes.Status409Conflict,
        "snapshotUnavailable" or "writesPaused" or "searchUnavailable" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status422UnprocessableEntity
    };
}
