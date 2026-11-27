using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Impact.Render;

namespace RuntimeKnowledge.Api;

/// <summary>Signed-in impact search, path traces and C4 views over the active snapshot's model (B15, B16, Q2).</summary>
internal static class ImpactRoutes
{
    private const string Impact = "Impact";
    private const int PageSize = 100;

    public static void MapImpactRoutes(this IEndpointRouteBuilder app)
    {
        var impact = app.MapGroup("/api/impact").RequireAuthorization(Identity.ReadPolicy);
        impact.MapPost("/search", async (ImpactSearchRequest request, ImpactModelCache cache, HttpContext context, CancellationToken token) =>
            {
                var result = await SearchAsync(request, cache, token);
                WorkspaceLinks.Append(context, WorkspaceLinks.ImpactSearch);
                if (result.NextCursor is not null) WorkspaceLinks.Append(context, WorkspaceLinks.ImpactSearchNext);
                return result;
            })
            .WithSummary("Search the active snapshot for impact leads").WithTags(Impact)
            .ProducesWorkspaceErrors("invalidImpactRequest", "invalidCursor", "snapshotChanged", "impactUnavailable", "snapshotUnavailable")
            .WithMetadata(new RequestExamples([("firstPage", OpenApiExamples.ImpactSearch), ("nextPage", OpenApiExamples.ImpactSearchNextPage)]));
        impact.MapPost("/paths", async (ImpactPathsRequest request, ImpactModelCache cache, HttpContext context, CancellationToken token) =>
            {
                var (snapshotId, model) = await cache.GetAsync(request.SnapshotId, token);
                var starts = (request.Starts ?? []).Select(k => (k.SourceId, k.NodeIri)).ToList();
                var result = Engine(() => Paths.Read(model, starts, request.MaxDepth ?? 4));
                AddCitedLines(model, result);
                WorkspaceLinks.Append(context, WorkspaceLinks.ImpactPaths);
                return Map<ImpactPathsResult>(result, snapshotId);
            })
            .WithSummary("Trace impact paths from supported leads").WithTags(Impact)
            .ProducesWorkspaceErrors("invalidImpactRequest", "snapshotChanged", "impactUnavailable", "snapshotUnavailable")
            .WithMetadata(new RequestExample(OpenApiExamples.ImpactPaths));
        impact.MapPost("/views", async (ImpactViewRequest request, ImpactModelCache cache, HttpContext context, CancellationToken token) =>
            {
                var (snapshotId, model) = await cache.GetAsync(request.SnapshotId, token);
                if (request.Focus is null || string.IsNullOrWhiteSpace(request.Level))
                    throw new WorkspaceException("invalidImpactRequest", "focus and level required");
                var view = Engine(() => Views.Project(model, (request.Focus.SourceId, request.Focus.NodeIri), request.Level));
                var (_, markdown) = ViewMarkdown.Render(view);
                WorkspaceLinks.Append(context, WorkspaceLinks.ImpactViews);
                return new C4ViewResult(snapshotId, view, markdown);
            })
            .WithSummary("Project a C4 view of a mapped node").WithTags(Impact)
            .ProducesWorkspaceErrors("invalidImpactRequest", "snapshotChanged", "impactUnavailable", "snapshotUnavailable")
            .WithMetadata(new RequestExample(OpenApiExamples.ImpactView));
    }

    private static async Task<ImpactSearchResult> SearchAsync(ImpactSearchRequest request, ImpactModelCache cache, CancellationToken token)
    {
        var query = request.Query ?? "";
        var queryDigest = EcmaJson.Sha256Hex(query + "\u0000" + (request.ExactIri ?? ""));
        var after = request.Cursor is null ? null : DecodeCursor(request.Cursor, queryDigest);
        var (snapshotId, model) = await cache.GetAsync(after?.Snapshot ?? request.SnapshotId, token);

        var result = Engine(() => Candidates.Find(model, query, request.ExactIri));
        var leads = result["leads"]!.AsArray().Select(l => l!.AsObject()).ToList();
        var remaining = after is null ? leads : leads.Where(l => Compare(LeadKey(l), after.Key) > 0).ToList();
        var page = remaining.Take(PageSize).ToList();
        result["leads"] = new JsonArray(page.Select(l => (JsonNode)l.DeepClone()).ToArray());

        var mapped = Map<ImpactSearchResult>(result, snapshotId);
        return remaining.Count > PageSize
            ? mapped with { NextCursor = EncodeCursor(snapshotId, queryDigest, LeadKey(page[^1])) }
            : mapped;
    }

