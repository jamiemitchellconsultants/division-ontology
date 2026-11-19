using System.Security.Claims;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Api;

internal static class WorkspaceRoutes
{
    private const string Reading = "Reading";
    private const string Contributions = "Contributions";

    public static void MapWorkspaceRoutes(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Identity.ReadPolicy);
        api.MapGet("/session", (ClaimsPrincipal user) => Identity.GetSession(user))
            .WithSummary("Show the caller's workspace access").WithTags(Reading);
        api.MapGet("/inventory", (string? cursor, int? limit, GraphQueries graph, CancellationToken token) =>
            graph.InventoryAsync(new PageRequest(cursor, limit ?? 50), token))
            .WithSummary("List published entities, relationship types and relationships").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidLimit", "invalidCursor", "staleCursor", "snapshotUnavailable");
        api.MapPost("/entities/detail", (EntityDetailRequest request, GraphQueries graph, CancellationToken token) =>
            graph.EntityDetailAsync(request.Key, token))
            .WithSummary("Get one published entity with its annotations").WithTags(Reading)
            .ProducesWorkspaceErrors("entityNotFound", "snapshotUnavailable");
        api.MapPost("/relationships/detail", (RelationshipDetailRequest request, GraphQueries graph, CancellationToken token) =>
            graph.RelationshipDetailAsync(request, token))
            .WithSummary("Get one published or runtime relationship").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidRelationship", "relationshipNotFound", "snapshotUnavailable");
        api.MapPost("/search", (SearchRequest request, SearchStore search, CancellationToken token) =>
            search.SearchAsync(request.Query, request.Limit, token))
            .WithSummary("Search published entities and annotations by meaning").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidSearch", "searchUnavailable", "snapshotUnavailable")
            .WithMetadata(new RequestExample(OpenApiExamples.Search));
        api.MapPost("/traverse", (TraversalRequest request, GraphQueries graph, CancellationToken token) =>
            graph.TraverseAsync(request, token))
            .WithSummary("Traverse relationships from a published entity").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidTraversal", "entityNotFound", "snapshotUnavailable")
            .WithMetadata(new RequestExample(OpenApiExamples.Traversal));
        api.MapGet("/history", (Guid? contributionId, string? cursor, int? limit, ContributionStore store, CancellationToken token) =>
            store.HistoryAsync(contributionId, new PageRequest(cursor, limit ?? 50), token))
            .WithSummary("List contribution history").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidLimit", "invalidCursor");

        var changes = api.MapGroup("/contributions").RequireAuthorization(Identity.ManagePolicy);
        changes.MapPost("/annotations", async (CreateAnnotation request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
        {
            var created = await store.ApplyAsync(Identity.GetActor(user), request, token);
            return Results.Created($"/api/history?contributionId={created.Id:D}", created);
        }).WithSummary("Create an annotation").WithTags(Contributions).Produces<ContributionResult>(StatusCodes.Status201Created)
            .ProducesWorkspaceErrors("invalidAnnotation", "endpointUnavailable", "snapshotUnavailable", "writesPaused")
            .WithMetadata(new RequestExample(OpenApiExamples.CreateAnnotation));
        changes.MapPost("/relationships", async (CreateRelationship request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
        {
            var created = await store.ApplyAsync(Identity.GetActor(user), request, token);
            return Results.Created($"/api/history?contributionId={created.Id:D}", created);
        }).WithSummary("Create a runtime relationship").WithTags(Contributions).Produces<ContributionResult>(StatusCodes.Status201Created)
            .ProducesWorkspaceErrors("endpointUnavailable", "invalidMeaning", "snapshotUnavailable", "writesPaused")
            .WithMetadata(new RequestExample(OpenApiExamples.CreateRelationship));
        changes.MapPut("/{id:guid}", (Guid id, EditContributionRequest request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
        {
            ContributionCommand command = request switch
            {
                { Category: { } category, Text: { } text, From: null, To: null, Meaning: null } =>
                    new EditAnnotation(id, request.ExpectedVersion, category, text),
                { Category: null, Text: null, From: { } from, To: { } to, Meaning: { } meaning } =>
                    new EditRelationship(id, request.ExpectedVersion, from, to, meaning),
                _ => throw new WorkspaceException("invalidCommand", "Supply annotation or relationship changes.")
            };
            return store.ApplyAsync(Identity.GetActor(user), command, token);
        }).WithSummary("Edit an annotation or runtime relationship").WithTags(Contributions)
            .ProducesWorkspaceErrors("invalidCommand", "invalidAnnotation", "endpointUnavailable", "invalidMeaning",
                "contributionNotFound", "versionConflict", "snapshotUnavailable", "writesPaused");
        changes.MapPost("/{id:guid}/remove", (Guid id, RemoveContributionRequest request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
            store.ApplyAsync(Identity.GetActor(user), new RemoveContribution(id, request.ExpectedVersion), token))
            .WithSummary("Remove a contribution").WithTags(Contributions)
            .ProducesWorkspaceErrors("contributionNotFound", "versionConflict", "snapshotUnavailable", "writesPaused");
        changes.MapPost("/{id:guid}/codifications", (Guid id, CodificationRequest request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
            store.ApplyAsync(Identity.GetActor(user),
                new AssociateCodification(id, request.ExpectedVersion, request.PublishedRelationship), token))
            .WithSummary("Associate a runtime relationship with its codifying published relationship").WithTags(Contributions)
            .ProducesWorkspaceErrors("invalidCommand", "invalidAssociation", "associationExists", "contributionNotFound",
                "versionConflict", "snapshotUnavailable", "writesPaused");
    }
}
