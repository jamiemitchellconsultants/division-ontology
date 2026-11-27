using System.Text.Json.Nodes;
using RuntimeKnowledge.Api;
using RuntimeKnowledge.Contracts;
using Xunit;

namespace RuntimeKnowledge.Tests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class AgentDecoratorsTests
{
    // method, path, operationId, tool name, side effects, idempotent, retryable, input, output
    public static TheoryData<string, string, string, string, string, bool, bool, string, string> Tools => new()
    {
        { "get", "/health", "getHealth", "workspace_get_health", "none", true, true, "none", "HealthStatus" },
        { "get", "/api/session", "getSession", "workspace_get_session", "none", true, true, "none", "WorkspaceSession" },
        { "get", "/api/inventory", "listInventory", "workspace_list_inventory", "none", true, true, "parameters", "InventoryPage" },
        { "post", "/api/entities/detail", "getEntityDetail", "workspace_get_entity_detail", "none", true, true, "EntityDetailRequest", "EntityDetail" },
        { "post", "/api/relationships/detail", "getRelationshipDetail", "workspace_get_relationship_detail", "none", true, true, "RelationshipDetailRequest", "RelationshipDetail" },
        { "post", "/api/search", "searchWorkspace", "workspace_search", "none", true, true, "SearchRequest", "SearchResult" },
        { "post", "/api/traverse", "traverseGraph", "workspace_traverse_graph", "none", true, true, "TraversalRequest", "TraversalResult" },
        { "get", "/api/history", "listHistory", "workspace_list_history", "none", true, true, "parameters", "HistoryPage" },
        { "post", "/api/impact/search", "searchImpact", "workspace_search_impact", "none", true, true, "ImpactSearchRequest", "ImpactSearchResult" },
        { "post", "/api/impact/paths", "traceImpact", "workspace_trace_impact", "none", true, true, "ImpactPathsRequest", "ImpactPathsResult" },
        { "post", "/api/impact/views", "viewC4", "workspace_view_c4", "none", true, true, "ImpactViewRequest", "C4ViewResult" },
        { "post", "/api/assessments/preview", "previewAssessment", "workspace_preview_assessment", "none", true, true, "AssessmentDraftRequest", "AssessmentPreview" },
        { "post", "/api/assessments", "saveAssessment", "workspace_save_assessment", "creates-assessment", false, false, "AssessmentDraftRequest", "AssessmentSaved" },
        { "get", "/api/assessments/{id}", "getAssessment", "workspace_get_assessment", "none", true, true, "parameters", "SavedAssessment" },
        { "get", "/api/assessments/{id}/markdown", "getAssessmentMarkdown", "workspace_get_assessment_markdown", "none", true, true, "parameters", "none" },
        { "put", "/api/assessments/{id}", "editAssessment", "workspace_edit_assessment", "updates-assessment", false, false, "AssessmentEditRequest", "AssessmentSaved" },
        { "post", "/api/contributions/annotations", "createAnnotation", "workspace_create_annotation", "creates-contribution", false, false, "CreateAnnotation", "ContributionResult" },
        { "post", "/api/contributions/relationships", "createRelationship", "workspace_create_relationship", "creates-contribution", false, false, "CreateRelationship", "ContributionResult" },
        { "put", "/api/contributions/{id}", "editContribution", "workspace_edit_contribution", "updates-contribution", false, false, "EditContributionRequest", "ContributionResult" },
        { "post", "/api/contributions/{id}/remove", "removeContribution", "workspace_remove_contribution", "removes-contribution", false, false, "RemoveContributionRequest", "ContributionResult" },
        { "post", "/api/contributions/{id}/codifications", "associateCodification", "workspace_associate_codification", "updates-contribution", false, false, "CodificationRequest", "ContributionResult" },
    };

    static async Task<JsonNode> Spec()
    {
        await using var host = OpenApiHost.Start();
        return await host.SpecAsync();
    }

