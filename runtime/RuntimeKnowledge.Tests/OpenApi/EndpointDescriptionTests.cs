using RuntimeKnowledge.Api;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class EndpointDescriptionTests
{
    const string Read = "ReadKnowledgeGraph", Manage = "ManageKnowledgeGraph";

    public static TheoryData<string, string, string, string, string, string, string?, string[]> Routes => new()
    {
        // method, path, summary, tag, role, success "status schema", request schema (null if none), codes
        { "get", "/health", "Report readiness", "Health", "none", "200 HealthStatus", null, [] },
        { "get", "/api/session", "Show the caller's workspace access", "Reading", Read, "200 WorkspaceSession", null, [] },
        { "get", "/api/inventory", "List published entities, relationship types and relationships", "Reading", Read, "200 InventoryPage", null,
            ["invalidLimit", "invalidCursor", "staleCursor", "snapshotUnavailable"] },
        { "post", "/api/entities/detail", "Get one published entity with its annotations", "Reading", Read, "200 EntityDetail", "EntityDetailRequest",
            ["entityNotFound", "snapshotUnavailable"] },
        { "post", "/api/relationships/detail", "Get one published or runtime relationship", "Reading", Read, "200 RelationshipDetail", "RelationshipDetailRequest",
            ["invalidRelationship", "relationshipNotFound", "snapshotUnavailable"] },
        { "post", "/api/search", "Search published entities and annotations by meaning", "Reading", Read, "200 SearchResult", "SearchRequest",
            ["invalidSearch", "searchUnavailable", "snapshotUnavailable"] },
        { "post", "/api/traverse", "Traverse relationships from a published entity", "Reading", Read, "200 TraversalResult", "TraversalRequest",
            ["invalidTraversal", "entityNotFound", "snapshotUnavailable", "invalidCursor", "staleCursor"] },
        { "get", "/api/history", "List contribution history", "Reading", Read, "200 HistoryPage", null, ["invalidLimit", "invalidCursor"] },
        { "post", "/api/impact/search", "Search the active snapshot for impact leads", "Impact", Read, "200 ImpactSearchResult", "ImpactSearchRequest",
            ["invalidImpactRequest", "invalidCursor", "snapshotChanged", "impactUnavailable", "snapshotUnavailable"] },
        { "post", "/api/impact/paths", "Trace impact paths from supported leads", "Impact", Read, "200 ImpactPathsResult", "ImpactPathsRequest",
            ["invalidImpactRequest", "snapshotChanged", "impactUnavailable", "snapshotUnavailable"] },
        { "post", "/api/impact/views", "Project a C4 view of a mapped node", "Impact", Read, "200 C4ViewResult", "ImpactViewRequest",
            ["invalidImpactRequest", "snapshotChanged", "impactUnavailable", "snapshotUnavailable"] },
        { "post", "/api/assessments/preview", "Preview an assessment draft without saving it", "Assessments", Read, "200 AssessmentPreview", "AssessmentDraftRequest",
            ["invalidAssessment", "approvalNotSupported", "snapshotChanged", "impactUnavailable", "snapshotUnavailable"] },
        { "post", "/api/assessments", "Save an assessment", "Assessments", Manage, "201 AssessmentSaved", "AssessmentDraftRequest",
            ["invalidAssessment", "approvalNotSupported", "assessmentNotFound", "snapshotChanged", "impactUnavailable", "snapshotUnavailable", "writesPaused"] },
        { "get", "/api/assessments", "List saved assessments, newest first", "Assessments", Read, "200 AssessmentPage", null,
            ["invalidLimit", "invalidCursor", "snapshotUnavailable"] },
        { "get", "/api/assessments/{id}", "Open a saved assessment", "Assessments", Read, "200 SavedAssessment", null, ["assessmentNotFound"] },
        { "post", "/api/assessments/{id}/successor", "Start a new assessment from a saved one", "Assessments", Manage, "200 AssessmentSuccessor", null,
            ["assessmentNotFound", "impactUnavailable", "snapshotUnavailable"] },
        { "get", "/api/assessments/{id}/revisions", "List a saved assessment's revisions, oldest first", "Assessments", Read, "200 AssessmentRevisionPage", null,
            ["invalidLimit", "invalidCursor", "assessmentNotFound"] },
        { "get", "/api/assessments/{id}/revisions/{version}", "Open one revision of a saved assessment", "Assessments", Read, "200 AssessmentRevision", null, ["assessmentNotFound"] },
        { "get", "/api/assessments/{id}/markdown", "Download a saved assessment's Markdown", "Assessments", Read, "200 text/markdown", null, ["assessmentNotFound"] },
        { "put", "/api/assessments/{id}", "Edit a saved assessment", "Assessments", Manage, "200 AssessmentSaved", "AssessmentEditRequest",
            ["invalidAssessment", "approvalNotSupported", "assessmentNotFound", "versionConflict", "snapshotMismatch", "snapshotChanged", "impactUnavailable", "snapshotUnavailable", "writesPaused"] },
        { "post", "/api/contributions/annotations", "Create an annotation", "Contributions", Manage, "201 ContributionResult", "CreateAnnotation",
            ["invalidAnnotation", "endpointUnavailable", "snapshotUnavailable", "writesPaused"] },
        { "post", "/api/contributions/relationships", "Create a runtime relationship", "Contributions", Manage, "201 ContributionResult", "CreateRelationship",
            ["endpointUnavailable", "invalidMeaning", "snapshotUnavailable", "writesPaused"] },
        { "put", "/api/contributions/{id}", "Edit an annotation or runtime relationship", "Contributions", Manage, "200 ContributionResult", "EditContributionRequest",
            ["invalidCommand", "invalidAnnotation", "endpointUnavailable", "invalidMeaning", "contributionNotFound", "versionConflict", "snapshotUnavailable", "writesPaused"] },
        { "post", "/api/contributions/{id}/remove", "Remove a contribution", "Contributions", Manage, "200 ContributionResult", "RemoveContributionRequest",
            ["contributionNotFound", "versionConflict", "snapshotUnavailable", "writesPaused"] },
        { "post", "/api/contributions/{id}/codifications", "Associate a runtime relationship with its codifying published relationship", "Contributions", Manage,
            "200 ContributionResult", "CodificationRequest",
            ["invalidCommand", "invalidAssociation", "associationExists", "contributionNotFound", "versionConflict", "snapshotUnavailable", "writesPaused"] },
    };

    [Theory, MemberData(nameof(Routes))]
    public async Task OperationMatchesTheRouteTable(string method, string path, string summary, string tag, string role,
        string success, string? request, string[] codes)
    {
        await using var host = OpenApiHost.Start();
        var op = (await host.SpecAsync())["paths"]![path]![method]!;
        Assert.Equal(summary, (string)op["summary"]!);
        Assert.Equal($"[\"{tag}\"]", op["tags"]!.ToJsonString());
        Assert.Equal(role, (string)op["x-workspace-role"]!);
        var (status, schema) = (success.Split(' ')[0], success.Split(' ')[1]);
        if (schema.Contains('/'))
            Assert.Equal("string", (string)op["responses"]![status]!["content"]![schema]!["schema"]!["type"]!);   // a non-JSON media type
        else
            Assert.Equal($"#/components/schemas/{schema}", (string)op["responses"]![status]!["content"]!["application/json"]!["schema"]!["$ref"]!);
        Assert.Equal(request is null ? null : $"#/components/schemas/{request}",
            (string?)op["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]);
        var expected = codes.Select(c => ProblemResponses.StatusFor(c).ToString())
            .Append(status).Append("500")
            .Concat(path.StartsWith("/api/") ? ["401", "403"] : [])
            .Concat(op["requestBody"] is not null || op["parameters"] is not null ? ["400"] : [])
            .Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(expected, op["responses"]!.AsObject().Select(r => r.Key));
        Assert.Equal(codes.Order(StringComparer.Ordinal),
            op["responses"]!.AsObject().SelectMany(r => r.Value!["x-workspace-codes"]?.AsArray().Select(n => (string)n!) ?? []).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheTableCoversEveryOperation()
    {
        await using var host = OpenApiHost.Start();
        var operations = (await host.SpecAsync())["paths"]!.AsObject().Sum(p => p.Value!.AsObject().Count);
        Assert.Equal(Routes.Count, operations);
    }
}
