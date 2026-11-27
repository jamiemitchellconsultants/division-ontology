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
}
