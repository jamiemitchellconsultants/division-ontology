using System.Data;
using RuntimeKnowledge.Contracts;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

public sealed class SearchStore(Db db, IEmbeddingProvider embeddings, double minimumSimilarity = 0.35)
{
    public async Task<SearchResult> SearchAsync(string query, int limit, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 2048 || limit is < 1 or > 100)
            throw new WorkspaceException("invalidSearch", "Provide a query up to 2,048 characters and a limit from 1 to 100.");
        if (!double.IsFinite(minimumSimilarity) || minimumSimilarity is < -1 or > 1)
            throw new WorkspaceException("searchUnavailable", "The configured similarity cutoff is invalid.");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var profile = await new IndexRepository(db).ReadActiveProfileAsync(token) ??
                throw new WorkspaceException("searchUnavailable", "No embedding profile is active.");
            float[] vector;
            try
            {
                vector = await embeddings.EmbedAsync(profile, query.Trim(), EmbeddingPurpose.Query, token);
                profile.ValidateVector(vector);
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                throw new WorkspaceException("searchUnavailable", "Semantic search is temporarily unavailable.", error);
            }

            await using var connection = await db.OpenAsync(token);
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
            var state = await ReadStateAsync(tx, token);
            var snapshot = state.RequireSnapshot();
            var current = await IndexRepository.ReadActiveProfileAsync(tx, token);
            if (profile != current) continue;

            var hits = new List<SearchHit>();
            await using (var command = Command(tx, """
                WITH eligible AS MATERIALIZED (
                    SELECT p.body AS entity, c.body AS annotation, j.input_text, j.embedding, j.job_id
                    FROM workspace_projection.embedding_jobs j
                    LEFT JOIN workspace_projection.contributions c
                      ON c.generation = j.generation AND c.contribution_id = j.contribution_id
                      AND c.revision = j.revision AND NOT c.removed
                    JOIN workspace_projection.published_records p
                      ON p.generation = j.generation AND p.snapshot_id = $2
                      AND p.record_key = CASE WHEN j.contribution_id IS NULL THEN j.record_key ELSE c.target_key END
                    WHERE j.generation = $1 AND j.profile_id = $3 AND j.embedding IS NOT NULL
                      AND (j.contribution_id IS NOT NULL OR j.scope = $2)
                ), scored AS (
                    SELECT entity, annotation, input_text, job_id, 1 - (embedding <=> $4::vector) AS score
                    FROM eligible
                )
                SELECT entity::text, annotation::text, input_text, score FROM scored
                WHERE score >= $5 ORDER BY score DESC, job_id LIMIT $6
                """, state.Generation, snapshot, profile.Id, IndexRepository.VectorText(vector),
                minimumSimilarity, limit))
            await using (var rows = await command.ExecuteReaderAsync(token))
            {
                while (await rows.ReadAsync(token))
                {
                    var entity = ReadJson<PublishedEntity>(rows.GetString(0));
                    var annotation = rows.IsDBNull(1) ? null : ReadJson<RuntimeContribution>(rows.GetString(1));
                    hits.Add(new SearchHit(entity.Key, annotation?.Id, rows.GetString(2), rows.GetDouble(3),
                        entity.Provenance, annotation));
                }
            }

            var pending = !await IndexRepository.BaselineReadyAsync(tx, state.Generation, snapshot, profile.Id, token) ||
                await IndexRepository.AnnotationIndexPendingAsync(tx, state, profile.Id, token);
            return new SearchResult(snapshot, hits, pending);
        }
        throw new WorkspaceException("searchUnavailable", "The search profile changed. Retry your search.");
    }
}
