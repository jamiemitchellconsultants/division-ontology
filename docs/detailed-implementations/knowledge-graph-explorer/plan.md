# Knowledge Graph Explorer — detailed implementation plan

**Inputs** [Task plan](../../superpowers/plans/knowledge-graph-explorer/plan.md) · [Vision](../../features/knowledge-graph-explorer/01-vision.md) · [Behaviours](../../features/knowledge-graph-explorer/02-behaviours.md) · [Architecture](../../features/knowledge-graph-explorer/03-architecture.md)

**Build branch** `feature/knowledge-graph-explorer/dev-build`. Start after the plan is reviewed and merged and the build gate opens. Perform each task separately, add its tests first, and see the expected failure before implementation. Run the focused suite again and `npm test`, review `git diff --cached --check` and the staged diff, then commit and push using that task's message. Stage only that task's files. Use `git push -u origin HEAD` for the first push and `git push` for subsequent tasks. Use `cd runtime` for .NET commands so the pinned SDK applies. Generated `dist/` files belong to catalog sync.

## Task 1 — Project traversal pages into a graph neighbourhood

**Files** Create `runtime/RuntimeKnowledge.Web/Services/GraphNeighbourhood.cs` and `runtime/RuntimeKnowledge.Tests/GraphNeighbourhoodTests.cs`.

**Interfaces** In `RuntimeKnowledge.Web.Services`, define `GraphQuery(PublishedKey Focus, string Direction)` with `TraversalRequest ToRequest(string? cursor = null)`, and `GraphEdge(string Id, TraversalStep Step)`. `GraphNeighbourhood.FromPage(GraphQuery query, TraversalResult page)` and `GraphNeighbourhood.Append(TraversalResult page)` return neighbourhoods with `Query`, `SnapshotId`, `Nodes` (`IReadOnlyDictionary<PublishedKey, TraversalEntity>`), `Edges` (`IReadOnlyList<GraphEdge>`), `Gaps`, `NextCursor`, and `TraversalIncomplete` (the response's `Truncated`). Appending returns a new neighbourhood so Back history can retain the old record set.

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

    private static TraversalResult Page(string? cursor, params TraversalStep[] edges) =>
        new("snapshot", edges.Select(step => new TraversalPath([step])).ToArray(), [], cursor is not null,
            edges.Length == 0 ? [Entity(Ecommerce)] : [Entity(Ecommerce), Entity(Warehouse)], cursor);

    private static TraversalEntity Entity(PublishedKey key) =>
        new(key, "Order", new Provenance(key, "commit", "snapshot", null, []), true, []);
}
```

- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~GraphNeighbourhoodTests --nologo)`. Expect compilation to fail because the projection types are absent.
- [ ] Project one step per traversal path, keyed by `p:` plus `WorkspaceLinks.Encode(publishedKey)` or `r:` plus the canonical `Guid` in `D` format. Keep first appearance order, deduplicate on the exact edge ID, and merge nodes by `PublishedKey`. Copy fields from the current page for refreshed entity context, combine distinct gaps, and use the latest page's continuation and truncation. A mismatched snapshot throws `InvalidOperationException` before append. Extend `EmptyContinuationAdvancesAndAnEmptyFinalPageRetainsTheFocus` with a loaded edge and a `CoverageGap` and assert they survive a subsequent empty final page. Add a test for `Truncated = true` with no cursor and assert both fields remain available for the page to classify separately.
- [ ] Rerun the focused command. Commit `feat: project one-hop workspace graph pages` and push.

## Task 2 — Wire entry links, navigation, and accessible paging

**Files** Modify the entry pages, `WorkspaceLinks`, and `WorkspaceClient` listed in the task plan. Create `Pages/GraphExplorer.razor`, `Components/GraphNeighbourList.razor`, `GraphExplorerNavigationTests.cs` in the existing runtime test project, and the Node route test. Modify the existing SPA route matcher in `deploy/workspace/aws/cdn.tf`.

