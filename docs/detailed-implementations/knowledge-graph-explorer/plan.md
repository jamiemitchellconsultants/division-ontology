# Knowledge Graph Explorer — detailed implementation plan

**Inputs** [Task plan](../../superpowers/plans/knowledge-graph-explorer/plan.md) · [Vision](../../features/knowledge-graph-explorer/01-vision.md) · [Behaviours](../../features/knowledge-graph-explorer/02-behaviours.md) · [Architecture](../../features/knowledge-graph-explorer/03-architecture.md)

**Build branch** `feature/knowledge-graph-explorer/dev-build`. Start after the plan is reviewed and merged and the build gate opens. Perform each task separately, add its tests first, and see the expected failure before implementation. Run the focused suite again and `npm test`, review `git diff --cached --check` and the staged diff, then commit and push using that task's message. Stage only that task's files. Use `git push -u origin HEAD` for the first push and `git push` for subsequent tasks. Use `cd runtime` for .NET commands so the pinned SDK applies. Generated `dist/` files belong to catalog sync.

## Task 1 — Project traversal pages into a graph neighbourhood

**Files** Create `runtime/RuntimeKnowledge.Web/Services/GraphNeighbourhood.cs` and `runtime/RuntimeKnowledge.Tests/GraphNeighbourhoodTests.cs`.

