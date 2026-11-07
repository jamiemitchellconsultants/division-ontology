namespace RuntimeKnowledge.Contracts;

public abstract record ContributionCommand;
public sealed record CreateAnnotation(PublishedKey Target, string Category, string Text) : ContributionCommand;
public sealed record CreateRelationship(PublishedKey From, PublishedKey To, RelationshipMeaning Meaning) : ContributionCommand;
public sealed record EditAnnotation(Guid Id, int ExpectedVersion, string Category, string Text) : ContributionCommand;
public sealed record EditRelationship(Guid Id, int ExpectedVersion, PublishedKey From, PublishedKey To,
    RelationshipMeaning Meaning) : ContributionCommand;
public sealed record RemoveContribution(Guid Id, int ExpectedVersion) : ContributionCommand;
public sealed record AssociateCodification(Guid Id, int ExpectedVersion, PublishedKey PublishedRelationship) : ContributionCommand;

public sealed record RelationshipMeaning(PublishedKey? PublishedType, string? CustomLabel, string? Explanation)
{
    public static RelationshipMeaning Published(PublishedKey type) => new(type, null, null);
    public static RelationshipMeaning Custom(string label, string explanation) => new(null, label, explanation);
}

public sealed record AnnotationData(PublishedKey Target, string Category, string Text, Provenance TargetProvenance);
public sealed record RelationshipData(PublishedKey From, PublishedKey To, RelationshipMeaning Meaning,
    string Label, Provenance FromProvenance, Provenance ToProvenance, Provenance? MeaningProvenance);
public sealed record CodificationAssociation(Guid Id, PublishedKey PublishedRelationship, Provenance PublishedProvenance,
    Actor Actor, DateTimeOffset OccurredAt);
public sealed record RuntimeContribution(Guid Id, int Version, string Kind, Actor Author, DateTimeOffset CreatedAt,
    string CreatedSnapshotId, bool Removed, AnnotationData? Annotation, RelationshipData? Relationship,
    IReadOnlyList<CodificationAssociation> Associations);
public sealed record ContributionChange(RuntimeContribution? Before, RuntimeContribution After);
public sealed record ContributionResult(Guid Id, int Version, long Position);
public sealed record HistoryEntry(long Position, string Type, Actor Actor, DateTimeOffset OccurredAt,
    Guid ContributionId, int Revision, RuntimeContribution? Before, RuntimeContribution After, string SnapshotId);
public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Entries, string? NextCursor);

public sealed record EndpointContext(PublishedKey Key, string Label, Provenance Provenance, bool Available);
public sealed record TraversalEntity(PublishedKey Key, string Label, Provenance Provenance, bool Available,
    IReadOnlyList<RuntimeContribution> Annotations);
