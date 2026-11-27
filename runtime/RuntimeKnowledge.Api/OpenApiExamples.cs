using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Api;

/// <summary>Invented example values for the spec. None comes from a catalog or names a real person (B4).</summary>
internal static class OpenApiExamples
{
    public static readonly PublishedKey Order = new("example-sales", "concept", "https://example.test/sales#Order");
    public static readonly PublishedKey Invoice = new("example-billing", "concept", "https://example.test/billing#Invoice");
    public static readonly CreateAnnotation CreateAnnotation =
        new(Order, "businessExplanation", "Example: an order is accepted once payment is authorised.");
    public static readonly CreateRelationship CreateRelationship =
        new(Order, Invoice, new RelationshipMeaning(null, "is billed by", "Example: each accepted order is billed by one invoice."));
    public static readonly SearchRequest Search = new("example order lifecycle", 10);
    public static readonly TraversalRequest Traversal = new(Order, "outgoing", 2);
    public static readonly TraversalRequest TraversalNextPage = new(Order, "outgoing", 2, Cursor: "opaque-continuation-cursor");

    public static readonly ImpactKey ClaimLead = new("example-claims", "https://example.test/claims/graph#claim");
    public const string ExampleSnapshot = "example-snapshot-id";
    public static readonly ImpactSearchRequest ImpactSearch = new("example claim approval", null);
    public static readonly ImpactSearchRequest ImpactSearchNextPage = new("example claim approval", null, Cursor: "opaque-continuation-cursor");
    public static readonly ImpactPathsRequest ImpactPaths = new(ExampleSnapshot, [ClaimLead], 4);
    public static readonly ImpactViewRequest ImpactView = new(ExampleSnapshot, ClaimLead, "container");

    private static System.Text.Json.Nodes.JsonObject ExampleRecord() => new()
    {
        ["schemaVersion"] = 2,
        ["requestId"] = "example-CR-1",
        ["description"] = "Example: raise the claim approval threshold.",
        ["assessor"] = "Example assessor",
        ["pins"] = new System.Text.Json.Nodes.JsonObject
        {
            ["snapshotId"] = new System.Text.Json.Nodes.JsonObject { ["catalogCommit"] = "example-catalog-commit" },
            ["masterList"] = new System.Text.Json.Nodes.JsonObject { ["status"] = "none" },
            ["registry"] = null
        },
        ["confirmedLeads"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            { ["sourceId"] = ClaimLead.SourceId, ["nodeIri"] = ClaimLead.NodeIri }),
        ["rejectedLeads"] = new System.Text.Json.Nodes.JsonArray(),
        ["likelyImpacts"] = new System.Text.Json.Nodes.JsonArray(),
        ["unresolved"] = new System.Text.Json.Nodes.JsonArray()
    };
    public static readonly AssessmentDraftRequest AssessmentDraft = new(ExampleSnapshot, ExampleRecord());
    public static readonly AssessmentEditRequest AssessmentEdit = new(ExampleSnapshot, 1, ExampleRecord());

    public static IReadOnlyList<object> All { get; } =
        [CreateAnnotation, CreateRelationship, Search, Traversal, TraversalNextPage, ImpactSearch, ImpactSearchNextPage, ImpactPaths, ImpactView,
         AssessmentDraft, AssessmentEdit];
}
