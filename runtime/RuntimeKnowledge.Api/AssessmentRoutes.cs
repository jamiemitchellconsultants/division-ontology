using System.Security.Claims;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Render;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Api;

/// <summary>Preview, save, read and edit impact assessments in the workspace (B17–B22, B31–B33, B36).</summary>
internal static class AssessmentRoutes
{
    private const string Assessments = "Assessments";

    public static void MapAssessmentRoutes(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/assessments").RequireAuthorization(Identity.ReadPolicy);
        var manage = app.MapGroup("/api/assessments").RequireAuthorization(Identity.ManagePolicy);

        read.MapPost("/preview", async (AssessmentDraftRequest request, ImpactModelCache cache, HttpContext context, CancellationToken token) =>
            {
                var (frozen, markdown) = await FreezeAsync(request.SnapshotId, request.Record, request.Views, cache, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.AssessmentPreview);
                return new AssessmentPreview(request.SnapshotId, frozen["digest"]!.GetValue<string>(), frozen, markdown);
            })
            .WithSummary("Preview an assessment draft without saving it").WithTags(Assessments)
            .ProducesWorkspaceErrors("invalidAssessment", "approvalNotSupported", "snapshotChanged", "impactUnavailable", "snapshotUnavailable")
            .WithMetadata(new RequestExample(OpenApiExamples.AssessmentDraft));

        app.MapPost("/api/assessments", async (AssessmentDraftRequest request, ClaimsPrincipal user, ImpactModelCache cache,
                AssessmentStore store, HttpContext context, CancellationToken token) =>
            {
                var (frozen, markdown) = await FreezeAsync(request.SnapshotId, request.Record, request.Views, cache, token);
                var startedFrom = request.Record["startedFrom"]?["assessmentId"]?.GetValue<string>() is { } earlier
                    ? Guid.TryParse(earlier, out var parsed) ? parsed
                        : throw new WorkspaceException("invalidAssessment", "startedFrom.assessmentId must be an assessment id.")
                    : (Guid?)null;
                var saved = await store.SaveAsync(Identity.GetActor(user), request.SnapshotId,
                    request.Record["requestId"]!.GetValue<string>(), EcmaJson.Stringify(frozen), markdown, startedFrom, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.Assessment(saved.Id));
                return Results.Created($"/api/assessments/{saved.Id:D}", saved);
            })
            .RequireAuthorization(Identity.ManagePolicy)
            .WithSummary("Save an assessment").WithTags(Assessments).Produces<AssessmentSaved>(StatusCodes.Status201Created)
            .ProducesWorkspaceErrors("invalidAssessment", "approvalNotSupported", "assessmentNotFound", "snapshotChanged",
                "impactUnavailable", "snapshotUnavailable", "writesPaused")
            .WithMetadata(new RequestExample(OpenApiExamples.AssessmentDraft));

        app.MapGet("/api/assessments", async (string? cursor, int? limit, AssessmentStore store, HttpContext context, CancellationToken token) =>
            {
                var page = await store.ListAsync(cursor, limit ?? 50, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.NextPage("/api/assessments", page.NextCursor, limit: limit));
                return page;
            })
            .RequireAuthorization(Identity.ReadPolicy)
            .WithSummary("List saved assessments, newest first").WithTags(Assessments)
            .ProducesWorkspaceErrors("invalidLimit", "invalidCursor", "snapshotUnavailable");

        read.MapGet("/{id:guid}", async (Guid id, AssessmentStore store, HttpContext context, CancellationToken token) =>
            {
                var saved = await store.GetAsync(id, token) ?? throw NotFound();
                WorkspaceLinks.Append(context, WorkspaceLinks.Assessment(id));
                return saved;
            })
            .WithSummary("Open a saved assessment").WithTags(Assessments)
            .ProducesWorkspaceErrors("assessmentNotFound");

        read.MapGet("/{id:guid}/revisions", async (Guid id, string? cursor, int? limit, AssessmentStore store, HttpContext context,
                CancellationToken token) =>
            {
                var page = await store.RevisionsAsync(id, cursor, limit ?? 50, token) ?? throw NotFound();
                WorkspaceLinks.Append(context, WorkspaceLinks.NextPage($"/api/assessments/{id:D}/revisions", page.NextCursor, limit: limit));
                return page;
            })
            .WithSummary("List a saved assessment's revisions, oldest first").WithTags(Assessments)
            .ProducesWorkspaceErrors("invalidLimit", "invalidCursor", "assessmentNotFound");

        read.MapGet("/{id:guid}/revisions/{version:int}", async (Guid id, int version, AssessmentStore store, CancellationToken token) =>
                await store.RevisionAsync(id, version, token) ?? throw NotFound())
            .WithSummary("Open one revision of a saved assessment").WithTags(Assessments)
            .ProducesWorkspaceErrors("assessmentNotFound");

        read.MapGet("/{id:guid}/markdown", async (Guid id, AssessmentStore store, CancellationToken token) =>
            {
                var markdown = await store.MarkdownAsync(id, token) ?? throw NotFound();
                return Results.Bytes(markdown, "text/markdown; charset=utf-8");
            })
            .WithSummary("Download a saved assessment's Markdown").WithTags(Assessments)
            .Produces<string>(StatusCodes.Status200OK, "text/markdown")
            .ProducesWorkspaceErrors("assessmentNotFound");

        manage.MapPut("/{id:guid}", async (Guid id, AssessmentEditRequest request, ClaimsPrincipal user, ImpactModelCache cache,
                AssessmentStore store, HttpContext context, CancellationToken token) =>
            {
                // An assessment pinned to an earlier snapshot is refused before anything is frozen (B33).
                if (await store.PinnedToEarlierSnapshotAsync(id, token) ?? throw NotFound())
                    throw new WorkspaceException("snapshotMismatch",
                        "This assessment is pinned to an earlier snapshot and cannot be edited. Start a new assessment from it.");
                var (frozen, markdown) = await FreezeAsync(request.SnapshotId, request.Record, request.Views, cache, token);
                var saved = await store.ReviseAsync(Identity.GetActor(user), id, request.ExpectedVersion, request.SnapshotId,
                    EcmaJson.Stringify(frozen), markdown, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.Assessment(id));
                return saved;
            })
            .WithSummary("Edit a saved assessment").WithTags(Assessments)
            .ProducesWorkspaceErrors("invalidAssessment", "approvalNotSupported", "assessmentNotFound", "versionConflict",
                "snapshotMismatch", "snapshotChanged", "impactUnavailable", "snapshotUnavailable", "writesPaused")
            .WithMetadata(new RequestExample(OpenApiExamples.AssessmentEdit));
    }

