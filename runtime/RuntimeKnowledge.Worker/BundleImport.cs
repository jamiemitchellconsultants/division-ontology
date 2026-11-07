using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Worker;

public sealed class BundleImport(SnapshotStore snapshots)
{
    public Task<SnapshotCandidate> StageAsync(string manifestId, CancellationToken cancellationToken) =>
        snapshots.StageAsync(manifestId, cancellationToken);

    public Task<long> ActivateAsync(string manifestId, CancellationToken cancellationToken) =>
        snapshots.ActivateAsync(manifestId, cancellationToken);
}