**Interfaces** `WorkspaceLinks.Graph(PublishedKey key)` returns `graph/{Encode(key)}`. `GraphExplorer` exposes route parameter `Key`. `GraphNeighbourList` takes a `GraphNeighbourhood`, `EventCallback<PublishedKey> OnFollow`, and `EventCallback<string> OnInspectEdge`. `WorkspaceClient.Traverse(TraversalRequest, CancellationToken cancellationToken = default)`, `Entity(PublishedKey, CancellationToken cancellationToken = default)`, and `Relationship(RelationshipDetailRequest, CancellationToken cancellationToken = default)` pass the token to the existing HTTP send and JSON read. Existing single-argument calls still compile.

- [ ] Use the `BunitContext` and stub `HttpMessageHandler` pattern in `WorkspaceUiTests.cs` for `GraphExplorerNavigationTests`. Return `WorkspaceSession(true, false)` for `/api/session`. For traversal, record the decoded `TraversalRequest` and return a scripted response or a `TaskCompletionSource<HttpResponseMessage>` created with `RunContinuationsAsynchronously`. Use an intentionally uncancellable completion in the delayed-response case to prove the generation guard separately from cancellation. Give the toolbar distinct accessible names **Direction**, **Back**, **Load more**, **Reload**, and **Retry**, and each neighbour row separate **Follow** and **Inspect relationship** actions with source-qualified names. Add these cases before implementation.

  | Test | Arrange and assert |
  | --- | --- |
  | `LinksSelectTheExactAvailablePublishedEntity` | Two **Order** search results with different sources link to different `graph/{key}` values. The entry and entity page select the exact encoded key. An unavailable entity detail and a relationship type omit the graph link. A malformed direct entry shows recovery and performs zero traversal requests. |
  | `FirstPageAndDirectionChangesUseOneHop` | First request equals `new TraversalRequest(ecommerce, "both", 1, false)`. Selecting incoming makes an incoming request with `Cursor == null`, shows connections for that response, and leaves the original arrow endpoints intact in the row. |
  | `EmptyPageKeepsLoadMoreAndFinalPageKeepsEarlierEdges` | Script one edge and cursor `first`, zero edges and cursor `second`, then a final empty page. Assert request cursors are `null`, `first`, `second`, all query fields are otherwise equal, and the initial edge remains visible through the final page. Assert **Load more** disappears only at the end. |
  | `CoverageEmptyAndIncompleteStatesAreIndependent` | Run a completed empty response, a completed empty response with a real gap, an empty response with a cursor, and `Truncated = true` without a cursor. Assert, respectively, no visible connections, coverage with no claim of isolation, **Load more** with no final-empty text, and an incomplete/unsupported paging notice. |
  | `BackRestoresTheLoadedFrame` | Load page two, follow a neighbour, then Back. Assert the original focus, direction, both loaded edges and selection return and no traversal request is made for Back. Extend during Task 3 to assert saved pan and zoom. |
  | `StaleCursorRequiresReloadAndInvalidatesBackFrames` | Open a neighbour frame with cursor, return `409 staleCursor`, and assert the original frame stays visible and marked out of date and **Load more** is unavailable. Reload sends the saved query without a cursor and replaces old rows. Back thereafter reads page one for the invalidated saved focus and preserves its direction. Script a missing saved focus and assert a route to search. |
  | `TransientContinuationRetriesTheSameCursor` | Fail continuation once with 503, assert the frame stays visible without an out-of-date label, and verify **Retry** uses the same cursor. Script `422 invalidCursor` in a second case and assert **Reload** uses page one. |
  | `LateResponsesCannotReplaceTheLatestQuery` | Delay the incoming response, complete outgoing first, then complete incoming. Assert only outgoing rows are visible and Back history contains no superseded frame. Separately fail the latest follow with 503 and verify retry and return to the last successful frame. |

  Start the navigation test file with the entry request test and its self-contained handler. Extend the handler with the specific page, problem, and delayed-response fixtures in the table.

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
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
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var requests = new ConcurrentQueue<TraversalRequest>();
        using var http = new HttpClient(new ReadHandler(focus, requests)) { BaseAddress = new Uri("https://workspace.test") };
        context.Services.AddSingleton(new WorkspaceClient(http));
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
- [ ] Add the authorized route and links. Decode and allow `concept` and `node`; load session capability and the first successful frame. Let the list use native links or buttons and a status message for loading and completion. Set the start direction to `both`. Append under the exact query and cursor, one in-flight continuation per frame. Keep successful frames in memory. Build a Back entry only upon a successful follow and restore it unless invalidated. Changing direction starts page one at the existing focus. Maintain a monotonically increasing request ID and a cancellation token for superseded traversal reads; apply effects only from the matching request. Keep a retry target for a failed transition. `staleCursor`, a snapshot-mismatched continuation, and a known later snapshot mark earlier frames stale; `invalidCursor` also clears continuation and offers Reload. A temporary network error retains its cursor for Retry. Clear state when the page is disposed. Add `graph` to the CloudFront rewrite alternatives, with the same segment boundary as the other routes.
- [ ] Rerun both focused commands and `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~WorkspaceUiTests --nologo)`. Commit `feat: navigate paged graph neighbourhoods` and push.

