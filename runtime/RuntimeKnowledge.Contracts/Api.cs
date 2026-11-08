namespace RuntimeKnowledge.Contracts;

public sealed record WorkspaceSession(bool CanRead, bool CanManage);
public sealed record EntityDetailRequest(PublishedKey Key);
public sealed record RelationshipDetailRequest(PublishedKey? Key = null, Guid? RuntimeId = null);
public sealed record EntityDetail(string SnapshotId, PublishedEntity Entity, bool Available,
    IReadOnlyList<RuntimeContribution> Annotations);
public sealed record RelationshipDetail(string SnapshotId, TraversalStep Relationship);

public sealed record EditContributionRequest(int ExpectedVersion, string? Category = null, string? Text = null,
    PublishedKey? From = null, PublishedKey? To = null, RelationshipMeaning? Meaning = null);
public sealed record RemoveContributionRequest(int ExpectedVersion);
public sealed record CodificationRequest(int ExpectedVersion, PublishedKey PublishedRelationship);