    static IEnumerable<(string Path, string Method, JsonNode Op)> Operations(JsonNode spec) =>
        spec["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject().Select(m => (p.Key, m.Key, m.Value!)));

    [Theory, MemberData(nameof(Tools))]
    public async Task EveryOperationIsAnAgentTool(string method, string path, string operationId, string tool, string sideEffects,
        bool idempotent, bool retryable, string input, string output)
    {
        var op = (await Spec())["paths"]![path]![method]!;
        Assert.Equal(operationId, (string)op["operationId"]!);
        Assert.False(string.IsNullOrWhiteSpace((string?)op["description"]));
        Assert.Equal(tool, (string)op["x-agent-tool-name"]!);
        Assert.Equal(sideEffects, (string)op["x-agent-side-effects"]!);
        Assert.Equal(idempotent, (bool)op["x-agent-idempotent"]!);
        Assert.Equal(retryable, (bool)op["x-agent-retryable"]!);
        Assert.Equal(input, (string)op["x-agent-input"]!);
        Assert.Equal(output, (string)op["x-agent-output"]!);
    }

    [Fact]
    public async Task ToolNamesAndOperationIdsAreUnique()
    {
        var operations = Operations(await Spec()).Select(o => o.Op).ToArray();
        Assert.Equal(Tools.Count, operations.Length);
        Assert.Equal(operations.Length, operations.Select(op => (string)op["operationId"]!).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(operations.Length, operations.Select(op => (string)op["x-agent-tool-name"]!).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DocumentCarriesAgentDiscoveryAndTagDescriptions()
    {
        var spec = await Spec();
        var discovery = spec["x-agent-discovery"]!;
        Assert.Equal("/api/openapi/v1.json", (string)discovery["spec"]!);
        Assert.Equal("/api/docs", (string)discovery["docs"]!);
        Assert.Equal("/health", (string)discovery["health"]!["path"]!);
        var tags = spec["tags"]!.AsArray().ToDictionary(t => (string)t!["name"]!, t => (string)t!["description"]!);
        Assert.Equal(["Assessments", "Contributions", "Health", "Impact", "Reading"], tags.Keys.Order(StringComparer.Ordinal));
        Assert.All(tags.Values, description => Assert.False(string.IsNullOrWhiteSpace(description)));
    }

    [Fact]
    public async Task EverySuccessLinkTargetsAKnownOperation()
    {
        var spec = await Spec();
        var operationIds = Operations(spec).Select(o => (string)o.Op["operationId"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var (path, method, op) in Operations(spec))
        {
            var success = op["responses"]!.AsObject().First(r => r.Key.StartsWith('2')).Value!;
            var links = success["links"]?.AsObject();
            Assert.True(links is { Count: > 0 }, $"{method} {path} has no success links");
            foreach (var (name, link) in links!)
                Assert.True(operationIds.Contains((string)link!["operationId"]!),
                    $"{method} {path} link {name} targets unknown {(string?)link!["operationId"]}");
        }
    }

    [Fact]
    public async Task CreatedContributionsLinkToTheirLifecycle()
    {
        var spec = await Spec();
        foreach (var path in new[] { "/api/contributions/annotations", "/api/contributions/relationships" })
        {
            var links = spec["paths"]![path]!["post"]!["responses"]!["201"]!["links"]!.AsObject();
            Assert.Equal("listHistory", (string)links["history"]!["operationId"]!);
            Assert.Equal("$response.body#/id", (string)links["history"]!["parameters"]!["contributionId"]!);
            Assert.Equal("editContribution", (string)links["editContribution"]!["operationId"]!);
            Assert.Equal("removeContribution", (string)links["removeContribution"]!["operationId"]!);
        }
        var inventory = spec["paths"]!["/api/inventory"]!["get"]!["responses"]!["200"]!["links"]!;
        Assert.Equal("$response.body#/nextCursor", (string)inventory["next"]!["parameters"]!["cursor"]!);
    }

    private const string RelBase = "https://github.com/jamiemitchellconsultants/division-ontology#link-relations/";

    [Fact]
    public async Task TraverseDocumentsCursorPaging()
    {
        var spec = await Spec();
        var request = spec["components"]!["schemas"]!["TraversalRequest"]!;
        Assert.Contains("cursor", request["properties"]!.AsObject().Select(p => p.Key));
        var result = spec["components"]!["schemas"]!["TraversalResult"]!;
        Assert.Contains("nextCursor", result["properties"]!.AsObject().Select(p => p.Key));
        var op = spec["paths"]!["/api/traverse"]!["post"]!;
        Assert.Contains("invalidCursor", op["responses"]!["422"]!["x-workspace-codes"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(["staleCursor"], op["responses"]!["409"]!["x-workspace-codes"]!.AsArray().Select(n => (string)n!));
        var examples = op["requestBody"]!["content"]!["application/json"]!["examples"]!.AsObject();
        Assert.Equal(["firstPage", "nextPage"], examples.Select(e => e.Key).Order(StringComparer.Ordinal));
        Assert.NotNull(examples["nextPage"]!["value"]!["cursor"]);
        Assert.Null(examples["firstPage"]!["value"]!["cursor"]);
        var links = op["responses"]!["200"]!["links"]!.AsObject();
        Assert.Equal("traverseGraph", (string)links["next"]!["operationId"]!);
        Assert.Contains("traverseGraph", (string)spec["x-agent-discovery"]!["linkRelations"]!["next"]!);
        Assert.Contains("entityDetail", links.Select(l => l.Key));
        Assert.Contains("relationshipDetail", links.Select(l => l.Key));
    }

    [Fact]
    public async Task SessionAndHealthCarryLinkHeaders()
    {
        await using var host = OpenApiHost.Start();
        var reader = host.ClientWithToken(["ReadKnowledgeGraph"]);
        var session = await reader.GetAsync("/api/session");
        var sessionLinks = string.Join(",", session.Headers.GetValues("Link"));
        Assert.Contains($"</api/inventory>; rel=\"{RelBase}inventory\"", sessionLinks);
        Assert.Contains($"</api/history>; rel=\"{RelBase}history\"", sessionLinks);
    }

    [Fact]
    public void LinkHeaderValuesAreWellFormed()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var links = WorkspaceLinks.Contribution(id, codifiable: true);
        Assert.Contains($"</api/history?contributionId={id:D}>; rel=\"{RelBase}history\"", links);
        Assert.Contains($"</api/contributions/{id:D}/codifications>; rel=\"{RelBase}codifications\"", links);
        Assert.Equal([], WorkspaceLinks.NextPage("/api/inventory", null));
        Assert.Equal(["</api/inventory?cursor=a%20b&limit=1>; rel=\"next\""],
            WorkspaceLinks.NextPage("/api/inventory", "a b", limit: 1));
        Assert.Equal([$"</api/history?cursor=c&contributionId={id:D}&limit=5>; rel=\"next\""],
            WorkspaceLinks.NextPage("/api/history", "c", limit: 5, contributionId: id));
        Assert.Contains("</api/openapi/v1.json>; rel=\"service-desc\"", WorkspaceLinks.Health);
    }

    [Fact]
    public void ReadOperationsLinkToTheirSpecFollowOns()
    {
        Assert.Equal([$"</api/search>; rel=\"{RelBase}search\""], WorkspaceLinks.Inventory);
        Assert.Contains($"</api/traverse>; rel=\"{RelBase}traverse\"", WorkspaceLinks.EntityDetail);
        Assert.Contains($"</api/contributions/annotations>; rel=\"{RelBase}createAnnotation\"", WorkspaceLinks.EntityDetail);
        Assert.Equal([$"</api/entities/detail>; rel=\"{RelBase}entityDetail\""], WorkspaceLinks.RelationshipDetail);
        Assert.Equal([$"</api/entities/detail>; rel=\"{RelBase}entityDetail\""], WorkspaceLinks.Search);
        Assert.Contains($"</api/relationships/detail>; rel=\"{RelBase}relationshipDetail\"", WorkspaceLinks.Traverse);
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal([$"</api/history?contributionId={id:D}>; rel=\"{RelBase}history\""], WorkspaceLinks.ContributionHistory(id));
    }

    [Fact]
    public async Task PaginationLinksPreserveTheQueryAndHistoryHasNoEditAffordance()
    {
        var spec = await Spec();
        var inventory = spec["paths"]!["/api/inventory"]!["get"]!["responses"]!["200"]!["links"]!;
        Assert.Equal("$request.query.limit", (string)inventory["next"]!["parameters"]!["limit"]!);
        var history = spec["paths"]!["/api/history"]!["get"]!;
        var historyLinks = history["responses"]!["200"]!["links"]!.AsObject();
        Assert.Equal(["next"], historyLinks.Select(l => l.Key));
        Assert.Equal("$request.query.contributionId", (string)historyLinks["next"]!["parameters"]!["contributionId"]!);
        Assert.Equal("$request.query.limit", (string)historyLinks["next"]!["parameters"]!["limit"]!);
        Assert.Contains("oldest first", (string)history["description"]!);
        var session = spec["paths"]!["/api/session"]!["get"]!["responses"]!["200"]!["links"]!;
        Assert.Contains("oldest first", (string)session["history"]!["description"]!);
    }

    [Fact]
    public async Task RemoveGuidanceDistinguishesConflictFromNotFound()
    {
        var op = (await Spec())["paths"]!["/api/contributions/{id}/remove"]!["post"]!;
        var description = (string)op["description"]!;
        Assert.Contains("409", description);
        Assert.Contains("versionConflict", description);
        Assert.Contains("current.removed", description);
        Assert.Contains("still", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("404", description);
        Assert.Contains("contributionNotFound", description);
    }

    [Fact]
    public async Task EditAndCodificationGuidanceReconcilesWithTheCurrentRecord()
    {
        var spec = await Spec();
        foreach (var (path, method) in new[] { ("/api/contributions/{id}", "put"), ("/api/contributions/{id}/codifications", "post") })
        {
            var description = (string)spec["paths"]![path]![method]!["description"]!;
            Assert.Contains("reconcile", description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("current record", description);
            Assert.Contains("409", description);
        }
    }

    [Fact]
    public async Task HealthIsInternalInTheAgentMetadata()
    {
        var spec = await Spec();
        var health = spec["paths"]!["/health"]!["get"]!;
        Assert.Contains("internal", (string)health["description"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/*", (string)health["description"]!);
        var discovery = spec["x-agent-discovery"]!;
        Assert.Equal("/health", (string)discovery["health"]!["path"]!);
        Assert.Contains("internal", (string)discovery["health"]!["scope"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/api/openapi/v1.json", (string)discovery["spec"]!);
        Assert.Equal("/api/docs", (string)discovery["docs"]!);
    }

    [Fact]
    public async Task RuntimeRelationsAreRfc8288CompliantAndDocumentedInTheSpec()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var headers = WorkspaceLinks.Health.Concat(WorkspaceLinks.Session).Concat(WorkspaceLinks.Inventory)
            .Concat(WorkspaceLinks.EntityDetail).Concat(WorkspaceLinks.RelationshipDetail).Concat(WorkspaceLinks.Search)
            .Concat(WorkspaceLinks.Traverse).Concat(WorkspaceLinks.NextPage("/api/history", "c", 5, id))
            .Concat(WorkspaceLinks.Contribution(id, codifiable: true)).Concat(WorkspaceLinks.ContributionHistory(id));
        var rels = headers.Select(h => h.Split("rel=\"")[1].TrimEnd('"')).Distinct().ToArray();
        var registered = new HashSet<string> { "next", "service-desc", "service-doc" };
        Assert.All(rels, rel => Assert.True(registered.Contains(rel) ||
            rel.StartsWith("https://github.com/jamiemitchellconsultants/division-ontology#link-relations/", StringComparison.Ordinal), rel));
        Assert.Equal("https://github.com/jamiemitchellconsultants/division-ontology#link-relations/", WorkspaceLinks.RelationBase);
        var mapping = (await Spec())["x-agent-discovery"]!["linkRelations"]!.AsObject();
        Assert.All(rels, rel => Assert.True(mapping.ContainsKey(rel), $"undocumented rel {rel}"));
    }

    [Fact]
    public async Task EditAndCodificationSpecLinksMatchTheRuntimeActionSets()
    {
        var spec = await Spec();
        var expected = new[] { "associateCodification", "editContribution", "history", "removeContribution" };
        foreach (var (path, method, status) in new[] { ("/api/contributions/{id}", "put", "200"), ("/api/contributions/{id}/codifications", "post", "200") })
            Assert.Equal(expected, spec["paths"]![path]![method]!["responses"]![status]!["links"]!.AsObject()
                .Select(l => l.Key).Order(StringComparer.Ordinal));
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targets = WorkspaceLinks.Contribution(id, codifiable: true)
            .Select(h => h.Split('>')[0].TrimStart('<')).Order(StringComparer.Ordinal);
        Assert.Equal([$"/api/contributions/{id:D}", $"/api/contributions/{id:D}/codifications",
            $"/api/contributions/{id:D}/remove", $"/api/history?contributionId={id:D}"], targets);
        Assert.DoesNotContain(WorkspaceLinks.Contribution(id, codifiable: false),
            h => h.Contains("codifications", StringComparison.Ordinal));
    }
}