## Task 3 — Draw the shared neighbourhood with Cytoscape

**Files** Add `GraphDiagram.razor`, `wwwroot/graph-diagram.mjs`, and the versioned vendor assets under the web project. Update `GraphExplorer.razor` to host the diagram and `wwwroot/workspace.css` for its container and controls. Add `test/graph-explorer-renderer.test.mjs`.

**Interfaces** `GraphDiagram` takes the current `GraphNeighbourhood`, `EventCallback<PublishedKey> OnFollow`, and `EventCallback<string> OnInspectEdge`. It exposes `CaptureViewportAsync()`, `RestoreViewportAsync(GraphViewport)`, `ZoomByAsync(double factor)`, and `ResetZoomAsync()` to the explorer and owns disposal. `GraphViewport` carries `Pan`, `Zoom`, and `Positions`, using `GraphPoint(double X, double Y)` for pan and positions. The module exports `mount(host, dotNetCallbacks)`, `placeNeighbours(ids, previous)`, and `bindGraphActions(cy, callbacks)`. `mount` returns methods `render(model)`, `captureViewport()`, `restoreViewport(viewport)`, `zoomBy(factor)`, `resetZoom()`, and `dispose()`. The explorer calls restore once after rendering a Back frame. Identity passed to the diagram uses `n:` plus the encoded published key for nodes and the existing `GraphEdge.Id` for edges.

- [ ] Write `test/graph-explorer-renderer.test.mjs` from the test file below. Add a bUnit viewport restoration assertion to `BackRestoresTheLoadedFrame`. Set the JS capture result to a non-default zoom and pan and verify those values are supplied on Back after the frame is rendered.

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
  const { placeNeighbours, bindGraphActions } = await import(new URL('graph-diagram.mjs', web));
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
      inspectEdge: id => actions.push(['inspectEdge', id])
    });
    for (const id of ['n:b', 'p:assertion', 'r:contribution']) cy.getElementById(id).emit('tap');
    assert.deepEqual(actions, [['follow', 'n:b'], ['inspectEdge', 'p:assertion'], ['inspectEdge', 'r:contribution']]);
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

- [ ] Import the same-origin `.mjs` on first graph render from a dedicated sized container. In the diagram model send display labels, origin, endpoints and IDs. Use Cytoscape preset coordinates with focus at `(0, 0)`. Allocate neighbours in first-seen order on rings whose slots have fixed angles. Start with 8 slots at radius 180 layout units and add 8 slots and 180 units of radius per ring. Preserve previous slots on append. Use arrowheads from source to target and curved parallel edges. Batch new elements and fit only on the first view of a new focus. Save and restore positions, zoom and pan on Back. Ignore callbacks for IDs outside the current view; report diagram startup failure alongside the native list. Wire keyboard toolbar buttons to zoom in, zoom out, and reset, and release the instance and .NET/JS references on dispose.
- [ ] Rerun the renderer and navigation tests. Run `(cd runtime && dotnet publish RuntimeKnowledge.Web -c Release -o "$graph_publish_dir")` with `graph_publish_dir` set to an absolute new temporary directory. Verify the published `wwwroot/graph-diagram.mjs` and vendor paths exist and the module and license still match both hashes. Commit `feat: render workspace graph with local Cytoscape` and push.

