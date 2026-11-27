using System.Net.Http.Json;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Tests.OpenApi;
using Xunit;

namespace RuntimeKnowledge.Tests;

/// <summary>Cited page text reaches signed-in readers only (B28, B29, Q7).</summary>
[Trait("Category", "Database")]
public sealed class ImpactPublicationTests
{
    private const string P = "https://example.test/policies/graph#";
    private static readonly string[] PageText =
    [
        "Claims over 500 need a manager's approval.",
        "Managers approve claims in the finance system.",
        "The old ledger still exports claims nightly.",
        "The finance team runs the claims job."
    ];

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

    private static void AssertNoPageText(string body)
    {
        foreach (var line in PageText) Assert.DoesNotContain(line, body);
    }

    [Fact]
    public async Task Unauthenticated_responses_carry_no_cited_text()                       // Q7
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var anonymous = api.CreateClient();
        AssertNoPageText(await anonymous.GetStringAsync("/api/openapi/v1.json"));
        AssertNoPageText(await (await anonymous.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("expense", null)))
            .Content.ReadAsStringAsync());
        AssertNoPageText(await (await anonymous.PostAsJsonAsync("api/impact/paths",
            new ImpactPathsRequest(snapshot, [new ImpactKey("fixture/policies", P + "ledger")]))).Content.ReadAsStringAsync());
    }

    [Fact]
    public void The_committed_spec_carries_no_cited_text() =>
        AssertNoPageText(OpenApiHost.CommittedSpec().ToJsonString());

    [Fact]
    public async Task A_signed_in_reader_sees_the_lines_of_an_ordinary_document()             // B28, B38
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        // ledger exports approval, citing legacy-notes.md. From expense-policy that step is never taken,
        // because approval is already on the path, so the trace starts at the ledger.
        var result = await (await api.ClientWithToken(["ReadKnowledgeGraph"]).PostAsJsonAsync("api/impact/paths",
            new ImpactPathsRequest(snapshot, [new ImpactKey("fixture/policies", P + "ledger")])))
            .Content.ReadFromJsonAsync<ImpactPathsResult>(WorkspaceJson.Options);
        var legacy = result!.Paths.SelectMany(p => p.Steps).SelectMany(s => s.Evidence ?? [])
            .First(e => e.Path == "pages/legacy-notes.md");
        Assert.Null(legacy.Page);
        Assert.Equal(["The old ledger still exports claims nightly."], legacy.Lines);
    }
}
