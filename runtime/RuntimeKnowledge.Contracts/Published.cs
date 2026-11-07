using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuntimeKnowledge.Contracts;

public static class WorkspaceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record PublishedKey(string SourceId, string Kind, string Identifier)
{
    [JsonIgnore]
    public string StorageKey => JsonSerializer.Serialize(new[] { SourceId, Kind, Identifier });
}

public sealed record Actor(string Issuer, string Subject);
public sealed record Evidence(string Path, int StartLine, int EndLine, string BlobId, string Role);
public sealed record Provenance(PublishedKey Key, string SourceCommit, string? SnapshotId,
    string? Basis, IReadOnlyList<Evidence> Evidence);
public sealed record PublishedEntity(PublishedKey Key, string Label, string? Description,
    IReadOnlyList<string> Types, Provenance Provenance, JsonElement? C4 = null);
public sealed record RelationshipType(PublishedKey Key, string Label, string? Description,
    string? Domain, string? Range, Provenance Provenance, JsonElement? Context = null);
public sealed record PublishedEdge(PublishedKey Key, PublishedKey From, PublishedKey To, string Label,
    PublishedKey? RelationshipType, Provenance Provenance, JsonElement? Context = null);
public sealed record CoverageGap(string Code, string? SourceId, string? NodeIri, string Reason,
    string? CoverageIri = null, string? Selector = null);
public sealed record PublishedProjection(IReadOnlyList<PublishedEntity> Entities,
    IReadOnlyList<RelationshipType> RelationshipTypes, IReadOnlyList<PublishedEdge> Edges,
    IReadOnlyList<CoverageGap> Gaps);
public sealed record SourceRevision(string Id, string Commit, int? GraphSchemaVersion, string GraphStatus);
public sealed record BundleFile(string Role, string SourceId, string Path, string Sha256, long Bytes);
public sealed record BundleManifest(int SchemaVersion, string DivisionCommit, string CatalogSha256,
    IReadOnlyList<SourceRevision> Sources, string? RegistryCommit, IReadOnlyList<BundleFile> Files);
public sealed record VerifiedBundle(string SnapshotId, BundleManifest Manifest, PublishedProjection Projection);

public sealed record PageRequest(string? Cursor = null, int Limit = 50);
public sealed record SnapshotCandidate(string SnapshotId, string State);
public sealed record InventoryPage(string SnapshotId, IReadOnlyList<PublishedEntity> Entities,
    IReadOnlyList<RelationshipType> RelationshipTypes, IReadOnlyList<CoverageGap> Gaps, string? NextCursor);
public sealed record TraversalRequest(PublishedKey Start, string Direction = "outgoing",
    int MaxDepth = 2, bool IncludeUnavailable = false);
public sealed record TraversalPath(IReadOnlyList<TraversalStep> Steps);
public sealed record TraversalStep(PublishedKey? Key, Guid? RuntimeId, PublishedKey From, PublishedKey To,
    string Label, string Direction, string Origin, Provenance Provenance,
    bool EndpointUnavailable = false, string? LastKnownSnapshotId = null, JsonElement? Context = null);
public sealed record TraversalResult(string SnapshotId, IReadOnlyList<TraversalPath> Paths,
    IReadOnlyList<CoverageGap> Gaps, bool Truncated);

public sealed class WorkspaceException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

public interface IBundleObjects
{
    Task<Stream> OpenAsync(string objectKey, CancellationToken cancellationToken);
    Task PutImmutableAsync(string objectKey, Stream content, CancellationToken cancellationToken);
}
