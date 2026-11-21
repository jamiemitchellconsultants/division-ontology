using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using RuntimeKnowledge.Tests.OpenApi;
using Xunit;

namespace RuntimeKnowledge.Tests;

/// <summary>
/// User-phase acceptance for traversal cursor pagination (#119): one test per behaviour in
/// docs/features/traversal-cursor-pagination/02-behaviours.md, driven through the HTTP API wherever the
/// behaviour is observable from outside. B6, B16, B17 and B19 need the test-only examination seam.
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class TraversalPaginationAcceptanceTests
{
    private const string Route = "/api/traverse";
    private const string Edge = "https://fixture.test/t#e";

    private sealed record Page(HttpStatusCode Status, TraversalResult? Body, JsonElement Raw, IReadOnlyList<string> Links);

    private sealed class Harness : IAsyncDisposable
    {
        public required Scenario Scenario { get; init; }
        public required ApiFixture Api { get; init; }
        public required HttpClient Reader { get; init; }
        public required HttpClient Manager { get; init; }

        public static async Task<Harness> OpenAsync(string traversalFixture)
        {
            var scenario = await Scenario.OpenAsync();
            await scenario.ActivateTraversalAsync(traversalFixture);
            var api = new ApiFixture(scenario, "Entra");
            return new Harness
            {
                Scenario = scenario, Api = api,
                Reader = api.ClientWithToken(["ReadKnowledgeGraph"]),
                Manager = api.ClientWithToken(["ManageKnowledgeGraph"])
            };
        }

        public async Task<Page> PostAsync(TraversalRequest request)
        {
            using var response = await Reader.PostAsJsonAsync(Route, request, WorkspaceJson.Options);
            var raw = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            var links = response.Headers.TryGetValues("Link", out var values) ? values.ToArray() : [];
            var body = response.IsSuccessStatusCode ? raw.Deserialize<TraversalResult>(WorkspaceJson.Options) : null;
            return new Page(response.StatusCode, body, raw, links);
        }

        public async Task<List<TraversalResult>> PageAllAsync(TraversalRequest request)
        {
            var pages = new List<TraversalResult>();
            string? cursor = null;
            do
            {
                var page = await PostAsync(request with { Cursor = cursor });
                Assert.Equal(HttpStatusCode.OK, page.Status);
                pages.Add(page.Body!);
                cursor = page.Body!.NextCursor;
            } while (cursor is not null);
            return pages;
        }

        public async Task<ContributionResult> CreateRelationshipAsync(PublishedKey from, PublishedKey to, string label)
        {
            using var response = await Manager.PostAsJsonAsync("/api/contributions/relationships",
                new CreateRelationship(from, to, RelationshipMeaning.Custom(label, "Acceptance fixture edge.")), WorkspaceJson.Options);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ContributionResult>(WorkspaceJson.Options))!;
        }

        public async Task<ContributionResult> SendAsync(HttpMethod method, string path, object body, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: WorkspaceJson.Options) };
            using var response = await Manager.SendAsync(request);
            Assert.Equal(expected, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ContributionResult>(WorkspaceJson.Options))!;
        }

        public async ValueTask DisposeAsync()
        {
            Reader.Dispose();
            Manager.Dispose();
            await Api.DisposeAsync();
        }
    }

    private static string Signature(TraversalPath path) => string.Join(">",
        path.Steps.Select(step => step.Key?.Identifier ?? step.RuntimeId!.Value.ToString("D")));

    private static string[] Signatures(IEnumerable<TraversalResult> pages) =>
        pages.SelectMany(page => page.Paths).Select(Signature).ToArray();

    private static string[] PublishedEdges(int count) =>
        Enumerable.Range(0, count).Select(i => $"{Edge}{i:000}").ToArray();

    private static void AssertRestartError(Page page, HttpStatusCode status, string code)
    {
        Assert.Equal(status, page.Status);
        Assert.Equal(code, page.Raw.GetProperty("code").GetString());
        Assert.Contains("Restart the traversal from the first page", page.Raw.GetProperty("title").GetString());
        Assert.False(page.Raw.TryGetProperty("paths", out _));
    }

    private static async Task<long> JournalHeadAsync(Scenario s)
    {
        await using var connection = new Npgsql.NpgsqlConnection(s.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT COALESCE(MAX(position), 0) FROM workspace_journal.events", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> PublishedStateDigestAsync(Scenario s)
    {
        await using var connection = new Npgsql.NpgsqlConnection(s.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("""
            SELECT md5(concat_ws('|',
                (SELECT string_agg(t::text, ',' ORDER BY t::text) FROM workspace_projection.state t),
                (SELECT string_agg(t::text, ',' ORDER BY t::text) FROM workspace_projection.published_records t),
                (SELECT string_agg(t::text, ',' ORDER BY t::text) FROM workspace_projection.contributions t)))
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact, Trait("Behaviour", "B1")]
    public async Task B01_BusyOneHopTraversalReachesEveryPathExactlyOnce()
    {
        await using var h = await Harness.OpenAsync("busy");
        var runtime = await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf000"), "beyond page one");

        var pages = await h.PageAllAsync(new TraversalRequest(h.Scenario.Hub, MaxDepth: 1));

        Assert.Equal(2, pages.Count);
        Assert.Equal(100, pages[0].Paths.Count);
        Assert.All(pages, page => Assert.True(page.Paths.Count <= 100));
        var signatures = Signatures(pages);
        Assert.Equal([.. PublishedEdges(150), runtime.Id.ToString("D")], signatures);
        Assert.DoesNotContain(runtime.Id.ToString("D"), pages[0].Paths.Select(Signature));
        Assert.Null(pages[^1].NextCursor);
    }

    [Fact, Trait("Behaviour", "B2")]
    public async Task B02_MultiHopPagesMatchTheUnpagedSequenceAcrossABranchBoundary()
    {
        await using var h = await Harness.OpenAsync("branches");
        var expected = new List<string>();
        for (var m = 0; m < 2; m++) expected.Add($"https://fixture.test/t#h-m{m}");
        for (var m = 0; m < 2; m++)
            for (var sub = 0; sub < 3; sub++)
                expected.Add($"https://fixture.test/t#h-m{m}>https://fixture.test/t#m{m}-s{sub}");
        for (var m = 0; m < 2; m++)
            for (var sub = 0; sub < 3; sub++)
                for (var l = 0; l < 30; l++)
                    expected.Add($"https://fixture.test/t#h-m{m}>https://fixture.test/t#m{m}-s{sub}>https://fixture.test/t#s{m}{sub}-l{l:00}");

        var pages = await h.PageAllAsync(new TraversalRequest(h.Scenario.Hub, MaxDepth: 3));

        Assert.Equal(expected, Signatures(pages));
        var lastOnFirstPage = Signature(pages[0].Paths[^1]);
        var firstOnSecondPage = Signature(pages[1].Paths[0]);
        Assert.Equal(lastOnFirstPage.Split('>')[1], firstOnSecondPage.Split('>')[1]);
    }

    [Fact, Trait("Behaviour", "B3")]
    public async Task B03_IncomingAndBothDirectionsAndUnavailableEndpointsPageWithoutLossOrReorder()
    {
        await using var h = await Harness.OpenAsync("busy");
        var store = new ContributionStore(h.Scenario.Db);
        var incoming = new List<string>();
        for (var i = 0; i < 110; i++)
            incoming.Add((await store.ApplyAsync(h.Scenario.Actor, new CreateRelationship(Scenario.TraversalKey($"leaf{i:000}"),
                h.Scenario.Hub, RelationshipMeaning.Custom($"in{i}", "Incoming fixture edge.")), CancellationToken.None)).Id.ToString("D"));

        var incomingPages = await h.PageAllAsync(new TraversalRequest(h.Scenario.Hub, "incoming", 1));
        Assert.True(incomingPages.Count >= 2);
        Assert.Equal(incoming.Order(StringComparer.Ordinal), Signatures(incomingPages).Order(StringComparer.Ordinal));
        Assert.Equal(incoming.Count, Signatures(incomingPages).Distinct().Count());

        var bothPages = await h.PageAllAsync(new TraversalRequest(h.Scenario.Hub, "both", 1));
        var both = Signatures(bothPages);
        Assert.Equal(260, both.Length);
        Assert.Equal(both.Length, both.Distinct().Count());
        Assert.Equal(PublishedEdges(150), both.Take(150));

        var busySnapshot = (await h.PostAsync(new TraversalRequest(h.Scenario.Hub, MaxDepth: 1))).Body!.SnapshotId;
        var gone = new List<string>();
        for (var i = 0; i < 120; i++)
            gone.Add((await store.ApplyAsync(h.Scenario.Actor, new CreateRelationship(h.Scenario.Hub, Scenario.TraversalKey("leaf149"),
                RelationshipMeaning.Custom($"gone{i}", "Endpoint removed by the next snapshot.")), CancellationToken.None)).Id.ToString("D"));
        await h.Scenario.ActivateTraversalAsync("trimmed");

        var withUnavailable = await h.PageAllAsync(new TraversalRequest(h.Scenario.Hub, MaxDepth: 1, IncludeUnavailable: true));
        Assert.True(withUnavailable.Count >= 3);
        var unavailableSteps = withUnavailable.Select(page => page.Paths.SelectMany(path => path.Steps)
            .Where(step => step.EndpointUnavailable).ToArray()).ToArray();
        Assert.All(unavailableSteps.Skip(1), steps => Assert.NotEmpty(steps));
        Assert.All(unavailableSteps.SelectMany(steps => steps), step => Assert.Equal(busySnapshot, step.LastKnownSnapshotId));
        var unavailableSignatures = Signatures(withUnavailable);
        Assert.Equal(PublishedEdges(99), unavailableSignatures.Take(99));
        Assert.Equal(gone.Order(StringComparer.Ordinal),
            unavailableSignatures.Skip(99).Order(StringComparer.Ordinal));

        var available = await h.PageAllAsync(new TraversalRequest(h.Scenario.Hub, MaxDepth: 1));
        Assert.Equal(PublishedEdges(99), Signatures(available));
        Assert.DoesNotContain(available.SelectMany(page => page.Paths).SelectMany(path => path.Steps), step => step.EndpointUnavailable);
    }

    [Fact, Trait("Behaviour", "B4")]
    public async Task B04_PathOrderIsBreadthFirstWithPublishedStepsBeforeWorkspaceSteps()
    {
        await using var h = await Harness.OpenAsync("busy");
        var first = await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf010"), "first workspace");
        var second = await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf020"), "second workspace");
        var request = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);

        var paged = Signatures(await h.PageAllAsync(request));
        var again = Signatures(await h.PageAllAsync(request));

        Assert.Equal(paged, again);
        Assert.Equal(PublishedEdges(150), paged.Take(150));
        Assert.Equal(new[] { first.Id, second.Id }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal),
            paged.Skip(150).Order(StringComparer.Ordinal));
    }

    [Fact, Trait("Behaviour", "B5")]
    public async Task B05_FirstPageKeepsTodaysShapeAndAddsOnlyNextCursor()
    {
        await using (var busy = await Harness.OpenAsync("busy"))
        {
            var page = await busy.PostAsync(new TraversalRequest(busy.Scenario.Hub, MaxDepth: 1));
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Equal(PublishedEdges(100), page.Body!.Paths.Select(Signature));
            Assert.True(page.Body.Truncated);
            Assert.False(string.IsNullOrWhiteSpace(page.Body.NextCursor));
            Assert.Equal(["entities", "gaps", "nextCursor", "paths", "snapshotId", "truncated"],
                page.Raw.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }

        await using (var exact = await Harness.OpenAsync("exact"))
        {
            var first = await exact.PostAsync(new TraversalRequest(exact.Scenario.Hub, MaxDepth: 2));
            Assert.Equal(100, first.Body!.Paths.Count);
            Assert.True(first.Body.Truncated);
            Assert.NotNull(first.Body.NextCursor);
            var settled = await exact.PostAsync(new TraversalRequest(exact.Scenario.Hub, MaxDepth: 2, Cursor: first.Body.NextCursor));
            Assert.Empty(settled.Body!.Paths);
            Assert.False(settled.Body.Truncated);
        }

        await using (var small = await Harness.OpenAsync("trimmed"))
        {
            var page = await small.PostAsync(new TraversalRequest(small.Scenario.Hub, MaxDepth: 1));
            Assert.Equal(99, page.Body!.Paths.Count);
            Assert.False(page.Body.Truncated);
            Assert.Equal(["entities", "gaps", "paths", "snapshotId", "truncated"],
                page.Raw.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }
    }

    [Fact, Trait("Behaviour", "B6")]
    public async Task B06_TruncatedMeansNotProvenFinishedOnEveryPage()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        var store = new ContributionStore(s.Db);
        for (var i = 0; i < 30; i++)
            await store.ApplyAsync(s.Actor, new CreateRelationship(s.Hub, Scenario.TraversalKey("leaf149"),
                RelationshipMeaning.Custom($"gone{i}", "Filtered after refresh.")), CancellationToken.None);
        await s.ActivateTraversalAsync("trimmed");

        var pages = new List<TraversalResult>();
        string? cursor = null;
        do
        {
            var page = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1, Cursor: cursor),
                CancellationToken.None, new TraversalCounters { Budget = 10 });
            pages.Add(page);
            Assert.Equal(page.NextCursor is not null, page.Truncated);
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Contains(pages.SkipLast(1), page => page.Paths.Count == 0 && page.Truncated);
        Assert.False(pages[^1].Truncated);
        Assert.Null(pages[^1].NextCursor);
        Assert.Equal(PublishedEdges(99), Signatures(pages));
    }

    [Fact, Trait("Behaviour", "B7")]
    public async Task B07_EachPageCarriesContextForEveryEndpointItTouches()
    {
        await using var h = await Harness.OpenAsync("busy");
        var annotated = Scenario.TraversalKey("leaf120");
        using (var note = await h.Manager.PostAsJsonAsync("/api/contributions/annotations",
                   new CreateAnnotation(annotated, "businessExplanation", "Only reached on page two.")))
            Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf005"), "workspace endpoint");

        var pages = await h.PageAllAsync(new TraversalRequest(h.Scenario.Hub, MaxDepth: 1));

        foreach (var page in pages)
        {
            var endpoints = page.Paths.SelectMany(path => path.Steps).SelectMany(step => new[] { step.From, step.To }).ToHashSet();
            var entities = page.Entities.ToDictionary(entity => entity.Key);
            Assert.All(endpoints, key => Assert.True(entities.ContainsKey(key), $"page lacks context for {key.Identifier}"));
            Assert.All(entities.Values, entity =>
            {
                Assert.False(string.IsNullOrEmpty(entity.Label));
                Assert.NotNull(entity.Provenance);
                Assert.True(entity.Available);
            });
        }
        Assert.Contains(pages[1].Entities, entity => entity.Key == annotated &&
            entity.Annotations.Any(a => a.Annotation?.Text == "Only reached on page two."));
        Assert.DoesNotContain(pages[0].Entities, entity => entity.Key == annotated);
        Assert.Contains(pages[1].Entities, entity => entity.Key == Scenario.TraversalKey("leaf005"));
    }

    [Fact, Trait("Behaviour", "B8")]
    public async Task B08_CursorIsBoundToItsTraversalParameters()
    {
        await using var h = await Harness.OpenAsync("busy");
        var original = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);
        var cursor = (await h.PostAsync(original)).Body!.NextCursor;

        TraversalRequest[] changed =
        [
            original with { Start = Scenario.TraversalKey("leaf000") },
            original with { Start = Scenario.TraversalKey("never-published") },
            original with { Direction = "both" },
            original with { MaxDepth = 2 },
            original with { IncludeUnavailable = true }
        ];
        foreach (var request in changed)
            AssertRestartError(await h.PostAsync(request with { Cursor = cursor }), HttpStatusCode.UnprocessableEntity, "invalidCursor");

        await h.Scenario.ActivateAsync("initial");
        AssertRestartError(await h.PostAsync(original with { Cursor = cursor }), HttpStatusCode.Conflict, "staleCursor");
    }

    [Fact, Trait("Behaviour", "B9")]
    public async Task B09_MalformedCursorsFailAsInvalidCursorWithoutResults()
    {
        await using var h = await Harness.OpenAsync("busy");
        var request = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);
        var cursor = (await h.PostAsync(request)).Body!.NextCursor!;

        string[] malformed =
        [
            "not a cursor",
            cursor[..(cursor.Length / 2)],
            Convert.ToBase64String("{\"version\":1}"u8.ToArray()),
            new string('A', TraversalCursor.MaxCursorChars + 4)
        ];
        foreach (var value in malformed)
            AssertRestartError(await h.PostAsync(request with { Cursor = value }), HttpStatusCode.UnprocessableEntity, "invalidCursor");
    }

    [Fact, Trait("Behaviour", "B10")]
    public async Task B10_SnapshotRefreshMakesTheCursorStale()
    {
        await using var h = await Harness.OpenAsync("busy");
        var request = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);
        var cursor = (await h.PostAsync(request)).Body!.NextCursor;

        await h.Scenario.ActivateTraversalAsync("trimmed");

        AssertRestartError(await h.PostAsync(request with { Cursor = cursor }), HttpStatusCode.Conflict, "staleCursor");
        Assert.Equal(HttpStatusCode.OK, (await h.PostAsync(request)).Status);
    }

    [Fact, Trait("Behaviour", "B11")]
    public async Task B11_AnyWorkspaceContributionChangeMakesTheCursorStale()
    {
        await using var h = await Harness.OpenAsync("busy");
        var request = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);
        var far = Scenario.TraversalKey("leaf149");
        ContributionResult? annotation = null;
        ContributionResult? relationship = null;

        var changes = new (string Name, Func<Task> Apply)[]
        {
            ("annotation created", async () => annotation = await h.SendAsync(HttpMethod.Post, "/api/contributions/annotations",
                new CreateAnnotation(far, "businessExplanation", "Unrelated note."), HttpStatusCode.Created)),
            ("annotation edited", async () => annotation = await h.SendAsync(HttpMethod.Put, $"/api/contributions/{annotation!.Id:D}",
                new EditContributionRequest(annotation.Version, "businessExplanation", "Edited note.", null, null, null), HttpStatusCode.OK)),
            ("annotation removed", async () => await h.SendAsync(HttpMethod.Post, $"/api/contributions/{annotation!.Id:D}/remove",
                new RemoveContributionRequest(annotation.Version), HttpStatusCode.OK)),
            ("relationship created", async () => relationship = await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf000"), "codify me")),
            ("relationship codified", async () => relationship = await h.SendAsync(HttpMethod.Post, $"/api/contributions/{relationship!.Id:D}/codifications",
                new CodificationRequest(relationship.Version, new PublishedKey("fixture/T", "assertion", $"{Edge}000")), HttpStatusCode.OK))
        };

        foreach (var (name, apply) in changes)
        {
            var cursor = (await h.PostAsync(request)).Body!.NextCursor;
            await apply();
            var page = await h.PostAsync(request with { Cursor = cursor });
            Assert.True(page.Status == HttpStatusCode.Conflict, $"{name}: expected 409, got {(int)page.Status}");
            AssertRestartError(page, HttpStatusCode.Conflict, "staleCursor");
        }
    }

    [Fact, Trait("Behaviour", "B12")]
    public async Task B12_ValidCursorsContinueExactlyOnceToTheFinalPage()
    {
        await using var h = await Harness.OpenAsync("wide");
        var request = new TraversalRequest(h.Scenario.Hub, MaxDepth: 2);
        var expected = new List<string>();
        for (var m = 0; m < 40; m++) expected.Add($"https://fixture.test/t#h-m{m:00}");
        for (var m = 0; m < 40; m++)
            for (var l = 0; l < 150; l++)
                expected.Add($"https://fixture.test/t#h-m{m:00}>https://fixture.test/t#m{m:00}-l{l:000}");

        var pages = await h.PageAllAsync(request);

        Assert.True(pages.Count > 60);
        Assert.Equal(expected, Signatures(pages));
        var middle = (await h.PostAsync(request)).Body!.NextCursor;
        var once = await h.PostAsync(request with { Cursor = middle });
        var twice = await h.PostAsync(request with { Cursor = middle });
        Assert.Equal(once.Body!.Paths.Select(Signature), twice.Body!.Paths.Select(Signature));
    }

    [Fact, Trait("Behaviour", "B13")]
    public async Task B13_NextLinkIsPresentExactlyWhileAnotherPageExists()
    {
        await using var h = await Harness.OpenAsync("busy");
        await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf000"), "beyond page one");
        var request = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);
        using var specClient = h.Api.CreateClient();
        var relations = JsonNode.Parse(await specClient.GetStringAsync("/api/openapi/v1.json"))!["x-agent-discovery"]!["linkRelations"]!;
        Assert.Contains("traverseGraph", (string)relations["next"]!);

        var first = await h.PostAsync(request);
        Assert.Contains("</api/traverse>; rel=\"next\"", first.Links);
        Assert.NotNull(first.Body!.NextCursor);

        var last = await h.PostAsync(request with { Cursor = first.Body.NextCursor });
        Assert.Null(last.Body!.NextCursor);
        Assert.DoesNotContain(last.Links, link => link.Contains("rel=\"next\"", StringComparison.Ordinal));
        Assert.Contains(last.Links, link => link.Contains("#link-relations/entityDetail", StringComparison.Ordinal));
    }

    [Fact, Trait("Behaviour", "B14")]
    public async Task B14_ServedAndCommittedSpecsDocumentPagingAndKeepExistingLinks()
    {
        await using var h = await Harness.OpenAsync("busy");
        using var client = h.Api.CreateClient();
        var served = JsonNode.Parse(await client.GetStringAsync("/api/openapi/v1.json"))!;
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(RepositoryPaths.Spec))!;

        foreach (var spec in new[] { served, committed })
        {
            var schemas = spec["components"]!["schemas"]!;
            Assert.NotNull(schemas["TraversalRequest"]!["properties"]!["cursor"]);
            Assert.NotNull(schemas["TraversalResult"]!["properties"]!["nextCursor"]);
            var op = spec["paths"]![Route]!["post"]!;
            Assert.Contains("invalidCursor", op["responses"]!["422"]!["x-workspace-codes"]!.AsArray().Select(n => (string)n!));
            Assert.Contains("staleCursor", op["responses"]!["409"]!["x-workspace-codes"]!.AsArray().Select(n => (string)n!));
            var examples = op["requestBody"]!["content"]!["application/json"]!["examples"]!.AsObject();
            Assert.Null(examples["firstPage"]!["value"]!["cursor"]);
            Assert.NotNull(examples["nextPage"]!["value"]!["cursor"]);
            var links = op["responses"]!["200"]!["links"]!.AsObject().Select(link => link.Key).ToArray();
            Assert.Contains("next", links);
            Assert.Contains("entityDetail", links);
            Assert.Contains("relationshipDetail", links);
            Assert.Equal("traverseGraph", (string)op["responses"]!["200"]!["links"]!["next"]!["operationId"]!);
        }
    }

    [Fact, Trait("Behaviour", "B15")]
    public async Task B15_AClientFollowingTheNextLinkReachesEveryPage()
    {
        await using var h = await Harness.OpenAsync("busy");
        var runtime = await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf000"), "beyond page one");
        var original = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);

        var collected = new List<string>();
        var target = Route;
        string? cursor = null;
        var calls = 0;
        while (target is not null)
        {
            using var response = await h.Reader.PostAsJsonAsync(target, original with { Cursor = cursor }, WorkspaceJson.Options);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.Content.ReadFromJsonAsync<TraversalResult>(WorkspaceJson.Options))!;
            collected.AddRange(page.Paths.Select(Signature));
            cursor = page.NextCursor;
            var next = response.Headers.TryGetValues("Link", out var links)
                ? links.FirstOrDefault(link => link.EndsWith("rel=\"next\"", StringComparison.Ordinal))
                : null;
            target = next?[(next.IndexOf('<') + 1)..next.IndexOf('>')];
            Assert.Equal(target is not null, cursor is not null);
            Assert.True(++calls <= 3);
        }

        Assert.Equal([.. PublishedEdges(150), runtime.Id.ToString("D")], collected);
    }

    [Fact, Trait("Behaviour", "B16")]
    public async Task B16_LatePagesCostTheSameAsEarlyPages()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("wide");
        var counts = new List<int>();
        string? cursor = null;
        do
        {
            var counters = new TraversalCounters();
            var page = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 2, Cursor: cursor), CancellationToken.None, counters);
            counts.Add(counters.ExaminedSteps);
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.True(counts.Count > 60);
        Assert.All(counts, count => Assert.InRange(count, 0, 1000));
        Assert.Equal(40 + 40 * 150, counts.Sum());
        Assert.True(counts.Skip(counts.Count / 2).Max() <= counts.Take(counts.Count / 2).Max() + 101,
            $"late pages examined more than early pages: {string.Join(",", counts)}");
    }

    [Fact, Trait("Behaviour", "B17")]
    public async Task B17_FilteredUnavailableRelationshipsDoNotGrowPerPageWork()
    {
        await using var s = await Scenario.OpenAsync();
        var store = new ContributionStore(s.Db);
        var created = 0;
        foreach (var filtered in new[] { 100, 1200 })
        {
            await s.ActivateTraversalAsync("busy");
            for (; created < filtered; created++)
                await store.ApplyAsync(s.Actor, new CreateRelationship(s.Hub, Scenario.TraversalKey("leaf149"),
                    RelationshipMeaning.Custom($"gone{created}", "Filtered after refresh.")), CancellationToken.None);
            await s.ActivateTraversalAsync("trimmed");

            var pages = new List<TraversalResult>();
            var examined = new List<int>();
            string? cursor = null;
            do
            {
                var counters = new TraversalCounters();
                var page = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1, Cursor: cursor), CancellationToken.None, counters);
                pages.Add(page);
                examined.Add(counters.ExaminedSteps);
                cursor = page.NextCursor;
            } while (cursor is not null);

            Assert.All(examined, count => Assert.InRange(count, 0, 1000));
            Assert.Equal(PublishedEdges(99), Signatures(pages));
            Assert.Equal(99, pages[0].Paths.Count);
        }
    }

    [Fact, Trait("Behaviour", "B18")]
    public async Task B18_PagingAndRejectedCursorsWriteNothing()
    {
        await using var h = await Harness.OpenAsync("busy");
        await h.CreateRelationshipAsync(h.Scenario.Hub, Scenario.TraversalKey("leaf000"), "existing workspace edge");
        var request = new TraversalRequest(h.Scenario.Hub, MaxDepth: 1);
        var journal = await JournalHeadAsync(h.Scenario);
        var state = await PublishedStateDigestAsync(h.Scenario);
        var turtle = Directory.EnumerateFiles(RepositoryPaths.Root, "*.ttl", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"))
            .ToDictionary(path => path, File.GetLastWriteTimeUtc);

        var pages = await h.PageAllAsync(request);
        var cursor = pages[0].NextCursor;
        await h.PostAsync(request with { Cursor = "garbage" });
        await h.PostAsync(request with { MaxDepth = 2, Cursor = cursor });

        Assert.Equal(journal, await JournalHeadAsync(h.Scenario));
        Assert.Equal(state, await PublishedStateDigestAsync(h.Scenario));
        Assert.All(turtle, file => Assert.Equal(file.Value, File.GetLastWriteTimeUtc(file.Key)));
    }

    [Fact, Trait("Behaviour", "B19")]
    public async Task B19_OversizedFrontierFailsAsInvalidTraversalWithRestartAdvice()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("wide");
        var counters = new TraversalCounters { MaxCursorChars = 200 };

        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 2), CancellationToken.None, counters));

        Assert.Equal("invalidTraversal", error.Code);
        Assert.Contains("Restart with a smaller maxDepth or a more specific start entity", error.Message);
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(RepositoryPaths.Spec))!;
        var op = committed["paths"]![Route]!["post"]!;
        Assert.Contains("invalidTraversal", op["responses"]!["422"]!["x-workspace-codes"]!.AsArray().Select(n => (string)n!));
    }
}