    private sealed record SearchKey(int Rank, string SourceId, string NodeIri, string Kind);
    private sealed record SearchCursor(string Snapshot, SearchKey Key);

    private static SearchKey LeadKey(JsonObject lead) => new(
        lead["matchKind"]!.GetValue<string>() switch { "exact-iri" => 0, "phrase" => 1, _ => 2 },
        lead["key"]!["sourceId"]!.GetValue<string>(), lead["key"]!["nodeIri"]!.GetValue<string>(), lead["kind"]!.GetValue<string>());

    private static int Compare(SearchKey a, SearchKey b)
    {
        var cmp = a.Rank.CompareTo(b.Rank);
        if (cmp == 0) cmp = string.CompareOrdinal(a.SourceId, b.SourceId);
        if (cmp == 0) cmp = string.CompareOrdinal(a.NodeIri, b.NodeIri);
        if (cmp == 0) cmp = string.CompareOrdinal(a.Kind, b.Kind);
        return cmp;
    }

    private static string EncodeCursor(string snapshotId, string queryDigest, SearchKey key)
    {
        var body = EcmaJson.Stringify(new JsonObject
        {
            ["v"] = 1, ["s"] = snapshotId, ["q"] = queryDigest,
            ["k"] = new JsonArray(key.Rank, key.SourceId, key.NodeIri, key.Kind)
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(body)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // A cursor is bound to its query; a malformed one or one for another query is refused (ADRs 0019, 0020).
    private static SearchCursor DecodeCursor(string cursor, string queryDigest)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var body = JsonNode.Parse(Convert.FromBase64String(base64))!.AsObject();
            var k = body["k"]!.AsArray();
            if (body["v"]!.GetValue<int>() != 1 || body["q"]!.GetValue<string>() != queryDigest || k.Count != 4)
                throw new FormatException();
            return new SearchCursor(body["s"]!.GetValue<string>(), new SearchKey(k[0]!.GetValue<int>(),
                k[1]!.GetValue<string>(), k[2]!.GetValue<string>(), k[3]!.GetValue<string>()));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or NullReferenceException or ArgumentException)
        {
            throw new WorkspaceException("invalidCursor", "The cursor is not valid for this search. Start again from the first page.");
        }
    }

    // B28, B38: every cited evidence record carries its lines in the workspace. Unreadable text is never
    // replaced; its gap is added once (B27).
    private static void AddCitedLines(ImpactModel model, JsonObject result)
    {
        var gaps = result["gaps"]!.AsArray();
        var known = gaps.Select(g => EcmaJson.Stringify(g)).ToHashSet(StringComparer.Ordinal);
        foreach (var step in result["paths"]!.AsArray().SelectMany(p => p!["steps"]!.AsArray()).Select(s => s!.AsObject()))
        {
            if (step["evidence"] is not JsonArray evidence || step["subject"]?["sourceId"]?.GetValue<string>() is not { } sourceId)
                continue;
            foreach (var record in evidence.Select(e => e!.AsObject()))
            {
                var cited = EvidenceText.Read(model, sourceId, record);
                record["lines"] = cited.Lines is null ? null : new JsonArray(cited.Lines.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
                if (cited.Lines is null && cited.Gap is not null && known.Add(EcmaJson.Stringify(cited.Gap)))
                    gaps.Add(cited.Gap.DeepClone());
            }
        }
    }

    private static T Map<T>(JsonObject engineResult, string snapshotId)
    {
        var body = new JsonObject { ["snapshotId"] = snapshotId };
        foreach (var (key, value) in engineResult)
            body[key == "snapshotId" ? "pinned" : key] = value?.DeepClone();
        body["masterList"] ??= new JsonObject { ["status"] = "none" };
        body["notFollowed"] ??= new JsonArray();
        return body.Deserialize<T>(WorkspaceJson.Options)!;
    }

    private static JsonObject Engine(Func<JsonObject> run)
    {
        try { return run(); }
        catch (ImpactException ex) { throw new WorkspaceException("invalidImpactRequest", ex.Message, ex); }
    }
}
