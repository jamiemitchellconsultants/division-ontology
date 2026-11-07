using Microsoft.AspNetCore.Diagnostics;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Api;

internal static class ProblemResponses
{
    public static Task WriteAsync(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (error is not WorkspaceException problem)
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "The workspace could not complete the request.").ExecuteAsync(context);

        var status = problem.Code switch
        {
            "entityNotFound" or "relationshipNotFound" or "contributionNotFound" => StatusCodes.Status404NotFound,
            "versionConflict" or "staleCursor" => StatusCodes.Status409Conflict,
            "snapshotUnavailable" or "writesPaused" or "searchUnavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        var fields = new Dictionary<string, object?> { ["code"] = problem.Code };
        if (problem.Current is not null) fields["current"] = problem.Current;
        return Results.Problem(statusCode: status, title: problem.Message, extensions: fields).ExecuteAsync(context);
    }
}
