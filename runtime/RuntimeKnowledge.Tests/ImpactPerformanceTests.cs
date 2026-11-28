using System.Diagnostics;
using System.Net.Http.Json;
using RuntimeKnowledge.Api;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Tests.Impact;
using Xunit;
using Xunit.Abstractions;

namespace RuntimeKnowledge.Tests;

/// <summary>Q1, measured on demand on the reference container. The test server stands in for "at the API".</summary>
[Trait("Category", "Performance")]
public sealed class ImpactPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Search_and_paths_meet_Q1_at_the_95th_percentile()
    {
        string? snapshot = null;
        await using var api = await ApiFixture.OpenAsync("Keycloak", async scenario =>
        {
            var (manifest, blobs) = LargeModel.Bundle();
            snapshot = scenario.Bundles.Add(manifest, blobs);
            await scenario.Store.StageAsync(snapshot, CancellationToken.None);
            await scenario.Store.ActivateAsync(snapshot, CancellationToken.None);
        });
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var search = new ImpactSearchRequest("claim 4", null);
        var paths = new ImpactPathsRequest(snapshot!, [new ImpactKey(LargeModel.SourceId, LargeModel.Base + "n00042")], 4);

        async Task<double> TimeAsync(string route, object body)
        {
            var watch = Stopwatch.StartNew();
            using var response = await reader.PostAsJsonAsync(route, body);
            await response.Content.ReadAsByteArrayAsync();
            watch.Stop();
            response.EnsureSuccessStatusCode();
            return watch.Elapsed.TotalMilliseconds;
        }

        await TimeAsync("api/impact/search", search);                                  // warm-up
        api.Service<ImpactModelCache>().Clear();
        var coldLoad = await TimeAsync("api/impact/search", search);

        static double P95(List<double> samples) => samples.Order().ElementAt((int)Math.Ceiling(samples.Count * 0.95) - 1);
        var searchTimes = new List<double>();
        for (var i = 0; i < 100; i++) searchTimes.Add(await TimeAsync("api/impact/search", search));
        var pathTimes = new List<double>();
        for (var i = 0; i < 100; i++) pathTimes.Add(await TimeAsync("api/impact/paths", paths));

        var searchP95 = P95(searchTimes);
        var pathsP95 = P95(pathTimes);
        output.WriteLine($"{{\"search_p95_ms\":{searchP95:F0},\"paths_p95_ms\":{pathsP95:F0},\"cold_load_ms\":{coldLoad:F0}}}");
        Assert.True(searchP95 <= 2000, $"search p95 {searchP95:F0} ms exceeds 2000 ms");
        Assert.True(pathsP95 <= 5000, $"paths p95 {pathsP95:F0} ms exceeds 5000 ms");
    }

    // ADR 0035: preview re-traces every listed trace, so its worst case is 5 traces of 20 starts each. It must finish
    // within the 30 s API Gateway allows on AWS.
    [Fact]
    public async Task Preview_with_five_traces_of_twenty_starts_stays_within_the_gateway_limit()
    {
        string? snapshot = null;
        await using var api = await ApiFixture.OpenAsync("Keycloak", async scenario =>
        {
            var (manifest, blobs) = LargeModel.Bundle();
            snapshot = scenario.Bundles.Add(manifest, blobs);
            await scenario.Store.StageAsync(snapshot, CancellationToken.None);
            await scenario.Store.ActivateAsync(snapshot, CancellationToken.None);
        });
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var search = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("claim 4", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        var draft = new RuntimeKnowledge.Web.Services.AssessmentDraft();
        draft.Start(snapshot!, "CR-42", "Five traces", "Analyst A", RuntimeKnowledge.Web.Services.AssessmentDraft.Pins(search!));
        var traces = new System.Text.Json.Nodes.JsonArray();
        for (var t = 0; t < 5; t++)
            traces.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["starts"] = new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0, 20).Select(s => (System.Text.Json.Nodes.JsonNode)
                    new System.Text.Json.Nodes.JsonObject { ["sourceId"] = LargeModel.SourceId, ["nodeIri"] = LargeModel.Base + $"n{t * 20 + s:D5}" }).ToArray()),
                ["maxDepth"] = 4
            });
        draft.Record!["traces"] = traces;

        var times = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await reader.PostAsJsonAsync("api/assessments/preview", new AssessmentDraftRequest(snapshot!, draft.Record));
            await response.Content.ReadAsByteArrayAsync();
            watch.Stop();
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        output.WriteLine($"{{\"preview_5x20_max_ms\":{times.Max():F0}}}");
        Assert.True(times.Max() <= 30000, $"preview {times.Max():F0} ms exceeds the 30 s gateway limit");
    }
}