**Interfaces** In `RuntimeKnowledge.Web.Services`, define `GraphQuery(PublishedKey Focus, string Direction)` with `TraversalRequest ToRequest(string? cursor = null)`, and `GraphEdge(string Id, TraversalStep Step)`. `GraphNeighbourhood.FromPage(GraphQuery query, TraversalResult page)` and `GraphNeighbourhood.Append(TraversalResult page)` return neighbourhoods with `Query`, `SnapshotId`, `Nodes` (`IReadOnlyDictionary<PublishedKey, TraversalEntity>`), `Edges` (`IReadOnlyList<GraphEdge>`), `Gaps`, `RelevantGaps`, `OtherGaps`, `NextCursor`, and `TraversalIncomplete` (the response's `Truncated`). Appending returns a new neighbourhood so Back history can retain the old record set.

The traversal API returns every coverage gap in the active snapshot on every page (`GraphQueries.TraverseAsync` calls the same `GapsAsync` as inventory). It does not report gaps per focus. The projection therefore scopes them itself. A gap is **relevant** when its `SourceId` is the source of the focus or of a loaded node, and its `NodeIri` is either null or the `Identifier` of the focus or a loaded node. Every other gap is in `OtherGaps`, including one with a null `SourceId`. `Gaps` keeps the combined, distinct list unchanged, so the explorer can still link to it.

- [ ] Start with this test file. Add the named fixture cases below before the green run.

```csharp
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Services;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class GraphNeighbourhoodTests
{
    private static readonly PublishedKey Ecommerce = new("fixture/ecommerce", "concept", "https://example.test/ecommerce#Order");
    private static readonly PublishedKey Warehouse = new("fixture/warehouse", "concept", "https://example.test/warehouse#Order");
    private static readonly PublishedKey Published = new("fixture/ecommerce", "assertion", "https://example.test/ecommerce#handoff");
    private static readonly Guid RuntimeId = Guid.Parse("373ca2ef-d6aa-4192-8034-47532bcdfd18");

    [Fact]
    public void DirectionsAndExactCursorAreSentWithThePublishedFocus()
    {
        var query = new GraphQuery(Ecommerce, "incoming");
        Assert.Equal(new TraversalRequest(Ecommerce, "incoming", 1, false), query.ToRequest());
        Assert.Equal(new TraversalRequest(Ecommerce, "incoming", 1, false, "opaque"), query.ToRequest("opaque"));
    }

    [Fact]
    public void ParallelOriginsAndSameLabelNodesRemainDistinctAcrossPages()
    {
        var published = new TraversalStep(Published, null, Warehouse, Ecommerce, "hands off",
            "incoming", "published", null);
        var runtime = published with { Key = null, RuntimeId = RuntimeId, Origin = "runtime" };
        var first = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"), Page("next", published));
        var result = first.Append(Page(null, published, runtime));
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal(new[] { "Order", "Order" }, result.Nodes.Values.Select(entity => entity.Label));
        Assert.Equal(new[] { "p:" + WorkspaceLinks.Encode(Published), $"r:{RuntimeId:D}" }, result.Edges.Select(edge => edge.Id));
        Assert.All(result.Edges, edge => Assert.Equal((Warehouse, Ecommerce), (edge.Step.From, edge.Step.To)));
        Assert.Single(first.Edges);
    }

    [Fact]
    public void EmptyContinuationAdvancesAndAnEmptyFinalPageRetainsTheFocus()
    {
        var first = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"), Page("first"));
        var intermediate = first.Append(Page("second"));
        var final = intermediate.Append(Page(null));
        Assert.Equal("second", intermediate.NextCursor);
        Assert.True(intermediate.TraversalIncomplete);
        Assert.Empty(final.Edges);
        Assert.True(final.Nodes.ContainsKey(Ecommerce));
        Assert.Null(final.NextCursor);
        Assert.False(final.TraversalIncomplete);
    }

    [Fact]
    public void APageFromAnotherSnapshotCannotBeMerged()
    {
        var first = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"), Page("next"));
        Assert.Throws<InvalidOperationException>(() => first.Append(Page(null) with { SnapshotId = "replacement" }));
        Assert.Equal("next", first.NextCursor);
    }

    [Fact]
    public void CoverageGapsAreScopedToTheLoadedSourcesAndNodes()
    {
        var sourceGap = new CoverageGap("coverage-missing", "fixture/ecommerce", null, "Code coverage missing");
        var otherNode = new CoverageGap("denotation-unresolved", "fixture/ecommerce", "https://example.test/ecommerce#Other", "Unresolved");
        var focusNode = otherNode with { NodeIri = Ecommerce.Identifier };
        var elsewhere = new CoverageGap("graph-unavailable", "fixture/billing", null, "Graph unavailable");
        var view = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"),
            Page(null) with { Gaps = [sourceGap, otherNode, focusNode, elsewhere] });
        Assert.Equal(new[] { sourceGap, focusNode }, view.RelevantGaps);
        Assert.Equal(new[] { otherNode, elsewhere }, view.OtherGaps);
        Assert.Equal(4, view.Gaps.Count);
    }

    private static TraversalResult Page(string? cursor, params TraversalStep[] edges) =>
        new("snapshot", edges.Select(step => new TraversalPath([step])).ToArray(), [], cursor is not null,
            edges.Length == 0 ? [Entity(Ecommerce)] : [Entity(Ecommerce), Entity(Warehouse)], cursor);

    private static TraversalEntity Entity(PublishedKey key) =>
        new(key, "Order", new Provenance(key, "commit", "snapshot", null, []), true, []);
}
```

- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~GraphNeighbourhoodTests --nologo)`. Expect compilation to fail because the projection types are absent.
- [ ] Project one step per traversal path, keyed by `p:` plus `WorkspaceLinks.Encode(publishedKey)` or `r:` plus the canonical `Guid` in `D` format. Keep first appearance order, deduplicate on the exact edge ID, and merge nodes by `PublishedKey`. Copy fields from the current page for refreshed entity context, combine distinct gaps, and recompute `RelevantGaps` and `OtherGaps` against the merged nodes in the order the gaps were first seen. Use the latest page's continuation and truncation. A mismatched snapshot throws `InvalidOperationException` before append. Extend `EmptyContinuationAdvancesAndAnEmptyFinalPageRetainsTheFocus` with a loaded edge and a `CoverageGap` and assert they survive a subsequent empty final page. Add a test for `Truncated = true` with no cursor and assert both fields remain available for the page to classify separately.
- [ ] Rerun the focused command. Commit `feat: project one-hop workspace graph pages` and push.

## Task 2 — Wire entry links, navigation, and accessible paging

**Files** Modify the entry pages, `WorkspaceLinks`, and `WorkspaceClient` listed in the task plan. Create `Pages/GraphExplorer.razor`, `Components/GraphNeighbourList.razor`, `GraphTestContext.cs` and `GraphExplorerNavigationTests.cs` in the existing runtime test project, and the Node route test. Modify the existing SPA route matcher in `deploy/workspace/aws/cdn.tf`.

**Interfaces** `WorkspaceLinks.Graph(PublishedKey key)` returns `graph/{Encode(key)}`. `GraphExplorer` exposes route parameter `Key` and holds the current selection as one graph identity: `n:` plus the encoded key for a node, or the `GraphEdge.Id` for an edge. The focus `n:` identity is the default selection. `GraphNeighbourList` takes a `GraphNeighbourhood`, `string? SelectedId`, `EventCallback<PublishedKey> OnFollow`, and `EventCallback<string> OnInspect`. `OnInspect` receives a node or an edge identity. `WorkspaceClient.Traverse(TraversalRequest, CancellationToken cancellationToken = default)`, `Entity(PublishedKey, CancellationToken cancellationToken = default)`, and `Relationship(RelationshipDetailRequest, CancellationToken cancellationToken = default)` pass the token to the existing HTTP send and JSON read. Existing single-argument calls still compile.

`GraphTestContext.Create(HttpMessageHandler handler)` is the only place graph tests build a `BunitContext`. It sets loose JS interop, and registers the scoped `DraftState` and a `WorkspaceClient` over the handler. The client is built by a factory so the container disposes it. Tasks 3–5 extend this helper when they add injected services or JS modules. They never add registrations to individual tests, so the tests from earlier tasks keep compiling and rendering. `WorkspaceClient.Traverse(TraversalRequest, CancellationToken cancellationToken = default)`, `Entity(PublishedKey, CancellationToken cancellationToken = default)`, and `Relationship(RelationshipDetailRequest, CancellationToken cancellationToken = default)` pass the token to the existing HTTP send and JSON read. Existing single-argument calls still compile.

- [ ] Use the `BunitContext` and stub `HttpMessageHandler` pattern in `WorkspaceUiTests.cs` for `GraphExplorerNavigationTests`. Return `WorkspaceSession(true, false)` for `/api/session`. For traversal, record the decoded `TraversalRequest` and return a scripted response or a `TaskCompletionSource<HttpResponseMessage>` created with `RunContinuationsAsynchronously`. Use an intentionally uncancellable completion in the delayed-response case to prove the generation guard separately from cancellation. Give the toolbar distinct accessible names **Direction**, **Back**, **Load more**, **Reload**, and **Retry**, and each neighbour row separate **Follow**, **Inspect** (the neighbour entity), and **Inspect relationship** actions, each with a source-qualified name. Add these cases before implementation.

  | Test | Arrange and assert |
  | --- | --- |
  | `LinksSelectTheExactAvailablePublishedEntity` | Two **Order** search results with different sources link to different `graph/{key}` values. The entry and entity page select the exact encoded key. An unavailable entity detail and a relationship type omit the graph link. A malformed direct entry shows recovery and performs zero traversal requests. |
  | `FirstPageAndDirectionChangesUseOneHop` | First request equals `new TraversalRequest(ecommerce, "both", 1, false)`. Selecting incoming makes an incoming request with `Cursor == null`, shows connections for that response, and leaves the original arrow endpoints intact in the row. |
  | `EmptyPageKeepsLoadMoreAndFinalPageKeepsEarlierEdges` | Script one edge and cursor `first`, zero edges and cursor `second`, then a final empty page. Assert request cursors are `null`, `first`, `second`, all query fields are otherwise equal, and the initial edge remains visible through the final page. Assert **Load more** disappears only at the end. |
  | `CoverageEmptyAndIncompleteStatesAreIndependent` | Run these responses: a completed empty response with no gaps; a completed empty response whose only gap is in another source; a completed empty response with a gap for the focus source; an empty response with a cursor; and `Truncated = true` without a cursor. Assert, in order: no visible connections; no visible connections plus a separate count of coverage gaps elsewhere in the division; a coverage notice for the focus source that does not claim the node is isolated; **Load more** with no final-empty text; and an incomplete or unsupported paging notice. |
  | `NeighbourRowsInspectWithoutFollowing` | Choose **Inspect** on a neighbour row. Assert the selection becomes that row's `n:` identity, no traversal request is sent, and the focus is unchanged. Then choose **Follow** and assert one page-one request for the neighbour. |
  | `BackRestoresTheLoadedFrame` | Load page two, follow a neighbour, then Back. Assert the original focus, direction, both loaded edges and selection return and no traversal request is made for Back. Extend during Task 3 to assert saved pan and zoom. |
  | `StaleCursorRequiresReloadAndInvalidatesBackFrames` | Open a neighbour frame with cursor, return `409 staleCursor`, and assert the original frame stays visible and marked out of date and **Load more** is unavailable. Reload sends the saved query without a cursor and replaces old rows. Back thereafter reads page one for the invalidated saved focus and preserves its direction. Script a missing saved focus and assert a route to search. |
  | `TransientContinuationRetriesTheSameCursor` | Fail continuation once with 503, assert the frame stays visible without an out-of-date label, and verify **Retry** uses the same cursor. Script `422 invalidCursor` in a second case and assert **Reload** uses page one. |
  | `LateResponsesCannotReplaceTheLatestQuery` | Delay the incoming response, complete outgoing first, then complete incoming. Assert only outgoing rows are visible and Back history contains no superseded frame. Separately fail the latest follow with 503 and verify retry and return to the last successful frame. |

  Create the shared context helper first.

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RuntimeKnowledge.Web.Services;

namespace RuntimeKnowledge.Tests;

internal static class GraphTestContext
{
    public static BunitContext Create(HttpMessageHandler handler)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(_ => new WorkspaceClient(new HttpClient(handler) { BaseAddress = new Uri("https://workspace.test") }));
        context.Services.AddScoped<DraftState>();
        return context;
    }
}
```

  Start the navigation test file with the entry request test and its self-contained handler. Extend the handler with the specific page, problem, and delayed-response fixtures in the table.

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Pages;
using RuntimeKnowledge.Web.Services;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class GraphExplorerNavigationTests
{
    [Fact]
    public void FirstPageAndDirectionChangesUseOneHop()
    {
        var focus = new PublishedKey("fixture/ecommerce", "concept", "https://example.test/ecommerce#Order");
        var requests = new ConcurrentQueue<TraversalRequest>();
        using var context = GraphTestContext.Create(new ReadHandler(focus, requests));
        var graph = context.Render<GraphExplorer>(args => args.Add(component => component.Key, WorkspaceLinks.Encode(focus)));
        graph.WaitForAssertion(() => Assert.Equal(new TraversalRequest(focus, "both", 1, false), Assert.Single(requests)));
        graph.WaitForElement("select").Change("incoming");
        graph.WaitForAssertion(() => Assert.Equal(new[] {
            new TraversalRequest(focus, "both", 1, false), new TraversalRequest(focus, "incoming", 1, false)
        }, requests.ToArray()));
    }

    private sealed class ReadHandler(PublishedKey focus, ConcurrentQueue<TraversalRequest> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object result;
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/session":
                    result = new WorkspaceSession(true, false);
                    break;
                case "/api/traverse":
                    var traversal = await request.Content!.ReadFromJsonAsync<TraversalRequest>(WorkspaceJson.Options, token);
                    requests.Enqueue(Assert.IsType<TraversalRequest>(traversal));
                    result = new TraversalResult("snapshot", [], [], false,
                        [new TraversalEntity(focus, "Order", new Provenance(focus, "commit", "snapshot", null, []), true, [])]);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected request for {request.RequestUri?.AbsolutePath}");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result, options: WorkspaceJson.Options) };
        }
    }
}
```

  Create `test/graph-explorer-route.test.mjs` using this complete test. It tests the deployed rewrite function itself.

```javascript
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { runInNewContext } from 'node:vm';