## Task 4 — Inspect published and workspace records

**Files** Create `GraphInspector.razor` and `Services/GraphEvidenceLinks.cs` in the web project and `GraphInspectorTests.cs` in the runtime tests. Extend the explorer, `OriginEvidence.razor`, and `wwwroot/appsettings.json`.

**Interfaces** `GraphInspector` accepts `PublishedKey Focus`, optional `GraphEdge SelectedEdge`, and a close callback. The panel loads focus detail when no edge is selected, or relationship detail for the selected edge. `GraphEvidenceLinks.TryCreate(IReadOnlyDictionary<string, string> repositories, Provenance owner, Evidence evidence)` returns `Uri?`. Allow an optional `EvidenceRepositories` object in Blazor configuration. `OriginEvidence` accepts optional link resolution for the inspector, while other callers keep their current evidence display.

- [ ] In `GraphInspectorTests`, script published and runtime detail responses using the same endpoints and label. Assert that selecting the published `p:` ID sends `RelationshipDetailRequest.Key`, shows the pinned revision, evidence, direction and both endpoint sources, and that `r:` sends `RuntimeId` and shows workspace origin, author, custom explanation, and the existing history link. Resolve a slow published response after the runtime selection and assert the inspector remains on runtime. In a navigation test assert closing details preserves the two loaded pages and viewport. Add these resolver assertions using `var commit = new string('a', 40)` and `new Evidence("docs/order notes.md", 3, 4, "blob", "source")`.

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
```

- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter FullyQualifiedName~GraphInspectorTests --nologo)`. Expect missing inspector and link resolver.
- [ ] Compose detail rendering over existing `RelationshipStep`, `OriginEvidence`, and contribution displays where appropriate. Wire node and edge actions to the panel while keeping the frame in place. Guard detail completion by frame and selection identity and cancel superseded reads. A different returned snapshot marks the frame stale. For links, accept a configured HTTPS GitHub repository root with the expected owner/repo path and no credentials, port override, query or fragment. Require a pinned hex revision and a nonempty safe repository-relative path, encode its segments, and add a positive ordered line range when present. Link only from the provenance that owns the citation. Retain plain commit/path/lines/blob text and an unavailable-link message otherwise. Return focus to the initiating control when the inspector closes.
- [ ] Rerun `GraphInspectorTests` and `GraphExplorerNavigationTests`. Commit `feat: inspect graph relationship evidence` and push.

## Task 5 — Reuse workspace editing and reauthentication

**Files** Extend the inspector and explorer, `ContributionEditor.razor`, `ApiError.razor`, and `WorkspaceLinks.cs`. Create `GraphExplorerContributionTests.cs` in the existing runtime tests.

**Interfaces** The inspector receives session capability and `EventCallback<GraphContributionSaved> OnSaved`, where `GraphContributionSaved(ContributionResult Result, bool IsRelationship, bool IsRemoval)` records the editor action. Allow `ContributionEditor` and `ApiError` an optional same-origin sign-in return route. `WorkspaceLinks.GraphResume(PublishedKey focus, string direction, string action, Guid? contributionId)` builds the graph route plus typed query fields. Use `add-note`, `edit-note`, `remove-note`, `add-relationship`, `edit-relationship`, and `remove-relationship` as the allowed actions. Edit/remove actions require an ID, and add actions require none. The inspector resumes only after authorization and a matching record lookup.

- [ ] Add these tests using the existing bUnit editor patterns. Stub session, inventory, entity and relationship detail, traversal, and real command shapes. Capture POST/PUT bodies and return real `ContributionResult` or workspace problem bodies.

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

**Interfaces** Export `verifyGraphExplorer({ manager, reader, origin, snapshot, fixturePath })`. Reuse the existing browser clients (`page`, `request`) and the known published fixture identity. `fixturePath` points at the synthetic `fixture.json` on local runs. Home-lab runs without the source checkout manifest still exercise the deployed route, diagram, auth, paging, and contributions. Record the local Turtle comparison separately.

