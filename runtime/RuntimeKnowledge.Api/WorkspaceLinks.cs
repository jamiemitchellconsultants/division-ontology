using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Api;

/// <summary>
/// RFC 8288 Link headers: the runtime half of the spec's HATEOAS links. Relation names are registered
/// names where their semantics fit (next, service-desc, service-doc); workspace-specific relations are
/// absolute URIs under RelationBase, mapped to OpenAPI operations in the spec's x-agent-discovery.
/// </summary>
internal static class WorkspaceLinks
{
    public const string RelationBase = "https://github.com/jamiemitchellconsultants/division-ontology#link-relations/";

    private static string Rel(string name) => RelationBase + name;

    public static readonly IReadOnlyList<string> Health =
    [
        $"<{DocsUi.SpecPath}>; rel=\"service-desc\"",
        "</api/docs>; rel=\"service-doc\"",
        $"</api/session>; rel=\"{Rel("session")}\""
    ];

    public static readonly IReadOnlyList<string> Session =
    [
        $"</api/inventory>; rel=\"{Rel("inventory")}\"",
        $"</api/history>; rel=\"{Rel("history")}\"",
        $"<{DocsUi.SpecPath}>; rel=\"service-desc\""
    ];

    public static readonly IReadOnlyList<string> Inventory =
    [
        $"</api/search>; rel=\"{Rel("search")}\""
    ];

    public static readonly IReadOnlyList<string> EntityDetail =
    [
        $"</api/traverse>; rel=\"{Rel("traverse")}\"",
        $"</api/contributions/annotations>; rel=\"{Rel("createAnnotation")}\""
    ];

    public static readonly IReadOnlyList<string> RelationshipDetail =
    [
        $"</api/entities/detail>; rel=\"{Rel("entityDetail")}\""
    ];

    public static readonly IReadOnlyList<string> Search =
    [
        $"</api/entities/detail>; rel=\"{Rel("entityDetail")}\""
    ];

    public static readonly IReadOnlyList<string> Traverse =
    [
        $"</api/entities/detail>; rel=\"{Rel("entityDetail")}\"",
        $"</api/relationships/detail>; rel=\"{Rel("relationshipDetail")}\""
    ];

    public static readonly IReadOnlyList<string> TraverseNext =
    [
        "</api/traverse>; rel=\"next\""
    ];

    public static readonly IReadOnlyList<string> ImpactSearch =
    [
        $"</api/impact/paths>; rel=\"{Rel("traceImpact")}\"",
        $"</api/impact/views>; rel=\"{Rel("viewC4")}\""
    ];

    public static readonly IReadOnlyList<string> ImpactSearchNext =
    [
        "</api/impact/search>; rel=\"next\""
    ];

    public static readonly IReadOnlyList<string> ImpactPaths =
    [
        $"</api/impact/views>; rel=\"{Rel("viewC4")}\""
    ];

    public static readonly IReadOnlyList<string> ImpactViews =
    [
        $"</api/impact/paths>; rel=\"{Rel("traceImpact")}\""
    ];

    public static IReadOnlyList<string> NextPage(string path, string? nextCursor, int? limit = null, Guid? contributionId = null)
    {
        if (nextCursor is null) return [];
        var query = new List<string> { $"cursor={Uri.EscapeDataString(nextCursor)}" };
        if (contributionId is { } id) query.Add($"contributionId={id:D}");
        if (limit is { } value) query.Add($"limit={value}");
        return [$"<{path}?{string.Join('&', query)}>; rel=\"next\""];
    }

    public static IReadOnlyList<string> ContributionHistory(Guid id) =>
        [$"</api/history?contributionId={id:D}>; rel=\"{Rel("history")}\""];

    public static IReadOnlyList<string> Contribution(Guid id, bool codifiable)
    {
        var links = new List<string>
        {
            $"</api/history?contributionId={id:D}>; rel=\"{Rel("history")}\"",
            $"</api/contributions/{id:D}>; rel=\"{Rel("edit")}\"",
            $"</api/contributions/{id:D}/remove>; rel=\"{Rel("remove")}\""
        };
        if (codifiable) links.Add($"</api/contributions/{id:D}/codifications>; rel=\"{Rel("codifications")}\"");
        return links;
    }

    public static void Append(HttpContext context, IReadOnlyList<string> links)
    {
        foreach (var link in links) context.Response.Headers.Append("Link", link);
    }
}
