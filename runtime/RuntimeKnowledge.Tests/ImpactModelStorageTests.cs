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

    // What a worker from before this feature leaves: the snapshot staged, with no impact model.
    private static async Task DropModelAsync(Scenario scenario, string id)
    {
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var delete = new Npgsql.NpgsqlCommand("DELETE FROM workspace_projection.impact_models WHERE snapshot_id = $1", connection);
        delete.Parameters.AddWithValue(id);
        Assert.Equal(1, await delete.ExecuteNonQueryAsync());
    }

    private static async Task<string?> ModelDigestAsync(Scenario scenario, string id)
    {
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var read = new Npgsql.NpgsqlCommand("SELECT package_sha256 FROM workspace_projection.impact_models WHERE snapshot_id = $1", connection);
        read.Parameters.AddWithValue(id);
        return await read.ExecuteScalarAsync() as string;
    }

    [Fact]
    public async Task A_missing_model_is_repaired_by_staging_again_and_by_activation()          // ADR 0036 (PR #220 review 22)
    {
        await using var scenario = await Scenario.OpenAsync();
        var id = scenario.Bundles.AddCorpusBundle("confluence");
        await scenario.Store.StageAsync(id, CancellationToken.None);
        var digest = await ModelDigestAsync(scenario, id);

        await DropModelAsync(scenario, id);
        await scenario.Store.StageAsync(id, CancellationToken.None);
        Assert.Equal(digest, await ModelDigestAsync(scenario, id));

        await DropModelAsync(scenario, id);
        await scenario.Store.ActivateAsync(id, CancellationToken.None);
        Assert.Equal(digest, await ModelDigestAsync(scenario, id));

        await DropModelAsync(scenario, id);
        await scenario.Store.ActivateAsync(id, CancellationToken.None);   // already active
        Assert.Equal(digest, await ModelDigestAsync(scenario, id));
    }

    [Fact]
    public async Task A_present_model_is_not_rebuilt_when_staged_again()                        // ADR 0036
    {
        await using var scenario = await Scenario.OpenAsync();
        var id = scenario.Bundles.AddCorpusBundle("confluence");
        await scenario.Store.StageAsync(id, CancellationToken.None);
        await using (var connection = await scenario.Db.OpenAsync(CancellationToken.None))
        await using (var mark = new Npgsql.NpgsqlCommand("UPDATE workspace_projection.impact_models SET package_sha256 = $1 WHERE snapshot_id = $2", connection))
        {
            mark.Parameters.AddWithValue(new string('0', 64));
            mark.Parameters.AddWithValue(id);
            await mark.ExecuteNonQueryAsync();
        }
        await scenario.Store.StageAsync(id, CancellationToken.None);
        await scenario.Store.ActivateAsync(id, CancellationToken.None);
        Assert.Equal(new string('0', 64), await ModelDigestAsync(scenario, id));
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
