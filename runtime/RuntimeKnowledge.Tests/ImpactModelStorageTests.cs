using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Store;
using RuntimeKnowledge.Tests.Impact;
using Xunit;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class ImpactModelStorageTests
{
    [Fact]
    public async Task Staging_a_bundle_with_impact_input_stores_one_verified_model()
    {
        await using var scenario = await Scenario.OpenAsync();
        var id = scenario.Bundles.AddCorpusBundle("confluence");           // copies a corpus manifest and its blobs into MemoryBundles
        await scenario.Store.StageAsync(id, CancellationToken.None);
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var tx = await connection.BeginTransactionAsync();
        var state = await ReadStateAsync(tx, CancellationToken.None);
        var package = await ImpactModels.ReadPackageAsync(tx, state.Generation, id, CancellationToken.None);
        Assert.NotNull(package);
        var model = ImpactModel.Build(ModelPackage.Open(package!));
        Assert.Equal(["fixture/operations", "fixture/policies"], model.Sources.Select(s => s.Id));
    }

    [Fact]
    public async Task A_bundle_without_impact_input_stores_no_model()
    {
        await using var scenario = await Scenario.OpenAsync();
        var id = scenario.Bundles.AddSnapshot("initial");                    // existing helper: no impactInput role
        await scenario.Store.StageAsync(id, CancellationToken.None);
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var tx = await connection.BeginTransactionAsync();
        var state = await ReadStateAsync(tx, CancellationToken.None);
        Assert.Null(await ImpactModels.ReadPackageAsync(tx, state.Generation, id, CancellationToken.None));
    }

    [Fact]
    public void A_package_round_trips_and_is_deterministic()
    {
        var id = CorpusPaths.FirstManifest("confluence", "search");
        var manifest = File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "manifests", id + ".json"));
        byte[] Blob(string sha) => File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "blobs", sha));
        var (first, sha1) = ModelPackage.Create(id, manifest, Blob);
        var (second, sha2) = ModelPackage.Create(id, manifest, Blob);
        Assert.Equal(sha1, sha2);
        Assert.Equal(first, second);
        Assert.Equal(id, ModelPackage.Open(first).Id);
    }
}
