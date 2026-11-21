using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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
        api.MapGet("/session", (ClaimsPrincipal user, HttpContext context) =>
            {
                WorkspaceLinks.Append(context, WorkspaceLinks.Session);
                return Identity.GetSession(user);
            })
            .WithSummary("Show the caller's workspace access").WithTags(Reading);
        api.MapGet("/inventory", async (string? cursor, int? limit, GraphQueries graph, HttpContext context, CancellationToken token) =>
            {
                var page = await graph.InventoryAsync(new PageRequest(cursor, limit ?? 50), token);
                WorkspaceLinks.Append(context, WorkspaceLinks.NextPage("/api/inventory", page.NextCursor, limit: limit));
                WorkspaceLinks.Append(context, WorkspaceLinks.Inventory);
                return page;
            })
            .WithSummary("List published entities, relationship types and relationships").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidLimit", "invalidCursor", "staleCursor", "snapshotUnavailable");
        api.MapPost("/entities/detail", async (EntityDetailRequest request, GraphQueries graph, HttpContext context, CancellationToken token) =>
            {
                var detail = await graph.EntityDetailAsync(request.Key, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.EntityDetail);
                return detail;
            })
            .WithSummary("Get one published entity with its annotations").WithTags(Reading)
            .ProducesWorkspaceErrors("entityNotFound", "snapshotUnavailable");
        api.MapPost("/relationships/detail", async (RelationshipDetailRequest request, GraphQueries graph, HttpContext context, CancellationToken token) =>
            {
                var detail = await graph.RelationshipDetailAsync(request, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.RelationshipDetail);
                return detail;
            })
            .WithSummary("Get one published or runtime relationship").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidRelationship", "relationshipNotFound", "snapshotUnavailable");
        api.MapPost("/search", async (SearchRequest request, SearchStore search, HttpContext context, CancellationToken token) =>
            {
                var result = await search.SearchAsync(request.Query, request.Limit, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.Search);
                return result;
            })
            .WithSummary("Search published entities and annotations by meaning").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidSearch", "searchUnavailable", "snapshotUnavailable")
            .WithMetadata(new RequestExample(OpenApiExamples.Search));
        api.MapPost("/traverse", async (TraversalRequest request, GraphQueries graph, HttpContext context, CancellationToken token) =>
            {
                var result = await graph.TraverseAsync(request, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.Traverse);
                return result;
            })
            .WithSummary("Traverse relationships from a published entity").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidTraversal", "entityNotFound", "snapshotUnavailable")
            .WithMetadata(new RequestExample(OpenApiExamples.Traversal));
        api.MapGet("/history", async (Guid? contributionId, string? cursor, int? limit, ContributionStore store, HttpContext context, CancellationToken token) =>
            {
                var page = await store.HistoryAsync(contributionId, new PageRequest(cursor, limit ?? 50), token);
                WorkspaceLinks.Append(context, WorkspaceLinks.NextPage("/api/history", page.NextCursor, limit: limit, contributionId: contributionId));
                return page;
            })
            .WithSummary("List contribution history").WithTags(Reading)
            .ProducesWorkspaceErrors("invalidLimit", "invalidCursor");

        var changes = api.MapGroup("/contributions").RequireAuthorization(Identity.ManagePolicy);
        changes.MapPost("/annotations", async (CreateAnnotation request, ClaimsPrincipal user,
            ContributionStore store, HttpContext context, CancellationToken token) =>
        {
            var created = await store.ApplyAsync(Identity.GetActor(user), request, token);
            WorkspaceLinks.Append(context, WorkspaceLinks.Contribution(created.Id, codifiable: false));
            return Results.Created($"/api/history?contributionId={created.Id:D}", created);
        }).WithSummary("Create an annotation").WithTags(Contributions).Produces<ContributionResult>(StatusCodes.Status201Created)
            .ProducesWorkspaceErrors("invalidAnnotation", "endpointUnavailable", "snapshotUnavailable", "writesPaused")
            .WithMetadata(new RequestExample(OpenApiExamples.CreateAnnotation));
        changes.MapPost("/relationships", async (CreateRelationship request, ClaimsPrincipal user,
            ContributionStore store, HttpContext context, CancellationToken token) =>
        {
            var created = await store.ApplyAsync(Identity.GetActor(user), request, token);
            WorkspaceLinks.Append(context, WorkspaceLinks.Contribution(created.Id, codifiable: true));
            return Results.Created($"/api/history?contributionId={created.Id:D}", created);
        }).WithSummary("Create a runtime relationship").WithTags(Contributions).Produces<ContributionResult>(StatusCodes.Status201Created)
            .ProducesWorkspaceErrors("endpointUnavailable", "invalidMeaning", "snapshotUnavailable", "writesPaused")
            .WithMetadata(new RequestExample(OpenApiExamples.CreateRelationship));
        changes.MapPut("/{id:guid}", async (Guid id, EditContributionRequest request, ClaimsPrincipal user,
            ContributionStore store, HttpContext context, CancellationToken token) =>
        {
            ContributionCommand command = request switch
            {
                { Category: { } category, Text: { } text, From: null, To: null, Meaning: null } =>
                    new EditAnnotation(id, request.ExpectedVersion, category, text),
                { Category: null, Text: null, From: { } from, To: { } to, Meaning: { } meaning } =>
                    new EditRelationship(id, request.ExpectedVersion, from, to, meaning),
                _ => throw new WorkspaceException("invalidCommand", "Supply annotation or relationship changes.")
            };
            var result = await store.ApplyAsync(Identity.GetActor(user), command, token);
            WorkspaceLinks.Append(context, WorkspaceLinks.Contribution(result.Id, codifiable: command is EditRelationship));
            return result;
        }).WithSummary("Edit an annotation or runtime relationship").WithTags(Contributions)
            .ProducesWorkspaceErrors("invalidCommand", "invalidAnnotation", "endpointUnavailable", "invalidMeaning",
                "contributionNotFound", "versionConflict", "snapshotUnavailable", "writesPaused");
        changes.MapPost("/{id:guid}/remove", async (Guid id, RemoveContributionRequest request, ClaimsPrincipal user,
            ContributionStore store, HttpContext context, CancellationToken token) =>
            {
                var result = await store.ApplyAsync(Identity.GetActor(user), new RemoveContribution(id, request.ExpectedVersion), token);
                WorkspaceLinks.Append(context, WorkspaceLinks.ContributionHistory(result.Id));
                return result;
            })
            .WithSummary("Remove a contribution").WithTags(Contributions)
            .ProducesWorkspaceErrors("contributionNotFound", "versionConflict", "snapshotUnavailable", "writesPaused");
        changes.MapPost("/{id:guid}/codifications", async (Guid id, CodificationRequest request, ClaimsPrincipal user,
            ContributionStore store, HttpContext context, CancellationToken token) =>
            {
                var result = await store.ApplyAsync(Identity.GetActor(user),
                    new AssociateCodification(id, request.ExpectedVersion, request.PublishedRelationship), token);
                WorkspaceLinks.Append(context, WorkspaceLinks.Contribution(result.Id, codifiable: true));
                return result;
            })
            .WithSummary("Associate a runtime relationship with its codifying published relationship").WithTags(Contributions)
            .ProducesWorkspaceErrors("invalidCommand", "invalidAssociation", "associationExists", "contributionNotFound",
                "versionConflict", "snapshotUnavailable", "writesPaused");
    }
}