test('CloudFront rewrites graph deep links while serving graph assets directly', () => {
  const terraform = readFileSync(new URL('../deploy/workspace/aws/cdn.tf', import.meta.url), 'utf8');
  const script = terraform.match(/code\s*=\s*<<-JS\n([\s\S]+?)^\s*JS$/m)?.[1];
  assert.ok(script);
  const handler = runInNewContext(`${script}\nhandler`);
  const rewrite = uri => handler({ request: { uri } }).uri;
  assert.equal(rewrite('/graph/abc_123'), '/index.html');
  assert.equal(rewrite('/entity/abc_123'), '/index.html');
  assert.equal(rewrite('/graph-diagram.mjs'), '/graph-diagram.mjs');
  assert.equal(rewrite('/vendor/cytoscape/3.35.0/cytoscape.esm.min.mjs'), '/vendor/cytoscape/3.35.0/cytoscape.esm.min.mjs');
});
```

- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~GraphExplorerNavigationTests --nologo)` and `node --test test/graph-explorer-route.test.mjs`. Expect missing web navigation types and the graph rewrite assertion to fail.
- [ ] Add the authorized route and links. Decode and allow `concept` and `node`; load session capability and the first successful frame. Let the list use native links or buttons and a status message for loading and completion. Show `RelevantGaps` next to the result as source coverage, and never say "no connections" or "isolated" while a relevant gap exists. Summarise `OtherGaps` as a separate count of gaps elsewhere in the division. **Inspect** changes only the selection; **Follow** is the only list action that traverses. Set the start direction to `both`. Append under the exact query and cursor, one in-flight continuation per frame. Keep successful frames in memory. Build a Back entry only upon a successful follow and restore it unless invalidated. Changing direction starts page one at the existing focus. Maintain a monotonically increasing request ID and a cancellation token for superseded traversal reads; apply effects only from the matching request. Keep a retry target for a failed transition. `staleCursor`, a snapshot-mismatched continuation, and a known later snapshot mark earlier frames stale; `invalidCursor` also clears continuation and offers Reload. A temporary network error retains its cursor for Retry. Clear state when the page is disposed. Add `graph` to the CloudFront rewrite alternatives, with the same segment boundary as the other routes.
- [ ] Rerun both focused commands and `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~WorkspaceUiTests --nologo)`. Commit `feat: navigate paged graph neighbourhoods` and push.

