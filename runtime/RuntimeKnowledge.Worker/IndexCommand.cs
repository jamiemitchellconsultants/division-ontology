using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;

namespace RuntimeKnowledge.Worker;

public sealed class IndexCommand(IndexRepository index, IEmbeddingProvider embeddings, EmbeddingProfile profile)
{
    public async Task<IndexBatchResult> RunBatchAsync(int limit, CancellationToken token)
    {
        await index.SelectAsync(profile, token);
        await index.EnqueueAsync(token);
        var jobs = await index.ClaimAsync(limit, token);
        var indexed = 0;
        var deferred = 0;
        foreach (var job in jobs)
        {
            float[] vector;
            try
            {
                vector = await embeddings.EmbedAsync(job.Profile, job.Input, EmbeddingPurpose.Document, token);
                job.Profile.ValidateVector(vector);
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                await index.DeferAsync(job, token);
                deferred++;
                continue;
            }
            if (await index.CompleteAsync(job, vector, token)) indexed++;
        }
        var promoted = await index.PromoteIfReadyAsync(token);
        return new IndexBatchResult(jobs.Count, indexed, deferred, promoted);
    }
}
