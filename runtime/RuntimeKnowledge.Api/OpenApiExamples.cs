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

    public static IReadOnlyList<object> All { get; } = [CreateAnnotation, CreateRelationship, Search, Traversal, TraversalNextPage];
}