## Task 3 — Draw the shared neighbourhood with Cytoscape

**Files** Add `GraphDiagram.razor`, `wwwroot/graph-diagram.mjs`, and the versioned vendor assets under the web project. Update `GraphExplorer.razor` to host the diagram and `wwwroot/workspace.css` for its container and controls. Add `test/graph-explorer-renderer.test.mjs`. In `deploy/workspace/aws/storage.tf`, add `mjs = "text/javascript; charset=utf-8"` to `local.web_types`. Without it, S3 stores both graph modules as `application/octet-stream`, and browsers refuse to run module scripts served with that type. Add `deploy/workspace/aws/tests/fixture/graph-diagram.mjs` containing `export {};`, and add an assertion to `deploy/workspace/aws/tests/workspace.tftest.hcl`. Extend `GraphTestContext` with the diagram module setup.

**Interfaces** `GraphDiagram` takes the current `GraphNeighbourhood`, `string? SelectedId`, `EventCallback<PublishedKey> OnFollow`, and `EventCallback<string> OnInspect`. It uses the same identities as the list. A tap on a node or an edge inspects it. A double tap on a neighbour node follows it. An edge has no follow action. It exposes `CaptureViewportAsync()`, `RestoreViewportAsync(GraphViewport)`, `ZoomByAsync(double factor)`, and `ResetZoomAsync()` to the explorer and owns disposal. `GraphViewport` carries `Pan`, `Zoom`, and `Positions`, using `GraphPoint(double X, double Y)` for pan and positions. The module exports `mount(host, dotNetCallbacks)`, `placeNeighbours(ids, previous)`, `bindGraphActions(cy, callbacks)` with callbacks `inspect(id)` and `follow(id)`, and `diagramFor(host)`. `mount` returns methods `render(model)`, `captureViewport()`, `restoreViewport(viewport)`, `zoomBy(factor)`, `resetZoom()`, `renderedPosition(id)`, and `dispose()`. `renderedPosition(id)` returns `{ x, y }` relative to the host, or `null` when the ID is not in the current view. For a node this is `renderedPosition()`. For an edge it is `renderedMidpoint()`, which lies on that edge's own curve, so parallel edges give distinct points. `diagramFor(host)` returns the live instance for a mounted host from a module-level `WeakMap`, and returns `null` after `dispose()`. The browser harness uses it to find canvas coordinates, and nothing is put on `window`.

The explorer calls restore once after rendering a Back frame. Identity passed to the diagram uses `n:` plus the encoded published key for nodes and the existing `GraphEdge.Id` for edges. The module must not touch `document`, `window`, or other browser globals at import time; only `mount` may. The renderer test imports it in Node, where those globals are absent. The component imports it with the specifier `./graph-diagram.mjs`, the same one the harness uses, so both resolve to the same module instance.

Startup failure is a supported state. Startup has failed when the `import` returns `null` (which is what loose bUnit interop returns when no module is set up), when the import or `mount` throws `JSException`, or when `mount` returns `null`. In each case the explorer shows "The diagram could not start; use the neighbour list." beside the list, and it makes no further diagram calls.

- [ ] Write `test/graph-explorer-renderer.test.mjs` from the test file below. Add `GraphTestContext.SetupDiagram(BunitContext context)`, which returns the mounted-instance handler:

```csharp
public static BunitJSModuleInterop SetupDiagram(BunitContext context) =>
    context.JSInterop.SetupModule("./graph-diagram.mjs")
        .SetupModule(invocation => invocation.Identifier == "mount");
```

  Add a bUnit viewport restoration assertion to `BackRestoresTheLoadedFrame`. On the handler from `SetupDiagram`, use `Setup<GraphViewport>("captureViewport")` to return a non-default zoom and pan. Then verify, with `VerifyInvoke("restoreViewport")`, that those values are supplied on Back after the frame is rendered. Add `DiagramStartupFailureKeepsTheNativeList` without calling `SetupDiagram`: assert the startup-failure message, working list **Follow** and **Inspect** actions, and no `render` invocation. Add the Terraform assertion:

```hcl
  assert {
    condition     = aws_s3_object.web["graph-diagram.mjs"].content_type == "text/javascript; charset=utf-8"
    error_message = "Browser modules must be served as JavaScript."
  }
```

