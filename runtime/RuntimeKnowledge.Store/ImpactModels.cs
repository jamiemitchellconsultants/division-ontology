using Npgsql;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Model;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

/// <summary>Per-snapshot impact model packages (ADR 0030).</summary>
public static class ImpactModels
{
    /// <summary>Stores the model package of a bundle that carries impact input, after building it once to validate it.
    /// A model already stored is kept, so staging and activation can call this to repair a missing one (ADR 0036).</summary>
    internal static async Task StageAsync(NpgsqlTransaction tx, ProjectionState state, VerifiedBundle bundle, CancellationToken token)
    {
        if (!HasImpactInput(bundle) || await ExistsAsync(tx, state.Generation, bundle.SnapshotId, token)) return;
        var (package, sha256) = ModelPackage.Create(bundle.SnapshotId, bundle.ManifestBytes,
            digest => bundle.ImpactObjects[digest]);
        try
        {
            ImpactModel.Build(ModelPackage.Open(package));
        }
        catch (ImpactException ex)
        {
            throw new WorkspaceException("invalidBundle", "Impact input is invalid: " + ex.Message, ex);
        }
        await ExecuteAsync(tx, """
            INSERT INTO workspace_projection.impact_models (generation, snapshot_id, package, package_sha256)
                VALUES ($1, $2, $3, $4) ON CONFLICT DO NOTHING
            """, token, state.Generation, bundle.SnapshotId, package, sha256);
    }

    internal static bool HasImpactInput(VerifiedBundle bundle) => bundle.Manifest.Files.Any(f => f.Role == "impactInput");

    internal static async Task<bool> ExistsAsync(NpgsqlTransaction tx, Guid generation, string snapshotId, CancellationToken token) =>
        await ScalarAsync(tx, """
            SELECT EXISTS (SELECT FROM workspace_projection.impact_models WHERE generation = $1 AND snapshot_id = $2)
            """, token, generation, snapshotId) is true;

    public static async Task<byte[]?> ReadPackageAsync(NpgsqlTransaction tx, Guid generation, string snapshotId,
        CancellationToken token) =>
        await ScalarAsync(tx, """
            SELECT package FROM workspace_projection.impact_models WHERE generation = $1 AND snapshot_id = $2
            """, token, generation, snapshotId) as byte[];
}