- [ ] Write the browser assertions below. Wire the scenario into a temporary copy of the browser harness against the pre-feature main image or checkout and verify failure at direct navigation/module load. Keep this red run within the disposable workspace; it must not target a shared data deployment. For a green fixture use the exact manifest produced by the seed script for the checkout tree under the scenario directory. Assert that the browser reads that `snapshot` before seeding any contributions.

  | Scenario | Browser verification |
  | --- | --- |
  | Entry, handoff, keyboard and viewport | From search open Ecommerce **Order processing**. Follow incoming containment through **Storefront API** to **Ecommerce**, then traverse the directed handoff and reviewed binding to the actual `fixture/warehouse` system. Verify Back restores the prior focus and loaded controls. Use an actual canvas pointer event against a known neighbour in a simple fixture and then repeat the follow and inspect journey through keyboard controls. Check two **Order** records remain distinct. |
  | Parallel edge and evidence | Create a temporary manager-authored edge with the same endpoints and label as a known published handoff edge. Inspect each by ID and origin through the list. Assert published pinned evidence and workspace explanation/history are independently visible. Run Task 3's distinct hit-action assertions for the diagram. |
  | Many relationships and live staleness | Seed 105 temporary relationships around Ecommerce **Order** through the manager client, use unique labels for this run, and read as a separate reader. Assert **Load more** reaches a relationship absent from the first page without changing focus or pan/zoom. On a fresh traversal with a next cursor, post a separate manager edit, then click **Load more** and assert stale status and Reload. Assert Reload restarts the same direction and replaces stale results. Remove all seeded test relationships using their IDs and current versions in `finally`. |
  | Runtime writes and read-only inputs | As manager create a note and relationship using graph controls, edit the relationship, and verify the separate reader sees the expected values after each save. Assert reader controls remain read-only and the existing API refuses reader writes. Remove the test contributions after verifying history. When `fixturePath` is present, read `divisionRoot` and `checkouts` from it. Before the scenario and in `finally`, record `git rev-parse HEAD` and SHA-256 of every tracked `.ttl` file in each checkout, using `git ls-files -z -- '*.ttl'` and binary file reads. Assert both maps are identical and include the adopting core Turtle and all participating sources. |
  | Static delivery | Make an independent browser visit to `/graph/{encodedKey}` and await the graph heading. Verify same-origin graph-module and vendor-module requests succeed with `text/javascript` or `application/javascript`. Check the focused route still works with native controls when the renderer request is deliberately blocked. |

- [ ] In `local/smoke.sh` use `node "$root/src/workspace/seed-fixture.mjs" "$scenario/checkouts" "$scenario/bundle"` to derive the manifest. Pass the bundle path and digest to the existing `bootstrap.sh` interface. Pass `join(scenario, 'checkouts', 'fixture.json')` to the graph scenario from `verify-browser.mjs` for Mac, and omit it for home lab. Keep the old persistence and replay smoke paths intact. Capture the baseline TTL hashes before the graph makes its first seeded write, and check them after cleanup.
- [ ] Run `(cd runtime && dotnet test RuntimeKnowledge.Tests --filter 'FullyQualifiedName~GraphNeighbourhoodTests|FullyQualifiedName~GraphExplorerNavigationTests|FullyQualifiedName~GraphInspectorTests|FullyQualifiedName~GraphExplorerContributionTests|FullyQualifiedName~WorkspaceUiTests|Category=OpenApi' --nologo)`, `npm test`, `bash deploy/workspace/local/smoke.sh`, and `bash deploy/workspace/aws/verify-terraform.sh`. Run `bash deploy/workspace/aws/package.sh "$new_package_dir"` with an absolute nonexistent output path and verify the web entry, graph bridge, vendor module, and license. Confirm the API spec and catalog inputs have no diff against the build branch's merge base. Run the home-lab browser scenario when an authorized fixture deployment is available and distinguish results from local and AWS package/simulation checks.
- [ ] Commit `test: verify graph explorer in workspace browser smoke` and push. Report the six task commits, the focused and integrated test results, any unexercised deployment targets, and all B1–B15 evidence in the build handoff.
