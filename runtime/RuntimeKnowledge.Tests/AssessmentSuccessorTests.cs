using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Tests.OpenApi;
using Xunit;

namespace RuntimeKnowledge.Tests;

/// <summary>B35: a manager starts a new assessment from a saved one, on the active snapshot.</summary>
[Trait("Category", "Database")]
public sealed class AssessmentSuccessorTests
{
    private static readonly string[] Read = ["ReadKnowledgeGraph"];
    private static readonly string[] Manage = ["ReadKnowledgeGraph", "ManageKnowledgeGraph"];

    private static async Task<(ApiFixture Api, string Snapshot)> OpenAsync()
    {
        string? snapshot = null;
        var api = await ApiFixture.OpenAsync("Keycloak", async scenario =>
        {
            snapshot = scenario.Bundles.AddCorpusBundle("confluence");
            await scenario.Store.StageAsync(snapshot, CancellationToken.None);
            await scenario.Store.ActivateAsync(snapshot, CancellationToken.None);
        });
        return (api, snapshot!);
    }

    private static JsonObject Draft() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root,
        "test", "impact-corpus", "confluence", "inputs", "007", "assessment-input.json")))!.AsObject();

    private static async Task<Guid> SaveAsync(ApiFixture api, string snapshot, JsonObject record) =>
        (await (await api.ClientWithToken(Manage).PostAsJsonAsync("api/assessments", new AssessmentDraftRequest(snapshot, record)))
            .Content.ReadFromJsonAsync<AssessmentSaved>(WorkspaceJson.Options))!.Id;

    // The confluence bundle again, with one more inspected file: a new snapshot id with the same model input.
    private static async Task<string> RepublishAsync(ApiFixture api)
    {
        var newer = api.Scenario.Bundles.AddCorpusBundle("confluence", ("fixture/policies", "inputs/999/marker.txt"));
        await api.Scenario.Store.StageAsync(newer, CancellationToken.None);
        await api.Scenario.Store.ActivateAsync(newer, CancellationToken.None);
        return newer;
    }

    private static async Task<AssessmentSuccessor> SuccessorAsync(ApiFixture api, Guid earlier)
    {
        var response = await api.ClientWithToken(Manage).PostAsync($"api/assessments/{earlier:D}/successor", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssessmentSuccessor>(WorkspaceJson.Options))!;
    }

    [Fact]
    public async Task A_manager_starts_a_new_draft_on_the_active_snapshot_holding_only_the_request()
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var earlier = await SaveAsync(api, snapshot, Draft());
        var newer = await RepublishAsync(api);
        Assert.NotEqual(snapshot, newer);

        var successor = await SuccessorAsync(api, earlier);
        Assert.Equal(newer, successor.SnapshotId);
        var record = successor.Record;
        Assert.Equal((string)Draft()["requestId"]!, (string)record["requestId"]!);
        Assert.Equal((string)Draft()["description"]!, (string)record["description"]!);
        foreach (var field in new[] { "confirmedLeads", "rejectedLeads", "likelyImpacts", "unresolved" })
            Assert.Empty(record[field]!.AsArray());
        Assert.Equal(earlier.ToString("D"), (string)record["startedFrom"]!["assessmentId"]!);

        var search = await (await api.ClientWithToken(Read).PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("expense", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(search!.Pinned, WorkspaceJson.Options)),
            record["pins"]!["snapshotId"]), "the draft pins the active model's snapshot");
        Assert.Equal(search.Registry!.Commit, (string)record["pins"]!["registry"]!["commit"]!);
    }

    [Fact]
    public async Task A_reader_without_manage_gets_403()
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var earlier = await SaveAsync(api, snapshot, Draft());
        var response = await api.ClientWithToken(Read).PostAsync($"api/assessments/{earlier:D}/successor", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Saving_the_successor_links_back_to_the_earlier_assessment()
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var earlier = await SaveAsync(api, snapshot, Draft());
        var newer = await RepublishAsync(api);
        var record = (await SuccessorAsync(api, earlier)).Record;
        record["assessor"] = "Analyst B";
        var id = await SaveAsync(api, newer, record);
        var saved = await api.ClientWithToken(Read).GetFromJsonAsync<SavedAssessment>($"api/assessments/{id:D}", WorkspaceJson.Options);
        Assert.Equal(earlier, saved!.StartedFrom);
    }

    [Fact]
    public async Task An_unknown_earlier_assessment_is_not_found()
    {
        var (api, _) = await OpenAsync();
        await using var _ = api;
        var response = await api.ClientWithToken(Manage).PostAsync($"api/assessments/{Guid.NewGuid():D}/successor", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("assessmentNotFound", await response.Content.ReadAsStringAsync());
    }
}
