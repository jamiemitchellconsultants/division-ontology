using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Worker;

public sealed class ReplayCommand(ReplayStore replay)
{
    public Task<RebuildReport> RebuildAsync(long position, CancellationToken token) =>
        replay.RebuildAsync(position, token);

    public Task SetWritesPausedAsync(bool paused, CancellationToken token) =>
        replay.SetWritesPausedAsync(paused, token);

    public Task ActivateAsync(Guid generation, long expectedPosition, CancellationToken token) =>
        replay.CutoverAsync(generation, expectedPosition, token);
}
