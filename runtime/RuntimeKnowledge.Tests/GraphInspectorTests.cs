using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Pages;
using RuntimeKnowledge.Web.Services;
using Xunit;
using static RuntimeKnowledge.Tests.GraphExplorerNavigationTests;

namespace RuntimeKnowledge.Tests;

public sealed class GraphInspectorTests
{
    private static readonly PublishedKey Focus = new("fixture/ecommerce", "concept", "https://example.test/ecommerce#Order");
    private static readonly PublishedKey Neighbour = new("fixture/warehouse", "concept", "https://example.test/warehouse#Order");
    private static readonly PublishedKey Edge = new(Focus.SourceId, "assertion", "https://example.test/ecommerce#Handoff");
    private static readonly Guid RuntimeId = Guid.Parse("3edf88a7-76ea-4bf2-91fc-eab63d70a88b");
    private static readonly string Commit = new('a', 40);
    private static readonly Evidence Citation = new("docs/order notes.md", 3, 4, "blob", "source");
    private static readonly IReadOnlyDictionary<string, string> Repositories = new Dictionary<string, string>
    {
        [Focus.SourceId] = "https://github.com/team/commerce"
    };

    [Fact]
    public void EvidenceLinksUseTheOwningRepositoryAndPinnedRevision()
    {
        var owner = Origin(Edge);
        var maps = new Dictionary<string, string>(Repositories);
        Assert.Equal($"https://github.com/team/commerce/blob/{Commit}/docs/order%20notes.md#L3-L4",
            GraphEvidenceLinks.TryCreate(maps, owner, Citation)?.AbsoluteUri);
        Assert.Null(GraphEvidenceLinks.TryCreate(new Dictionary<string, string>(), owner, Citation));
        Assert.Null(GraphEvidenceLinks.TryCreate(maps, owner, Citation with { Path = "../other.ttl" }));
        Assert.Null(GraphEvidenceLinks.TryCreate(maps, owner with { SourceCommit = "main" }, Citation));
        maps[Focus.SourceId] = "https://github.com@untrusted.example/team/commerce";
        Assert.Null(GraphEvidenceLinks.TryCreate(maps, owner, Citation));
        var core = Origin(new PublishedKey("division-core", "concept", "https://example.test/core#Order"));
        Assert.Null(GraphEvidenceLinks.TryCreate(maps, core, Citation));
        maps["division-core"] = "https://github.com/team/division";
        Assert.StartsWith($"https://github.com/team/division/blob/{Commit}/",
            GraphEvidenceLinks.TryCreate(maps, core, Citation)?.AbsoluteUri);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParallelEdgesOpenTheirOwnDetails(bool configureLinks)
    {
        var api = new InspectorHandler();
        using var context = GraphTestContext.Create(api, configureLinks ? Repositories : null);
        var graph = Render(context);
        graph.WaitForElement("button[data-graph-inspect-edge^='p:']").Click();
        graph.WaitForAssertion(() =>
        {
            var panel = graph.Find("section[aria-label='Graph details']");
            Assert.Equal(new RelationshipDetailRequest(Edge), Assert.Single(api.RelationshipReads));
            Assert.Contains(Commit, panel.TextContent);
            Assert.Contains(Citation.Path, panel.TextContent);
            Assert.Contains("Incoming", panel.TextContent);
            Assert.Contains(Focus.SourceId, panel.TextContent);
            Assert.Contains(Neighbour.SourceId, panel.TextContent);
            var evidence = panel.QuerySelector("a[href*='/blob/']");
            if (configureLinks)
                Assert.Equal($"https://github.com/team/commerce/blob/{Commit}/docs/order%20notes.md#L3-L4", evidence?.GetAttribute("href"));
            else
            {
                Assert.Null(evidence);
                Assert.Contains("Evidence link unavailable", panel.TextContent);
            }
        });
        graph.Find("button[data-graph-inspect-edge^='r:']").Click();
        graph.WaitForAssertion(() =>
        {
            var panel = graph.Find("section[aria-label='Graph details']");
            Assert.Equal(new RelationshipDetailRequest(RuntimeId: RuntimeId), api.RelationshipReads.Last());
            Assert.Contains("Workspace relationship", panel.TextContent);
            Assert.Contains("fixture-manager", panel.TextContent);
            Assert.Contains("Dispatch starts after checkout.", panel.TextContent);
            Assert.NotNull(panel.QuerySelector($"a[href='history/{RuntimeId:D}']"));
            Assert.Equal(2, graph.FindAll("li[data-graph-edge]").Count);
            Assert.Single(api.Traversals);
        });
    }

    [Fact]
    public async Task EarlierDetailsCannotReplaceASubsequentSelection()
    {
        var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new InspectorHandler { DelayPublished = late };
        using var context = GraphTestContext.Create(api);
        var graph = Render(context);
        var earlier = graph.WaitForElement("button[data-graph-inspect-edge^='p:']").ClickAsync(new());
        graph.WaitForAssertion(() => Assert.Equal(Edge, Assert.Single(api.RelationshipReads).Key));
        await graph.Find("button[data-graph-inspect-edge^='r:']").ClickAsync(new());
        graph.WaitForAssertion(() => Assert.Contains("fixture-manager", graph.Find("section[aria-label='Graph details']").TextContent));
        late.SetResult(Json(new RelationshipDetail("snapshot", PublishedStep)));
        await earlier;
        Assert.Contains("fixture-manager", graph.Find("section[aria-label='Graph details']").TextContent);
    }

    [Fact]
    public void NodeInspectionAndClosingKeepTheLoadedNeighbourhood()
    {
        var api = new InspectorHandler();
        using var context = GraphTestContext.Create(api, Repositories);
        var canvas = GraphTestContext.SetupDiagram(context);
        var graph = Render(context);
        graph.WaitForElement("button[data-graph-inspect-node]").Click();
        graph.WaitForAssertion(() =>
        {
            var panel = graph.Find("section[aria-label='Graph details']");
            Assert.Equal(Neighbour, Assert.Single(api.EntityReads));
            Assert.Contains(Neighbour.SourceId, panel.TextContent);
            Assert.Contains(Commit, panel.TextContent);
            Assert.Contains("The selected node.", panel.TextContent);
            Assert.Contains(panel.QuerySelectorAll("button"), button => button.TextContent.Trim() == "Follow");
            Assert.Single(api.Traversals);
            Assert.Single(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        });
        graph.Find("button[data-graph-load-more]").Click();
        graph.WaitForAssertion(() => Assert.Equal(2, api.Traversals.Count));
        var renderCount = canvas.Invocations["render"].Count;
        graph.Find("button[data-graph-close-inspector]").Click();
        Assert.Empty(graph.FindAll("section[aria-label='Graph details']"));
        Assert.Equal(2, graph.FindAll("li[data-graph-edge]").Count);
        Assert.Equal(renderCount, canvas.Invocations["render"].Count);
        graph.Find("button[data-graph-inspect-focus]").Click();
        graph.WaitForAssertion(() =>
        {
            Assert.Equal(Focus, api.EntityReads.Last());
            Assert.DoesNotContain(graph.Find("section[aria-label='Graph details']").QuerySelectorAll("button"),
                button => button.TextContent.Trim() == "Follow");
        });
    }

    private static IRenderedComponent<GraphExplorer> Render(BunitContext context) =>
        context.Render<GraphExplorer>(args => args.Add(component => component.Key, WorkspaceLinks.Encode(Focus)));

    private static Provenance Origin(PublishedKey key, params Evidence[] evidence) =>
        new(key, Commit, "snapshot", "Reviewed handoff", evidence);

    private static TraversalStep PublishedStep => new(Edge, null, Neighbour, Focus, "hands off", "outgoing", "published", Origin(Edge, Citation));
    private static TraversalStep RuntimeStep => PublishedStep with
    {
        Key = null, RuntimeId = RuntimeId, Origin = "runtime", Provenance = null,
        Contribution = new(RuntimeId, 1, "relationship", new("issuer", "fixture-manager"), DateTimeOffset.UnixEpoch,
            "snapshot", false, null, new(Neighbour, Focus, RelationshipMeaning.Custom("hands off", "Dispatch starts after checkout."),
                "hands off", Origin(Neighbour), Origin(Focus), null), [])
    };

    private sealed class InspectorHandler : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage>? DelayPublished { get; init; }
        public ConcurrentQueue<RelationshipDetailRequest> RelationshipReads { get; } = new();
        public ConcurrentQueue<PublishedKey> EntityReads { get; } = new();
        public ConcurrentQueue<TraversalRequest> Traversals { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/session":
                    return Json(new WorkspaceSession(true, false));
                case "/api/traverse":
                    var query = (await request.Content!.ReadFromJsonAsync<TraversalRequest>(WorkspaceJson.Options, token))!;
                    Traversals.Enqueue(query);
                    return Json(new TraversalResult("snapshot", query.Cursor is null ? [new([PublishedStep]), new([RuntimeStep])] : [],
                        [], query.Cursor is null, [new(Focus, "Order", Origin(Focus), true, []), new(Neighbour, "Order", Origin(Neighbour), true, [])],
                        query.Cursor is null ? "next" : null));
                case "/api/entities/detail":
                    var entity = (await request.Content!.ReadFromJsonAsync<EntityDetailRequest>(WorkspaceJson.Options, token))!.Key;
                    EntityReads.Enqueue(entity);
                    return Json(new EntityDetail("snapshot", new(entity, "Order", "The selected node.", [], Origin(entity)), true, []));
                case "/api/relationships/detail":
                    var relationship = (await request.Content!.ReadFromJsonAsync<RelationshipDetailRequest>(WorkspaceJson.Options, token))!;
                    RelationshipReads.Enqueue(relationship);
                    if (relationship.Key is not null && DelayPublished is not null)
                        return await DelayPublished.Task;
                    return Json(new RelationshipDetail("snapshot", relationship.Key is null ? RuntimeStep : PublishedStep));
                default:
                    throw new InvalidOperationException($"Unexpected request for {request.RequestUri?.AbsolutePath}");
            }
        }
    }
}