    // B35: a new draft on the active snapshot that carries the earlier request forward, and nothing else from it.
    // Nothing is stored; saving the draft links back through startedFrom.
    internal static void MapSuccessorRoute(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/assessments/{id:guid}/successor", async (Guid id, AssessmentStore store, ImpactModelCache cache,
                HttpContext context, CancellationToken token) =>
            {
                var earlier = await store.GetAsync(id, token) ?? throw NotFound();
                var (snapshotId, model) = await cache.GetAsync(null, token);
                var description = earlier.Frozen["record"]?["description"]?.GetValue<string>() ?? "";
                var record = Impact.Engine.Assessments.New(model, earlier.RequestId, description);
                record["startedFrom"] = new JsonObject { ["assessmentId"] = id.ToString("D") };
                WorkspaceLinks.Append(context, WorkspaceLinks.AssessmentPreview);
                return new AssessmentSuccessor(snapshotId, record);
            })
            .RequireAuthorization(Identity.ManagePolicy)
            .WithSummary("Start a new assessment from a saved one").WithTags(Assessments)
            .ProducesWorkspaceErrors("assessmentNotFound", "impactUnavailable", "snapshotUnavailable");

    private static async Task<(JsonObject Frozen, string Markdown)> FreezeAsync(string snapshotId, JsonObject record,
        IReadOnlyList<JsonObject>? views, ImpactModelCache cache, CancellationToken token)
    {
        var (_, model) = await cache.GetAsync(snapshotId, token);
        if (record is null || record["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var schema) || schema != 2)
            throw new WorkspaceException("invalidAssessment", "The workspace accepts schema 2 records only");
        try
        {
            using var reuse = Paths.ReuseWithin();
            var paths = Impact.Engine.Assessments.CitablePaths(model, record);
            var frozen = Impact.Engine.Assessments.Freeze(record, model, paths, views ?? []);
            return (frozen, AssessmentMarkdown.Render(frozen));
        }
        catch (ImpactException ex) when (ex.Message == "approval is not supported")
        {
            throw new WorkspaceException("approvalNotSupported", ex.Message, ex);
        }
        catch (Exception ex) when (ex is ImpactException or InvalidOperationException or NullReferenceException or FormatException)
        {
            throw new WorkspaceException("invalidAssessment", ex.Message, ex);
        }
    }

    private static WorkspaceException NotFound() => new("assessmentNotFound", "The assessment was not found.");
}
