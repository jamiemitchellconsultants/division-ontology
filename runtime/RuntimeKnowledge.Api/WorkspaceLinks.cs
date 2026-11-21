using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Api;

/// <summary>RFC 8288 Link headers: the runtime half of the spec's HATEOAS links.</summary>
internal static class WorkspaceLinks
{
    public static readonly IReadOnlyList<string> Health =
    [
        $"<{DocsUi.SpecPath}>; rel=\"service-desc\"",
        "</api/docs>; rel=\"service-doc\"",
        "</api/session>; rel=\"session\""
    ];

    public static readonly IReadOnlyList<string> Session =
    [
        "</api/inventory>; rel=\"inventory\"",
        "</api/history>; rel=\"history\"",
        $"<{DocsUi.SpecPath}>; rel=\"service-desc\""
    ];

    public static readonly IReadOnlyList<string> Inventory =
    [
        "</api/search>; rel=\"search\""
    ];

    public static readonly IReadOnlyList<string> EntityDetail =
    [
        "</api/traverse>; rel=\"traverse\"",
        "</api/contributions/annotations>; rel=\"createAnnotation\""
    ];

    public static readonly IReadOnlyList<string> RelationshipDetail =
    [
        "</api/entities/detail>; rel=\"entityDetail\""
    ];

    public static readonly IReadOnlyList<string> Search =
    [
        "</api/entities/detail>; rel=\"entityDetail\""
    ];

    public static readonly IReadOnlyList<string> Traverse =
    [
        "</api/entities/detail>; rel=\"entityDetail\"",
        "</api/relationships/detail>; rel=\"relationshipDetail\""
    ];

    public static IReadOnlyList<string> NextPage(string path, string? nextCursor) =>
        nextCursor is null ? [] : [$"<{path}?cursor={Uri.EscapeDataString(nextCursor)}>; rel=\"next\""];

    public static IReadOnlyList<string> History(HistoryPage page) =>
        page.Entries.Count == 0 ? [] :
        [$"</api/contributions/{page.Entries[0].ContributionId:D}>; rel=\"editContribution\""];

    public static IReadOnlyList<string> ContributionHistory(Guid id) =>
        [$"</api/history?contributionId={id:D}>; rel=\"history\""];

    public static IReadOnlyList<string> Contribution(Guid id, bool codifiable)
    {
        var links = new List<string>
        {
            $"</api/history?contributionId={id:D}>; rel=\"history\"",
            $"</api/contributions/{id:D}>; rel=\"edit\"",
            $"</api/contributions/{id:D}/remove>; rel=\"remove\""
        };
        if (codifiable) links.Add($"</api/contributions/{id:D}/codifications>; rel=\"codifications\"");
        return links;
    }

    public static void Append(HttpContext context, IReadOnlyList<string> links)
    {
        foreach (var link in links) context.Response.Headers.Append("Link", link);
    }
}