```javascript
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const web = new URL('../runtime/RuntimeKnowledge.Web/wwwroot/', import.meta.url);
const vendor = new URL('vendor/cytoscape/3.35.0/', web);

test('the browser package preserves the pinned upstream module and license', async () => {
  for (const [file, hash] of [
    ['cytoscape.esm.min.mjs', 'aa33b0103bbdb721aeb1506000cb6e056692400baba7264c552d000b09da01f2'],
    ['LICENSE', 'eb319c6e6f233607f71e8e2f450391751883cfc0eeb3ca7ef574c13d1d9c2203']
  ]) {
    assert.equal(createHash('sha256').update(await readFile(new URL(file, vendor))).digest('hex'), hash);
  }
});

test('append preserves positions and diagram actions use stable identities', async () => {
  assert.equal(typeof globalThis.document, 'undefined');
  const { placeNeighbours, bindGraphActions, diagramFor } = await import(new URL('graph-diagram.mjs', web));
  assert.equal(diagramFor({}), null);
  const { default: cytoscape } = await import(new URL('cytoscape.esm.min.mjs', vendor));
  const first = placeNeighbours(['n:a', 'n:b'], {});
  const appended = placeNeighbours(['n:a', 'n:b', 'n:c'], first);
  for (const id of ['n:a', 'n:b', 'n:c']) {
    assert.ok(Number.isFinite(appended[id].x) && Number.isFinite(appended[id].y));
  }
  assert.notDeepEqual(first['n:a'], first['n:b']);
  assert.deepEqual(appended['n:a'], first['n:a']);
  assert.deepEqual(appended['n:b'], first['n:b']);
  const cy = cytoscape({ headless: true, elements: [
    { data: { id: 'n:a' } }, { data: { id: 'n:b' } },
    { data: { id: 'p:assertion', source: 'n:a', target: 'n:b' } },
    { data: { id: 'r:contribution', source: 'n:a', target: 'n:b' } }
  ] });
  const actions = [];
  try {
    bindGraphActions(cy, {
      follow: id => actions.push(['follow', id]),
      inspect: id => actions.push(['inspect', id])
    });
    for (const id of ['n:b', 'p:assertion', 'r:contribution']) cy.getElementById(id).emit('tap');
    for (const id of ['n:b', 'p:assertion']) cy.getElementById(id).emit('dbltap');
    assert.deepEqual(actions, [
      ['inspect', 'n:b'], ['inspect', 'p:assertion'], ['inspect', 'r:contribution'], ['follow', 'n:b']
    ]);
  } finally {
    cy.destroy();
  }
});
```
- [ ] Run `node --test test/graph-explorer-renderer.test.mjs`. Expect a missing module or vendor asset.
- [ ] Use the following exact artifact. Download into a temporary directory, verify the archive hash, then extract the listed files with `tar -xOf` so the bytes remain unchanged. Record upstream version, URL, and hashes in `wwwroot/vendor/cytoscape/3.35.0/SOURCE.md`. The package manifest declares MIT and has no runtime dependencies. [Upstream release v3.35.0](https://github.com/cytoscape/cytoscape.js/releases/tag/v3.35.0) · [npm archive](https://registry.npmjs.org/cytoscape/-/cytoscape-3.35.0.tgz)

  | File | SHA-256 |
  | --- | --- |
  | `cytoscape-3.35.0.tgz` | `058a81a7534777ea68dc737e9b77bd5b701e00acd9bee246245a576eeeac32e6` |
  | `package/dist/cytoscape.esm.min.mjs` → `cytoscape.esm.min.mjs` | `aa33b0103bbdb721aeb1506000cb6e056692400baba7264c552d000b09da01f2` |
  | `package/LICENSE` → `LICENSE` | `eb319c6e6f233607f71e8e2f450391751883cfc0eeb3ca7ef574c13d1d9c2203` |

- [ ] Import the same-origin `.mjs` on first graph render from a dedicated sized container. In the diagram model send display labels, origin, endpoints and IDs. Use Cytoscape preset coordinates with focus at `(0, 0)`. Allocate neighbours in first-seen order on rings whose slots have fixed angles. Start with 8 slots at radius 180 layout units and add 8 slots and 180 units of radius per ring. Preserve previous slots on append. Use arrowheads from source to target and curved parallel edges. Batch new elements and fit only on the first view of a new focus. Save and restore positions, zoom and pan on Back. Ignore callbacks for IDs outside the current view, and do not follow on a double tap of the focus itself. Highlight the element whose ID is `SelectedId`. Treat startup failure as described under **Interfaces**. Wire keyboard toolbar buttons to zoom in, zoom out, and reset, and release the instance and .NET/JS references on dispose.
- [ ] Rerun the renderer and navigation tests. Run `(cd runtime && dotnet publish RuntimeKnowledge.Web -c Release -o "$graph_publish_dir")` with `graph_publish_dir` set to an absolute new temporary directory. Verify the published `wwwroot/graph-diagram.mjs` and vendor paths exist and the module and license still match both hashes. Run `bash deploy/workspace/aws/verify-terraform.sh` and expect the new content-type assertion to pass. Commit `feat: render workspace graph with local Cytoscape` and push.

## Task 4 — Inspect published and workspace records

**Files** Create `GraphInspector.razor` and `Services/GraphEvidenceLinks.cs` in the web project and `GraphInspectorTests.cs` in the runtime tests. Extend the explorer, `OriginEvidence.razor`, `Program.cs`, `wwwroot/appsettings.json` (add an empty `"EvidenceRepositories": {}`), and `GraphTestContext`. Also carry the map into both deployed configurations, because neither serves the published `wwwroot/appsettings.json`:

- **AWS.** `storage.tf` skips that file and writes `aws_s3_object.web_settings`. Add `variable "evidence_repositories"` to `deploy/workspace/aws/variables.tf` with type `map(string)`, default `{}`, and a validation that every value matches `^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$`. Then add `EvidenceRepositories = var.evidence_repositories` to the `jsonencode` of `web_settings`.
- **Home lab.** `deploy/workspace/home-lab/compose.yml` inlines `web-settings`. Append `, "EvidenceRepositories": ${WORKSPACE_EVIDENCE_REPOSITORIES:-null}` inside its JSON object. Extend the `jq` check in `deploy/workspace/home-lab/verify-config.sh` to accept `null` or an object whose values all match the same GitHub-root pattern.

**Interfaces** `GraphInspector` accepts the current `GraphNeighbourhood`, `string SelectedId`, `EventCallback<PublishedKey> OnFollow`, and `EventCallback OnClose`. For an `n:` selection it loads entity detail for that node. The focus shows no follow action; a neighbour shows **Follow**, which raises `OnFollow`. For a `p:` or `r:` selection it loads relationship detail for that edge.

`GraphEvidenceLinks.TryCreate(IReadOnlyDictionary<string, string> repositories, Provenance owner, Evidence evidence)` returns `Uri?`. `GraphEvidenceRepositories(IReadOnlyDictionary<string, string> Map)` is registered as a singleton in `Program.cs` from `builder.Configuration.GetSection("EvidenceRepositories").Get<Dictionary<string, string>>() ?? []`. A JSON `null` or a missing section binds to an empty map. `GraphTestContext.Create` gains an optional `IReadOnlyDictionary<string, string>? evidenceRepositories` and registers this record. `OriginEvidence` accepts optional link resolution for the inspector, while other callers keep their current evidence display.

The owning provenance is the record's own `Provenance`, and the map key is `owner.Key.SourceId`. That matches how `src/workspace/bundle.mjs` builds records:
- A source assertion is owned by its source.
- A reviewed C4 binding is keyed under `fromSourceId` and carries that source's supporting evidence and commit.
- A `division-core` record carries the catalog commit and is linked through a `division-core` map entry.
- A reviewed link from `links/*.json` carries no evidence, so it always shows the explicit no-evidence message.

- [ ] In `GraphInspectorTests`, script published and runtime detail responses using the same endpoints and label. Assert that selecting the published `p:` ID sends `RelationshipDetailRequest.Key`, shows the pinned revision, evidence, direction and both endpoint sources, and that `r:` sends `RuntimeId` and shows workspace origin, author, custom explanation, and the existing history link. Resolve a slow published response after the runtime selection and assert the inspector remains on runtime. Select a neighbour's `n:` identity and assert one `EntityDetailRequest` for that exact key, the neighbour's source and revision, a **Follow** action, and no traversal request. Then select the focus and assert there is no **Follow** action. In a navigation test, assert that closing details preserves the two loaded pages and viewport. Add these resolver assertions using `var commit = new string('a', 40)` and `new Evidence("docs/order notes.md", 3, 4, "blob", "source")`.

```csharp
var key = new PublishedKey("fixture/ecommerce", "assertion", "https://example.test/graph#edge");
var owner = new Provenance(key, commit, "snapshot", null, []);
var repositories = new Dictionary<string, string> { [key.SourceId] = "https://github.com/team/commerce" };
Assert.Equal($"https://github.com/team/commerce/blob/{commit}/docs/order%20notes.md#L3-L4",
    GraphEvidenceLinks.TryCreate(repositories, owner, evidence)?.AbsoluteUri);
Assert.Null(GraphEvidenceLinks.TryCreate(new Dictionary<string, string>(), owner, evidence));
Assert.Null(GraphEvidenceLinks.TryCreate(repositories, owner, evidence with { Path = "../other.ttl" }));
repositories[key.SourceId] = "https://github.com@untrusted.example/team/commerce";
Assert.Null(GraphEvidenceLinks.TryCreate(repositories, owner, evidence));
var core = new Provenance(new PublishedKey("division-core", "concept", "https://example.test/core#Order"), commit, "snapshot", null, []);
Assert.Null(GraphEvidenceLinks.TryCreate(repositories, core, evidence));
repositories["division-core"] = "https://github.com/team/division";
Assert.StartsWith($"https://github.com/team/division/blob/{commit}/", GraphEvidenceLinks.TryCreate(repositories, core, evidence)?.AbsoluteUri);
```

  Add these Terraform runs to `workspace.tftest.hcl`:

```hcl
run "evidence_repository_map_reaches_web_settings" {
  command = plan
  variables {
    evidence_repositories = { "fixture/ecommerce" = "https://github.com/fixture/ecommerce" }
  }
  assert {
    condition     = jsondecode(aws_s3_object.web_settings.content).EvidenceRepositories["fixture/ecommerce"] == "https://github.com/fixture/ecommerce"
    error_message = "The evidence repository map must reach the served web settings."
  }
}

run "evidence_repository_map_rejects_non_github_roots" {
  command = plan
  variables {
    evidence_repositories = { "fixture/ecommerce" = "https://github.com@untrusted.example/team/commerce" }
  }
  expect_failures = [var.evidence_repositories]
}
```

- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~GraphInspectorTests --nologo)`. Expect missing inspector and link resolver.
- [ ] Compose detail rendering over existing `RelationshipStep`, `OriginEvidence`, and contribution displays where appropriate. Wire node and edge **Inspect** actions from the list and the diagram to the panel while keeping the frame in place. Guard detail completion by frame and selection identity and cancel superseded reads. A different returned snapshot marks the frame stale. For links, accept a configured HTTPS GitHub repository root with the expected owner/repo path and no credentials, port override, query or fragment. Require a pinned hex revision and a nonempty safe repository-relative path, encode its segments, and add a positive ordered line range when present. Link only from the provenance that owns the citation. Retain plain commit/path/lines/blob text and an unavailable-link message otherwise. Return focus to the initiating control when the inspector closes.
- [ ] Rerun `GraphInspectorTests` and `GraphExplorerNavigationTests`, then run `bash deploy/workspace/aws/verify-terraform.sh`. Render the home-lab configuration with `WORKSPACE_EVIDENCE_REPOSITORIES` unset and again set to `{"fixture/ecommerce":"https://github.com/fixture/ecommerce"}`. Use `docker compose -f deploy/workspace/home-lab/compose.yml config --format json` with the other required variables set to placeholder values. Pass each rendering through the extended `jq` check. Commit `feat: inspect graph relationship evidence` and push.

## Task 5 — Reuse workspace editing and reauthentication

**Files** Extend the inspector and explorer, `ContributionEditor.razor`, `ApiError.razor`, and `WorkspaceLinks.cs`. Create `GraphExplorerContributionTests.cs` in the existing runtime tests.

**Interfaces** The inspector receives session capability and `EventCallback<GraphContributionSaved> OnSaved`, where `GraphContributionSaved(ContributionResult Result, bool IsRelationship, bool IsRemoval)` records the editor action. Allow `ContributionEditor` and `ApiError` an optional same-origin sign-in return route. `WorkspaceLinks.GraphResume(PublishedKey focus, string direction, string action, Guid? contributionId)` builds the graph route plus typed query fields. Use `add-note`, `edit-note`, `remove-note`, `add-relationship`, `edit-relationship`, and `remove-relationship` as the allowed actions. Edit/remove actions require an ID, and add actions require none. The inspector resumes only after authorization and a matching record lookup.

- [ ] Add these tests using `GraphTestContext.Create` and the existing bUnit editor patterns. Extend the helper with any further service the editor needs, rather than adding registrations to individual tests. Stub session, inventory, entity and relationship detail, traversal, and real command shapes. Capture POST/PUT bodies and return real `ContributionResult` or workspace problem bodies.

  | Test | Assert |
  | --- | --- |
  | `ReaderHasNoContributionControls` | `CanManage = false` hides editor actions and a forged resume hint does not open one or send a write. A manager can open the supported editor modes for an available graph record. |
  | `CreatesANoteAndDirectedRelationshipFromTheGraph` | Use separate source-qualified selected endpoints and explicit direction. Verify POST target keys, published type or custom explanation, and the saved-record ID remains accessible even if absent from the reloaded first page. |
  | `SuccessfulWritesRefreshFromPageOneWithoutRepeatingTheWrite` | After edit and removal assert real command IDs and expected versions, the same graph focus and direction after refresh, and `Cursor == null`. Simulate a refresh failure after a successful create. Assert saved confirmation still appears and read retry sends no additional write. |
  | `RejectedSaveKeepsDraftAndVersionConflictRequiresReview` | Use a version conflict with current revision 2, an unavailable endpoint, 401, and 403. Assert the original inputs remain and no success callback runs. In the conflict case submit only after explicit review and verify expected version 2. |
  | `SignInResumeRestoresTheInterruptedEditor` | Start at one route entry, follow to a different focus, and receive 401 while editing a runtime edge. Assert the return route points at the current focus and valid editor action/ID and omits draft text. Restore session storage and reopen the editor by ID even when the edge is past page one. A missing/removed record retains the draft with a recovery notice and performs zero writes. |

- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~GraphExplorerContributionTests --nologo)`. Expect the graph editor and recovery assertions to fail.
- [ ] Reuse the existing editor modes, expected-version flow, and draft key shape. Use `InventoryChoices.Load` only in relationship editors, preserving the selected keys on rejection. Start a relationship from the focused entity and show both selected endpoints and direction. For note editing resolve the matching active annotation on the focused entity; for relationship editing resolve the exact runtime ID. Pass the resume URL to the sign-in flow only from the graph host, and construct it from validated keys and known action names. Preserve retained tab drafts when a resume lookup fails. Leave the editor closed for readers. Use the existing API authorization and validation for every save. On confirmed writes invalidate older graph frames and request page one of the current successful query. Keep saved record details or its history reachable even when absent from the current page, and handle follow-up read errors separately from save results.
- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter 'FullyQualifiedName~GraphExplorerContributionTests|FullyQualifiedName~WorkspaceUiTests|FullyQualifiedName~GraphInspectorTests' --nologo)`. Commit `feat: edit runtime contributions from the graph` and push.

## Task 6 — Verify the shipped browser experience

**Files** Add `deploy/workspace/verify-graph-explorer.mjs` as an importable scenario. Extend the non-persistence path in `deploy/workspace/verify-browser.mjs` and retain the checkouts under the existing disposable `WORKSPACE_SCENARIO_DIR` in `deploy/workspace/local/smoke.sh`. The same initial scenario runs on the home-lab harness through its existing `verify-browser.mjs` call.

**Interfaces** Export `verifyGraphExplorer({ manager, reader, origin, snapshot, fixturePath, writes })`. Reuse the existing browser clients (`page`, `request`) and the known published fixture identity. `fixturePath` points at the synthetic `fixture.json` on local runs. `writes` controls the scenarios that write to the journal (the parallel edge, many relationships and runtime writes).

- **Mac.** `verify-browser.mjs` passes `writes: true`. The deployment is disposable, and smoke deletes it on exit.
- **Home lab.** It passes `writes: process.env.WORKSPACE_VERIFY_GRAPH_WRITES === '1'`. That fixture deployment keeps a permanent journal, and each write run adds history that cannot be removed. Without the flag, home lab still runs the entry, handoff, keyboard, canvas, evidence, coverage, reader-refusal and static-delivery checks. Deployed paging and authorization remain covered by the existing `verify-traversal-paging.mjs home-lab` run in `acceptance-home-lab.mjs`.

The scenario prints one JSON result line per scenario, with `status` set to `pass` or `skipped` and the reason, so skipped write scenarios are visible in the build handoff. Record the local Turtle comparison separately.

Locate canvas targets with the bridge's hook, never with fixed pixels:

```javascript
async function canvasPoint(page, id) {
  const host = page.locator('[data-graph-diagram]');
  const box = await host.boundingBox();
  const point = await host.evaluate(async (element, id) =>
    (await import(new URL('graph-diagram.mjs', document.baseURI).href)).diagramFor(element)?.renderedPosition(id), id);
  assert.ok(box && point, `diagram position for ${id}`);
  return { x: box.x + point.x, y: box.y + point.y };
}
```

`GraphDiagram` renders its host with the `data-graph-diagram` attribute. Blazor resolves the component's `./graph-diagram.mjs` against `document.baseURI` (`<base href="/">`), and the harness resolves the same absolute URL. Browsers key modules by absolute URL, so `diagramFor` reads the instance the page mounted. Do not use a bare relative `import('./graph-diagram.mjs')` here: on `/graph/{key}` it would resolve under `/graph/`.

- [ ] Write the browser assertions below. Wire the scenario into a temporary copy of the browser harness against the pre-feature main image or checkout and verify failure at direct navigation/module load. Keep this red run within the disposable workspace; it must not target a shared data deployment. For a green fixture use the exact manifest produced by the seed script for the checkout tree under the scenario directory. Assert that the browser reads that `snapshot` before seeding any contributions.

  | Scenario | Browser verification |
  | --- | --- |
  | Entry, handoff, keyboard and viewport | From search open Ecommerce **Order processing**. Follow incoming containment through **Storefront API** to **Ecommerce**, then traverse the directed handoff and reviewed binding to the actual `fixture/warehouse` system. Verify Back restores the prior focus and loaded controls. On a known neighbour, click once at `canvasPoint` with `page.mouse.click` and assert the inspector shows that neighbour while the focus is unchanged. Then double-click it with `page.mouse.dblclick` and assert it becomes the focus. Repeat the inspect and follow journey through keyboard controls only. Check two **Order** records remain distinct. |
  | Evidence and coverage | Read `/appsettings.json` from the origin. For the published handoff edge, assert the inspector shows a commit-pinned GitHub link when `EvidenceRepositories` has an entry for the edge's `SourceId`. Otherwise assert it shows the plain citation and the unavailable-link message. For the current focus, read the reader's own `/api/traverse` page one. Compute relevant gaps with the Task 1 rule, then assert the page shows exactly those as source coverage, shows the remaining count as gaps elsewhere, and never claims isolation while a relevant gap exists. |
  | Parallel edge (writes) | Create a temporary manager-authored edge with the same endpoints and label as a known published handoff edge. Inspect each by ID and origin through the list. Assert published pinned evidence and workspace explanation/history are independently visible. Assert the two edges' `canvasPoint` values differ, then click each and assert each opens its own record. |
  | Many relationships and live staleness (writes) | Seed 101 temporary relationships, the smallest count that exceeds the 100-path page, around Ecommerce **Order** through the manager client, use unique labels for this run, and read as a separate reader. Assert **Load more** reaches a relationship absent from the first page without changing focus or pan/zoom. On a fresh traversal with a next cursor, post a separate manager edit, then click **Load more** and assert stale status and Reload. Assert Reload restarts the same direction and replaces stale results. Remove all seeded test relationships using their IDs and current versions in `finally`. |
  | Runtime writes (writes) | As manager, create a note and a relationship using graph controls, then edit the relationship. After each save, verify the separate reader sees the expected values. Remove the test contributions after verifying history. |
  | Reader refusal | Assert reader controls remain read-only and the existing API refuses reader writes. This runs in every environment, because a refused write leaves no journal entry. |
  | Read-only inputs | Before the scenario and in `finally`, record the active `snapshotId` reported by a reader page-one traversal. When `fixturePath` is present, also read `divisionRoot` and `checkouts` from it, and record `git rev-parse HEAD`, `git status --porcelain`, and the SHA-256 of every tracked `.ttl` file in each checkout. List the files with `git ls-files -z -- '*.ttl'` and hash them with binary file reads. Assert the snapshot IDs are equal and still match the `snapshot` the harness activated. Assert the revision and hash maps are identical, porcelain output is empty both times, and the maps include the adopting core Turtle and all participating sources. The snapshot check is the meaningful one: the API and publisher containers never mount the checkouts, so the checkout comparison only shows that the walkthrough did not write to the inputs out of band. |
  | Static delivery | Make an independent browser visit to `/graph/{encodedKey}` and await the graph heading. Verify same-origin graph-module and vendor-module requests succeed with `text/javascript` or `application/javascript`. Check the focused route still works with native controls and shows the startup-failure message when the renderer request is deliberately blocked. AWS has no browser run here; its module type is proven by the Task 3 Terraform assertion. |

- [ ] In `local/smoke.sh` use `node "$root/src/workspace/seed-fixture.mjs" "$scenario/checkouts" "$scenario/bundle"` to derive the manifest. Pass the bundle path and digest to the existing `bootstrap.sh` interface. Pass `join(scenario, 'checkouts', 'fixture.json')` and `writes: true` to the graph scenario from `verify-browser.mjs` for Mac. For home lab, omit `fixturePath` and set `writes` from `WORKSPACE_VERIFY_GRAPH_WRITES`. Keep the old persistence and replay smoke paths intact. Capture the baseline TTL hashes before the graph makes its first seeded write, and check them after cleanup.
- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter 'FullyQualifiedName~GraphNeighbourhoodTests|FullyQualifiedName~GraphExplorerNavigationTests|FullyQualifiedName~GraphInspectorTests|FullyQualifiedName~GraphExplorerContributionTests|FullyQualifiedName~WorkspaceUiTests|Category=OpenApi' --nologo)`, `npm test`, `bash deploy/workspace/local/smoke.sh`, and `bash deploy/workspace/aws/verify-terraform.sh`. Run `bash deploy/workspace/aws/package.sh "$new_package_dir"` with an absolute nonexistent output path and verify the web entry, graph bridge, vendor module, and license. `verify-terraform.sh` must include the `.mjs` content-type assertion and both evidence-map runs. Confirm the API spec and catalog inputs have no diff against the build branch's merge base. Run the home-lab browser scenario when an authorized fixture deployment is available and distinguish results from local and AWS package/simulation checks.
- [ ] Commit `test: verify graph explorer in workspace browser smoke` and push. Report the six task commits, the focused and integrated test results, any unexercised deployment targets, and all B1–B15 evidence in the build handoff.
