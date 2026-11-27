using System.Text.Json.Nodes;

namespace RuntimeKnowledge.Contracts;

public sealed record ImpactKey(string SourceId, string NodeIri);
public sealed record ImpactSearchRequest(string? Query, string? ExactIri, string? SnapshotId = null, string? Cursor = null);
public sealed record ImpactPathsRequest(string SnapshotId, IReadOnlyList<ImpactKey> Starts, int? MaxDepth = null);
public sealed record ImpactViewRequest(string SnapshotId, ImpactKey Focus, string Level);

/// <summary>The catalog commit and source commits the impact model was built from.</summary>
public sealed record PinnedSnapshot(string CatalogCommit, string CatalogSha256, IReadOnlyList<PinnedSource> Sources);
public sealed record PinnedSource(string Id, string Commit);
public sealed record MasterListIdentity(string Status, string? CatalogCommit = null, string? Sha256 = null);

public sealed record PageEvidence(string? Title, string? ConfluenceUrl, IReadOnlyList<string>? Lines);
/// <param name="Lines">The cited lines, a workspace-only excerpt (B28, B38); null when the text could not be read.</param>
public sealed record CitedEvidence(string Iri, string Path, int StartLine, int EndLine, string BlobId, string Role,
    PageEvidence? Page = null, IReadOnlyList<string>? Lines = null);
public sealed record LeadPageEvidence(string EvidenceIri, string Path, int StartLine, int EndLine,
    string? Title, string? ConfluenceUrl, IReadOnlyList<string>? Lines);
/// <param name="SourceCommit">Null for a catalog concept lead, which has no source graph.</param>
public sealed record ImpactLead(ImpactKey Key, string MatchedText, string MatchKind, string? SourceCommit,
    bool Supported, string Reason, string Kind, IReadOnlyList<LeadPageEvidence>? PageEvidence = null);

public sealed record GapNode(string SourceId, string NodeIri);
public sealed record ImpactGap(string Code, string? Reason = null, string? SourceId = null, string? NodeIri = null,
    string? CoverageIri = null, string? Selector = null, string? Relationship = null, string? Term = null,
    string? SharedTerm = null, string? Path = null, string? EvidenceIri = null,
    IReadOnlyList<GapNode>? Nodes = null, int? Assertions = null);
public sealed record CoverageRecord(string SourceId, string Iri, string Selector, string Status, int Priority, string? Reason = null);
public sealed record NotFollowed(string Relationship, int Assertions);

public sealed record LinkCitation(string Source, string Local, string Shared, string Relation);
public sealed record ImpactStep(string Type,
    ImpactKey? Subject = null, ImpactKey? Object = null, string? Direction = null, string? AssertionIri = null,
    string? Relationship = null, string? Basis = null, IReadOnlyList<string>? EvidenceIris = null,
    IReadOnlyList<CitedEvidence>? Evidence = null, string? Commit = null,
    ImpactKey? From = null, ImpactKey? To = null, string? RegistryCommit = null,
    JsonObject? IdentityEvidence = null,
    string? SharedTerm = null, IReadOnlyList<LinkCitation>? Links = null, string? CatalogCommit = null);
public sealed record ImpactPath(string Id, IReadOnlyList<ImpactStep> Steps);

public sealed record ImpactSearchResult(string SnapshotId, PinnedSnapshot Pinned, string RegistryCommit,
    MasterListIdentity MasterList, IReadOnlyList<CoverageRecord> Coverage, IReadOnlyList<ImpactLead> Leads,
    IReadOnlyList<ImpactGap> Gaps, string? NextCursor = null);
public sealed record ImpactPathsResult(string SnapshotId, PinnedSnapshot Pinned, string RegistryCommit,
    MasterListIdentity MasterList, IReadOnlyList<NotFollowed> NotFollowed, IReadOnlyList<CoverageRecord> Coverage,
    IReadOnlyList<ImpactPath> Paths, IReadOnlyList<ImpactGap> Gaps);
/// <param name="View">The engine's C4 view, in the C4 pilot's view format.</param>
public sealed record C4ViewResult(string SnapshotId, JsonObject View, string Markdown);
