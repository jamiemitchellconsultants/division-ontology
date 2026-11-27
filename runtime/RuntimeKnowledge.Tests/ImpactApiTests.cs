using System.Net;
using System.Net.Http.Json;
using RuntimeKnowledge.Contracts;
using Xunit;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class ImpactApiTests
{
    private const string P = "https://example.test/policies/graph#";

    // ApiFixture owns and disposes its scenario. The new overload runs `arrange` in place of activating "initial".
    private static async Task<(ApiFixture Api, string Snapshot)> OpenAsync(string corpus = "confluence")
    {
        string? snapshot = null;
        var api = await ApiFixture.OpenAsync("Keycloak", async scenario =>
        {
            snapshot = scenario.Bundles.AddCorpusBundle(corpus);
            await scenario.Store.StageAsync(snapshot, CancellationToken.None);
            await scenario.Store.ActivateAsync(snapshot, CancellationToken.None);
        });
        return (api, snapshot!);
    }

    [Fact]
    public async Task A_reader_searches_and_gets_supported_leads_with_the_snapshot()       // B15
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var result = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("expense", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        Assert.Equal(snapshot, result!.SnapshotId);
        Assert.Contains(result.Leads, l => l.Key.NodeIri == P + "expense-policy" && l.Supported);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task Search_pages_by_cursor_at_most_100_and_ends_without_one()            // B15
    {
        var (api, _) = await OpenAsync("paging");      // corpus scenario 'paging': 250 nodes labelled "Claim <n>"
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var seen = new List<ImpactLead>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("claim", null, Cursor: cursor)))
                .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
            Assert.True(page!.Leads.Count <= 100);
            seen.AddRange(page.Leads);
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null);
        Assert.Equal(3, pages);
        Assert.Equal(250, seen.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public async Task A_cursor_for_another_query_is_invalid()
    {
        var (api, _) = await OpenAsync("paging");
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var first = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("claim", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        var reused = await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("other", null, Cursor: first!.NextCursor));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Contains("invalidCursor", await reused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_reader_traces_paths_step_by_step_with_cited_lines()                // B16, B28
    {
        var (api, snapshot) = await OpenAsync();
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var result = await (await reader.PostAsJsonAsync("api/impact/paths",
            new ImpactPathsRequest(snapshot, [new ImpactKey("fixture/policies", P + "expense-policy")])))
            .Content.ReadFromJsonAsync<ImpactPathsResult>(WorkspaceJson.Options);
        var twoStep = result!.Paths.First(p => p.Steps.Count == 2);
        Assert.All(twoStep.Steps, step => Assert.NotNull(step.Evidence));
        Assert.Equal("Expense policy", twoStep.Steps[0].Evidence![0].Page!.Title);
        Assert.Equal(["Managers approve claims in the finance system."], twoStep.Steps[1].Evidence![0].Lines);
        Assert.DoesNotContain(result.Paths, p => p.Steps.Count == 1 && p.Steps[0].Object?.NodeIri == P + "ledger"
            && p.Steps[0].Subject?.NodeIri == P + "expense-policy");                    // never shown as a direct relationship
    }

    [Fact]
    public async Task Requests_for_another_snapshot_are_refused_as_snapshot_changed()      // B36 groundwork
    {
        var (api, _) = await OpenAsync();
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var response = await reader.PostAsJsonAsync("api/impact/paths",
            new ImpactPathsRequest(new string('0', 64), [new ImpactKey("fixture/policies", P + "expense-policy")]));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("snapshotChanged", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_snapshot_without_a_model_is_impact_unavailable()
    {
        await using var api = await ApiFixture.OpenAsync("Keycloak");                  // activates "initial", which has no impactInput
        var response = await api.ClientWithToken(["ReadKnowledgeGraph"])
            .PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("order", null));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("impactUnavailable", await response.Content.ReadAsStringAsync());
    }

    [Theory]                                                                           // Q2
    [InlineData("api/impact/search")]
    [InlineData("api/impact/paths")]
    [InlineData("api/impact/views")]
    public async Task Every_impact_route_refuses_unauthenticated_calls(string route)
    {
        var (api, _) = await OpenAsync();
        await using var a = api;
        var response = await api.CreateClient().PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
