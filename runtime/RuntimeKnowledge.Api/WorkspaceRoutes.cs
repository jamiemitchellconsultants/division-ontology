using System.Security.Claims;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Api;

internal static class WorkspaceRoutes
{
    public static void MapWorkspaceRoutes(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Identity.ReadPolicy);
        api.MapGet("/session", (ClaimsPrincipal user) => Identity.GetSession(user));
        api.MapGet("/inventory", (string? cursor, int? limit, GraphQueries graph, CancellationToken token) =>
            graph.InventoryAsync(new PageRequest(cursor, limit ?? 50), token))
            .ProducesWorkspaceErrors("invalidLimit", "invalidCursor", "staleCursor", "snapshotUnavailable");
        api.MapPost("/entities/detail", (EntityDetailRequest request, GraphQueries graph, CancellationToken token) =>
            graph.EntityDetailAsync(request.Key, token));
        api.MapPost("/relationships/detail", (RelationshipDetailRequest request, GraphQueries graph, CancellationToken token) =>
            graph.RelationshipDetailAsync(request, token));
        api.MapPost("/search", (SearchRequest request, SearchStore search, CancellationToken token) =>
            search.SearchAsync(request.Query, request.Limit, token));
        api.MapPost("/traverse", (TraversalRequest request, GraphQueries graph, CancellationToken token) =>
            graph.TraverseAsync(request, token));
        api.MapGet("/history", (Guid? contributionId, string? cursor, int? limit, ContributionStore store, CancellationToken token) =>
            store.HistoryAsync(contributionId, new PageRequest(cursor, limit ?? 50), token));

        var changes = api.MapGroup("/contributions").RequireAuthorization(Identity.ManagePolicy);
        changes.MapPost("/annotations", async (CreateAnnotation request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
        {
            var created = await store.ApplyAsync(Identity.GetActor(user), request, token);
            return Results.Created($"/api/history?contributionId={created.Id:D}", created);
        });
        changes.MapPost("/relationships", async (CreateRelationship request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
        {
            var created = await store.ApplyAsync(Identity.GetActor(user), request, token);
            return Results.Created($"/api/history?contributionId={created.Id:D}", created);
        });
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
        });
        changes.MapPost("/{id:guid}/remove", (Guid id, RemoveContributionRequest request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
            store.ApplyAsync(Identity.GetActor(user), new RemoveContribution(id, request.ExpectedVersion), token));
        changes.MapPost("/{id:guid}/codifications", (Guid id, CodificationRequest request, ClaimsPrincipal user,
            ContributionStore store, CancellationToken token) =>
            store.ApplyAsync(Identity.GetActor(user),
                new AssociateCodification(id, request.ExpectedVersion, request.PublishedRelationship), token));
    }
}
