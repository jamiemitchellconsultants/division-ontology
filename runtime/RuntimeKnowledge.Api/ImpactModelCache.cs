using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Api;

/// <summary>Holds the impact models of at most two snapshots in memory, keyed by generation and snapshot (ADR 0030).</summary>
public sealed class ImpactModelCache(Db db)
{
    private const int Capacity = 2;
    private readonly object _gate = new();
    private readonly LinkedList<((Guid, string) Key, Lazy<Task<ImpactModel>> Model)> _entries = new();

    /// <summary>The active snapshot's model. When <paramref name="expectedSnapshot"/> is set and is not the active
    /// snapshot, the caller's work belongs to an earlier snapshot and is refused.</summary>
    public async Task<(string SnapshotId, ImpactModel Model)> GetAsync(string? expectedSnapshot, CancellationToken token)
    {
        var (generation, snapshotId) = await ImpactReads.ActiveAsync(db, token);
        if (expectedSnapshot is not null && expectedSnapshot != snapshotId)
            throw new WorkspaceException("snapshotChanged", "The workspace snapshot changed. Start again from a new search.");

        Lazy<Task<ImpactModel>> model;
        lock (_gate)
        {
            var found = _entries.FirstOrDefault(e => e.Key == (generation, snapshotId));
            if (found.Model is not null) model = found.Model;
            else
            {
                model = new Lazy<Task<ImpactModel>>(() => LoadAsync(generation, snapshotId));
                _entries.AddLast(((generation, snapshotId), model));
                if (_entries.Count > Capacity) _entries.RemoveFirst();
            }
        }

        try
        {
            return (snapshotId, await model.Value);
        }
        catch
        {
            lock (_gate)
            {
                var failed = _entries.FirstOrDefault(e => ReferenceEquals(e.Model, model));
                if (failed.Model is not null) _entries.Remove(failed);
            }
            throw;
        }
    }

    private async Task<ImpactModel> LoadAsync(Guid generation, string snapshotId)
    {
        var package = await ImpactReads.PackageAsync(db, generation, snapshotId, CancellationToken.None)
            ?? throw new WorkspaceException("impactUnavailable", "This snapshot has no impact model. Publish a new bundle.");
        return ImpactModel.Build(ModelPackage.Open(package));
    }
}
