using System.Data;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

/// <summary>Reads for the API's impact model cache.</summary>
public static class ImpactReads
{
    public static async Task<(Guid Generation, string SnapshotId)> ActiveAsync(Db db, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        return (state.Generation, state.RequireSnapshot());
    }

    public static async Task<byte[]?> PackageAsync(Db db, Guid generation, string snapshotId, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        return await ImpactModels.ReadPackageAsync(tx, generation, snapshotId, token);
    }
}
