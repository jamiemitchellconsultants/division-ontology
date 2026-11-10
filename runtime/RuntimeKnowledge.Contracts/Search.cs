using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuntimeKnowledge.Contracts;

public sealed record EmbeddingProfile(string Provider, string Model, string Revision, int Dimensions, string TextProfile)
{
    [JsonIgnore]
    public string Id => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this, WorkspaceJson.Options)));

    public void ValidateVector(float[]? values)
    {
        if (values is null || values.Length != Dimensions || values.Any(value => !float.IsFinite(value)) ||
            !values.Any(value => value != 0))
            throw new WorkspaceException("invalidEmbedding", "The provider returned an invalid embedding for this profile.");
    }
}

public enum EmbeddingPurpose { Document, Query }

public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(EmbeddingProfile profile, string input, EmbeddingPurpose purpose, CancellationToken token);
}

public sealed record EmbeddingJob(long Id, Guid Lease, EmbeddingProfile Profile, string Input);
public sealed record IndexBatchResult(int Claimed, int Indexed, int Deferred, bool Promoted);
public sealed record SearchRequest(string Query, int Limit = 20);
public sealed record SearchHit(PublishedKey EntityKey, Guid? AnnotationId, string MatchedText, double Score,
    Provenance Provenance, RuntimeContribution? Annotation);
public sealed record SearchResult(string SnapshotId, IReadOnlyList<SearchHit> Hits, bool IndexPending);
