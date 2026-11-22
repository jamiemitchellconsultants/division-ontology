using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Components;
using RuntimeKnowledge.Web.Pages;
using RuntimeKnowledge.Web.Services;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class GraphExplorerNavigationTests
{
    private static readonly PublishedKey Ecommerce = new("fixture/ecommerce", "concept", "https://example.test/ecommerce#Order");
    private static readonly PublishedKey Warehouse = new("fixture/warehouse", "node", "https://example.test/warehouse#Order");

    [Fact]
    public void LinksSelectTheExactAvailablePublishedEntity()
    {
        var api = new ReadHandler((request, _) => Task.FromResult(Json(Page(request.Start))));
        using var context = GraphTestContext.Create(api);
        var explore = context.Render<Explore>();
        explore.WaitForElement("input[type=search]").Change("Order");
        explore.Find("form").Submit();
        explore.WaitForAssertion(() => Assert.Equal(
            new[] { WorkspaceLinks.Graph(Ecommerce), WorkspaceLinks.Graph(Warehouse) },
            explore.FindAll("section[aria-label='Search results'] a").Select(link => link.GetAttribute("href"))
                .Where(href => href?.StartsWith("graph/") == true)));

        var entity = context.Render<Entity>(args => args.Add(component => component.Key, WorkspaceLinks.Encode(Warehouse)));
        entity.WaitForElement($"a[href='{WorkspaceLinks.Graph(Warehouse)}']");
        api.EntityAvailable = false;
        entity.Render(args => args.Add(component => component.Key, WorkspaceLinks.Encode(Ecommerce)));
        entity.WaitForAssertion(() => Assert.DoesNotContain(entity.FindAll("a"), link => link.GetAttribute("href")?.StartsWith("graph/") == true));
        entity.Render(args => args.Add(component => component.Key, WorkspaceLinks.Encode(Ecommerce with { Kind = "relationshipType" })));
        entity.WaitForAssertion(() => Assert.DoesNotContain(entity.FindAll("a"), link => link.GetAttribute("href")?.StartsWith("graph/") == true));

        var graph = context.Render<GraphExplorer>(args => args.Add(component => component.Key, "###"));
        graph.WaitForElement("a[href='./']");
        Assert.Empty(api.Requests);
        graph.Render(args => args.Add(component => component.Key, WorkspaceLinks.Encode(Warehouse)));
        graph.WaitForAssertion(() => Assert.Equal(Warehouse, Assert.Single(api.Requests).Start));
    }

    [Fact]
    public void FirstPageAndDirectionChangesUseOneHop()
    {
        var api = new ReadHandler((request, _) => Task.FromResult(Json(Page(request.Start, label: request.Direction))));
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        graph.WaitForAssertion(() => Assert.Equal(new TraversalRequest(Ecommerce, "both", 1, false), Assert.Single(api.Requests)));
        graph.WaitForElement("select[aria-label=Direction]").Change("incoming");
        graph.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { new TraversalRequest(Ecommerce, "both", 1, false), new TraversalRequest(Ecommerce, "incoming", 1, false) }, api.Requests.ToArray());
            Assert.Contains("fixture/warehouse → Order · fixture/ecommerce", graph.Find("li[data-graph-edge]").TextContent);
        });
    }

    [Fact]
    public void EmptyPageKeepsLoadMoreAndFinalPageKeepsEarlierEdges()
    {
        var api = new ReadHandler((request, _) => Task.FromResult(Json(request.Cursor switch
        {
            null => Page(Ecommerce, "first", "Handoff"),
            "first" => Page(Ecommerce, "second"),
            _ => Page(Ecommerce)
        })));
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        Button(graph, "Load more").Click();
        graph.WaitForAssertion(() => Assert.Equal("first", api.Requests.Last().Cursor));
        Button(graph, "Load more").Click();
        graph.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { null, "first", "second" }, api.Requests.Select(request => request.Cursor));
            Assert.All(api.Requests, request => Assert.Equal(new TraversalRequest(Ecommerce, "both", 1, false), request with { Cursor = null }));
            Assert.Contains("Handoff", graph.Find("li[data-graph-edge]").TextContent);
            Assert.DoesNotContain(graph.FindAll("button"), button => button.TextContent.Trim() == "Load more");
        });
    }

    [Theory]
    [InlineData("complete", "No visible connections", false)]
    [InlineData("elsewhere", "No visible connections", false)]
    [InlineData("relevant", "Source coverage is incomplete", false)]
    [InlineData("cursor", "Check more results", true)]
    [InlineData("truncated", "Results are incomplete", false)]
    public void CoverageEmptyAndIncompleteStatesAreIndependent(string situation, string message, bool canContinue)
    {
        var page = Page(Ecommerce, situation == "cursor" ? "next" : null);
        if (situation is "elsewhere" or "relevant")
            page = page with { Gaps = [new CoverageGap("missing", situation == "relevant" ? Ecommerce.SourceId : "fixture/billing", null, "Missing coverage")] };
        if (situation == "truncated")
            page = page with { Truncated = true };
        using var context = GraphTestContext.Create(new ReadHandler((_, _) => Task.FromResult(Json(page))));
        var graph = Render(context);
        graph.WaitForAssertion(() =>
        {
            Assert.Contains(message, graph.Markup);
            Assert.Equal(canContinue, graph.FindAll("button").Any(button => button.TextContent.Trim() == "Load more"));
            Assert.Equal(situation is "complete" or "elsewhere", graph.Markup.Contains("No visible connections", StringComparison.Ordinal));
            Assert.Equal(situation == "elsewhere", graph.Markup.Contains("coverage gaps elsewhere", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void NeighbourRowsInspectWithoutFollowing()
    {
        var api = new ReadHandler((request, _) => Task.FromResult(Json(Page(request.Start, label: "Handoff"))));
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        graph.WaitForElement("button[data-graph-inspect-node]").Click();
        graph.WaitForAssertion(() =>
        {
            Assert.Single(api.Requests);
            Assert.Contains(Ecommerce.SourceId, graph.Find("h1").TextContent);
            Assert.Equal("true", graph.Find("button[data-graph-inspect-node]").GetAttribute("aria-pressed"));
        });
        graph.Find("button[data-graph-follow]").Click();
        graph.WaitForAssertion(() =>
        {
            Assert.Equal(Warehouse, api.Requests.Last().Start);
            Assert.Contains(Warehouse.SourceId, graph.Find("h1").TextContent);
        });
    }

    [Fact]
    public void BackRestoresTheLoadedFrame()
    {
        var api = new ReadHandler((request, _) => Task.FromResult(Json(request.Start == Warehouse
            ? Page(Warehouse)
            : Page(Ecommerce, request.Cursor is null ? "next" : null, request.Cursor is null ? "First" : "Second"))));
        using var context = GraphTestContext.Create(api);
        var diagram = GraphTestContext.SetupDiagram(context);
        var savedViewport = new GraphDiagram.GraphViewport(new(30, 50), 1.75, []);
        diagram.Setup<GraphDiagram.GraphViewport>("captureViewport").SetResult(savedViewport);
        var graph = Render(context);
        graph.WaitForElement("select").Change("incoming");
        Button(graph, "Load more").Click();
        graph.WaitForAssertion(() => Assert.Equal(2, graph.FindAll("li[data-graph-edge]").Count));
        graph.FindAll("button[data-graph-inspect-edge]")[1].Click();
        graph.Find("button[data-graph-follow]").Click();
        graph.WaitForAssertion(() => Assert.Contains(Warehouse.SourceId, graph.Find("h1").TextContent));
        var beforeBack = api.Requests.Count;
        Button(graph, "Back").Click();
        graph.WaitForAssertion(() =>
        {
            Assert.Contains(Ecommerce.SourceId, graph.Find("h1").TextContent);
            Assert.Equal("incoming", graph.Find("select").GetAttribute("value"));
            Assert.Equal(2, graph.FindAll("li[data-graph-edge]").Count);
            Assert.Equal("true", graph.FindAll("button[data-graph-inspect-edge]")[1].GetAttribute("aria-pressed"));
            Assert.Equal(beforeBack, api.Requests.Count);
            Assert.Same(savedViewport, Assert.Single(diagram.VerifyInvoke("restoreViewport").Arguments));
        });
    }

    [Fact]
    public void DiagramStartupFailureKeepsTheNativeList()
    {
        var api = new ReadHandler((request, _) => Task.FromResult(Json(Page(request.Start, label: "Handoff"))));
        using var context = GraphTestContext.Create(api);
        GraphTestContext.SetupUnavailableDiagram(context);
        var graph = Render(context);
        var diagram = graph.FindComponent<GraphDiagram>();
        diagram.WaitForAssertion(() => Assert.Contains("The diagram could not start; use the neighbour list.", diagram.Markup));
        graph.Find("button[data-graph-inspect-edge]").Click();
        Assert.Equal("true", graph.Find("button[data-graph-inspect-edge]").GetAttribute("aria-pressed"));
        graph.Find("button[data-graph-follow]").Click();
        graph.WaitForAssertion(() => Assert.Equal(Warehouse, api.Requests.Last().Start));
        Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier == "render");
    }

    [Fact]
    public void StaleCursorRequiresReloadAndInvalidatesBackFrames()
    {
        var warehousePages = 0;
        var refreshed = false;
        var api = new ReadHandler((request, _) => Task.FromResult(request.Start == Ecommerce
            ? refreshed ? Problem(HttpStatusCode.NotFound, "entityNotFound") : Json(Page(Ecommerce, label: "First"))
            : request.Cursor is not null ? Problem(HttpStatusCode.Conflict, "staleCursor")
            : ++warehousePages == 1 ? Json(Page(Warehouse, "expired", "Old"))
            : Json(Page(Warehouse, label: "Updated") with { SnapshotId = "updated" })));
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        graph.WaitForElement("select").Change("incoming");
        graph.WaitForElement("button[data-graph-follow]").Click();
        graph.WaitForAssertion(() => Assert.Contains("Old", graph.Find("li[data-graph-edge]").TextContent));
        Button(graph, "Load more").Click();
        graph.WaitForAssertion(() =>
        {
            Assert.Contains("out of date", graph.Markup);
            Assert.Contains("Old", graph.Markup);
            Assert.DoesNotContain(graph.FindAll("button"), button => button.TextContent.Trim() == "Load more");
        });
        Button(graph, "Reload").Click();
        graph.WaitForAssertion(() => Assert.Contains("Updated", graph.Find("li[data-graph-edge]").TextContent));
        Assert.Equal(new TraversalRequest(Warehouse, "incoming", 1, false), api.Requests.Last());
        refreshed = true;
        Button(graph, "Back").Click();
        graph.WaitForElement("a[href='./']");
        Assert.Equal(new TraversalRequest(Ecommerce, "incoming", 1, false), api.Requests.Last());
        Assert.Contains("Updated", graph.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransientContinuationRetriesTheSameCursor(bool invalid)
    {
        var reads = 0;
        var api = new ReadHandler((request, _) => Task.FromResult(++reads switch
        {
            1 => Json(Page(Ecommerce, "continue", "First")),
            2 => invalid ? Problem(HttpStatusCode.UnprocessableEntity, "invalidCursor") : Problem(HttpStatusCode.ServiceUnavailable, "unavailable"),
            _ => Json(Page(Ecommerce, label: "Recovered"))
        }));
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        Button(graph, "Load more").Click();
        graph.WaitForAssertion(() => Assert.Contains("First", graph.Markup));
        Assert.Equal(invalid, graph.Markup.Contains("out of date", StringComparison.Ordinal));
        Button(graph, invalid ? "Reload" : "Retry").Click();
        graph.WaitForAssertion(() => Assert.Contains("Recovered", graph.Markup));
        Assert.Equal(invalid ? null : "continue", api.Requests.Last().Cursor);
    }

    [Fact]
    public async Task LateResponsesCannotReplaceTheLatestQuery()
    {
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failFollow = true;
        var api = new ReadHandler((request, _) =>
        {
            if (request.Direction == "incoming") return late.Task;
            if (request.Start == Warehouse && failFollow) return Task.FromResult(Problem(HttpStatusCode.ServiceUnavailable, "unavailable"));
            return Task.FromResult(Json(Page(request.Start, label: request.Direction)));
        });
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        await graph.WaitForElement("select").ChangeAsync(new() { Value = "outgoing" });
        var select = graph.Find("select");
        var delayed = select.ChangeAsync(new() { Value = "incoming" });
        graph.WaitForAssertion(() => Assert.Equal("incoming", api.Requests.Last().Direction));
        await graph.Find("select").ChangeAsync(new() { Value = "both" });
        late.SetResult(Json(Page(Ecommerce, label: "Superseded")));
        await delayed;
        Assert.DoesNotContain("Superseded", graph.Markup);
        Assert.Contains("both", graph.Find("li[data-graph-edge]").TextContent);
        Assert.DoesNotContain(graph.FindAll("button"), button => button.TextContent.Trim() == "Back");

        graph.Find("button[data-graph-follow]").Click();
        graph.WaitForAssertion(() => Assert.Contains(Ecommerce.SourceId, graph.Find("h1").TextContent));
        failFollow = false;
        Button(graph, "Retry").Click();
        graph.WaitForAssertion(() => Assert.Contains(Warehouse.SourceId, graph.Find("h1").TextContent));
        Button(graph, "Back").Click();
        graph.WaitForAssertion(() => Assert.Contains(Ecommerce.SourceId, graph.Find("h1").TextContent));
    }

    private static IRenderedComponent<GraphExplorer> Render(BunitContext context) =>
        context.Render<GraphExplorer>(args => args.Add(component => component.Key, WorkspaceLinks.Encode(Ecommerce)));

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<GraphExplorer> graph, string text)
    {
        graph.WaitForAssertion(() => Assert.Contains(graph.FindAll("button"), button => button.TextContent.Trim() == text));
        return graph.FindAll("button").Single(button => button.TextContent.Trim() == text);
    }

    internal static TraversalResult Page(PublishedKey focus, string? cursor = null, string? label = null)
    {
        PublishedKey edge = new(Ecommerce.SourceId, "assertion", $"https://example.test/edges/{label}");
        return new("snapshot", label is null ? [] : [new TraversalPath([
            new TraversalStep(edge, null, Warehouse, Ecommerce, label, focus == Ecommerce ? "incoming" : "outgoing",
                "published", ProvenanceFor(edge))])], [], cursor is not null,
            label is null ? [TraversalEntityFor(focus)] : [TraversalEntityFor(Ecommerce), TraversalEntityFor(Warehouse)], cursor);
    }

    private static TraversalEntity TraversalEntityFor(PublishedKey key) => new(key, "Order", ProvenanceFor(key), true, []);
    private static PublishedEntity EntityFor(PublishedKey key) => new(key, "Order", null, [], ProvenanceFor(key));
    private static Provenance ProvenanceFor(PublishedKey key) => new(key, "commit", "snapshot", null, []);
    internal static HttpResponseMessage Json(object result, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(result, options: WorkspaceJson.Options) };
    internal static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        Json(new { code, detail = code }, status);

    private sealed class ReadHandler(Func<TraversalRequest, CancellationToken, Task<HttpResponseMessage>> traverse) : HttpMessageHandler
    {
        public ConcurrentQueue<TraversalRequest> Requests { get; } = new();
        public bool EntityAvailable { get; set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/session":
                    return Json(new WorkspaceSession(true, false));
                case "/api/inventory":
                    return Json(new InventoryPage("snapshot", [EntityFor(Ecommerce), EntityFor(Warehouse)], [], [], null, []));
                case "/api/search":
                    return Json(new SearchResult("snapshot", [
                        new SearchHit(Ecommerce, null, "Order", 1, ProvenanceFor(Ecommerce), null),
                        new SearchHit(Warehouse, null, "Order", 1, ProvenanceFor(Warehouse), null)
                    ], false));
                case "/api/entities/detail":
                    var entity = (await request.Content!.ReadFromJsonAsync<EntityDetailRequest>(WorkspaceJson.Options, token))!.Key;
                    return Json(new EntityDetail("snapshot", EntityFor(entity), EntityAvailable || entity.Kind == "relationshipType", []));
                case "/api/relationships/detail":
                    var relation = (await request.Content!.ReadFromJsonAsync<RelationshipDetailRequest>(WorkspaceJson.Options, token))!;
                    return Json(new RelationshipDetail("snapshot", new TraversalStep(relation.Key, relation.RuntimeId,
                        Warehouse, Ecommerce, "Handoff", "outgoing", "published", ProvenanceFor(relation.Key!))));
                case "/api/traverse":
                    var traversal = Assert.IsType<TraversalRequest>(
                        await request.Content!.ReadFromJsonAsync<TraversalRequest>(WorkspaceJson.Options, token));
                    Requests.Enqueue(traversal);
                    return await traverse(traversal, token);
                default:
                    throw new InvalidOperationException($"Unexpected request for {request.RequestUri?.AbsolutePath}");
            }
        }
    }
}
