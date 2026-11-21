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
        Assert.Equal("/health", (string)discovery["health"]!);
        var tags = spec["tags"]!.AsArray().ToDictionary(t => (string)t!["name"]!, t => (string)t!["description"]!);
        Assert.Equal(["Contributions", "Health", "Reading"], tags.Keys.Order(StringComparer.Ordinal));
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

    [Fact]
    public async Task SessionAndHealthCarryLinkHeaders()
    {
        await using var host = OpenApiHost.Start();
        var reader = host.ClientWithToken(["ReadKnowledgeGraph"]);
        var session = await reader.GetAsync("/api/session");
        var sessionLinks = string.Join(",", session.Headers.GetValues("Link"));
        Assert.Contains("</api/inventory>; rel=\"inventory\"", sessionLinks);
        Assert.Contains("</api/history>; rel=\"history\"", sessionLinks);
    }

    [Fact]
    public void LinkHeaderValuesAreWellFormed()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var links = WorkspaceLinks.Contribution(id, codifiable: true);
        Assert.Contains($"</api/history?contributionId={id:D}>; rel=\"history\"", links);
        Assert.Contains($"</api/contributions/{id:D}/codifications>; rel=\"codifications\"", links);
        Assert.Equal([], WorkspaceLinks.NextPage("/api/inventory", null));
        Assert.Equal(["</api/inventory?cursor=a%20b>; rel=\"next\""], WorkspaceLinks.NextPage("/api/inventory", "a b"));
        Assert.Contains("</api/openapi/v1.json>; rel=\"service-desc\"", WorkspaceLinks.Health);
    }

    [Fact]
    public void ReadOperationsLinkToTheirSpecFollowOns()
    {
        Assert.Equal(["</api/search>; rel=\"search\""], WorkspaceLinks.Inventory);
        Assert.Contains("</api/traverse>; rel=\"traverse\"", WorkspaceLinks.EntityDetail);
        Assert.Contains("</api/contributions/annotations>; rel=\"createAnnotation\"", WorkspaceLinks.EntityDetail);
        Assert.Equal(["</api/entities/detail>; rel=\"entityDetail\""], WorkspaceLinks.RelationshipDetail);
        Assert.Equal(["</api/entities/detail>; rel=\"entityDetail\""], WorkspaceLinks.Search);
        Assert.Contains("</api/relationships/detail>; rel=\"relationshipDetail\"", WorkspaceLinks.Traverse);
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal([$"</api/contributions/{id:D}>; rel=\"editContribution\""], WorkspaceLinks.History(
            new HistoryPage([new HistoryEntry(1, "created", new Actor("i", "s"), DateTimeOffset.UnixEpoch, id, 1, null,
                null!, "snapshot")], null)));
        Assert.Equal([], WorkspaceLinks.History(new HistoryPage([], null)));
        Assert.Equal([$"</api/history?contributionId={id:D}>; rel=\"history\""], WorkspaceLinks.ContributionHistory(id));
    }
}
