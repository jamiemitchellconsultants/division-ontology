# I4. Workspace search and trace: detailed implementation plan

**Inputs:**
- [Behaviours](../../features/generalise-impact-assessment/02-behaviours.md): B15, B16, B28 and B29 are delivered here, respecting Q1, Q2, Q6 and Q7.
- ADRs [0030](../../adr/0030-hold-evidence-excerpts-in-a-per-snapshot-impact-model.md), [0008](../../adr/0008-rebuild-workspace-from-snapshots-and-events.md), [0018](../../adr/0018-give-refusals-fixed-problem-details-bodies.md), [0019](../../adr/0019-carry-the-traversal-frontier-in-the-cursor.md) and [0020](../../adr/0020-bind-traversal-cursors-to-snapshot-and-journal.md).
- [Task overview](../../superpowers/plans/generalise-impact-assessment/plan.md).

**Start:** I3 is done, its handoff is posted, and the database test container is available:

```bash
bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter Category=Database
```

Database-backed tests carry `[Trait("Category", "Database")]` and use `Scenario.OpenAsync()`. API tests use `ApiFixture.OpenAsync(provider)` and `ClientWithToken(["ReadKnowledgeGraph"])`, following `WorkspaceScenarioTests`.

**Spec discipline:** every task that changes a route regenerates `runtime/openapi/workspace-api.json` with `dotnet build runtime/RuntimeKnowledge.Api`, and commits it in the same commit. `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi` must pass.

---

## Task 22: Impact model storage

**Files:**
- create `runtime/RuntimeKnowledge.Store/Sql/006_impact_models.sql`, `runtime/RuntimeKnowledge.Impact/Bundle/ModelPackage.cs` and `runtime/RuntimeKnowledge.Store/ImpactModels.cs`;
- modify `RuntimeKnowledge.Store.csproj` (a `ProjectReference` to Impact), `BundleReader.cs` and `Snapshots.cs`;
- create `runtime/RuntimeKnowledge.Tests/ImpactModelStorageTests.cs`.

### 22.1 Migration

```sql
CREATE TABLE workspace_projection.impact_models (
    generation uuid NOT NULL,
    snapshot_id text NOT NULL,
    package bytea NOT NULL,
    package_sha256 text NOT NULL CHECK (package_sha256 ~ '^[0-9a-f]{64}$'),
    PRIMARY KEY (generation, snapshot_id),
    FOREIGN KEY (generation, snapshot_id)
        REFERENCES workspace_projection.snapshots (generation, snapshot_id)
);
GRANT SELECT ON workspace_projection.impact_models TO workspace_api, workspace_publisher;
GRANT INSERT ON workspace_projection.impact_models TO workspace_publisher;
```

The stored value is a **model package**, not a serialised object graph. It holds the bundle objects that the engine needs:
- the manifest's `impactInput`, `relationshipMasterList`, `graphEvidence` and `inspectedSource` files;
- the manifest id, so that `ImpactBundle.FromObjects` re-verifies every digest on load.

The model built from a package is therefore the model the command line builds from the same bundle (B23).

### 22.2 `ModelPackage`

```csharp
namespace RuntimeKnowledge.Impact.Bundle;
public static class ModelPackage
{
    public static readonly string[] Roles = ["impactInput", "relationshipMasterList", "graphEvidence", "inspectedSource"];
    /// <summary>Deterministic, ZLib-compressed package of a manifest and its impact objects.</summary>
    public static (byte[] Package, string Sha256) Create(string manifestId, byte[] manifestBytes, Func<string, byte[]> blob);
    public static ImpactBundle Open(byte[] package);
}
```

The uncompressed layout is UTF-8 JSON:

```json
{"manifestId":"...","manifest":"<base64>","objects":[{"sha256":"...","bytes":"<base64>"}]}
```

`objects` is sorted by `sha256` and written with `EcmaJson.Stringify`. `Sha256` is taken over the uncompressed bytes. Compress with `ZLibStream` at `CompressionLevel.SmallestSize`.

### 22.3 Staging

- `BundleReader.ReadAsync` keeps the bytes of files whose role is in `ModelPackage.Roles`, and the manifest bytes, on `VerifiedBundle`. It gains `byte[] ManifestBytes` and `IReadOnlyDictionary<string, byte[]> ImpactObjects`, keyed by `sha256`.
- `SnapshotStore.StageProjectionAsync` runs after the existing inserts. When the manifest has an `impactInput` file:
  1. create the package;
  2. build `ImpactModel.Build(ModelPackage.Open(package))` to validate it, converting any `ImpactException` into `WorkspaceException("invalidBundle", "Impact input is invalid: " + message)`;
  3. `INSERT ... ON CONFLICT DO NOTHING` into `impact_models`.

  This runs in the same transaction. Replay calls the same method, so it rebuilds models (Task 23).
- `ImpactModels.ReadPackageAsync(NpgsqlTransaction tx, Guid generation, string snapshotId) → byte[]?`.

### 22.4 `ImpactModelStorageTests.cs`

```csharp
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Store;
using RuntimeKnowledge.Tests.Impact;
using Xunit;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class ImpactModelStorageTests
{
    [Fact]
    public async Task Staging_a_bundle_with_impact_input_stores_one_verified_model()
    {
        await using var scenario = await Scenario.OpenAsync();
        var id = scenario.Bundles.AddCorpusBundle("confluence");           // copies a corpus manifest and its blobs into MemoryBundles
        await scenario.Store.StageAsync(id, CancellationToken.None);
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var tx = await connection.BeginTransactionAsync();
        var state = await ReadStateAsync(tx, CancellationToken.None);
        var package = await ImpactModels.ReadPackageAsync(tx, state.Generation, id, CancellationToken.None);
        Assert.NotNull(package);
        var model = ImpactModel.Build(ModelPackage.Open(package!));
        Assert.Equal(["fixture/operations", "fixture/policies"], model.Sources.Select(s => s.Id));
    }

    [Fact]
    public async Task A_bundle_without_impact_input_stores_no_model()
    {
        await using var scenario = await Scenario.OpenAsync();
        var id = scenario.Bundles.AddSnapshot("initial");                    // existing helper: no impactInput role
        await scenario.Store.StageAsync(id, CancellationToken.None);
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var tx = await connection.BeginTransactionAsync();
        var state = await ReadStateAsync(tx, CancellationToken.None);
        Assert.Null(await ImpactModels.ReadPackageAsync(tx, state.Generation, id, CancellationToken.None));
    }

    [Fact]
    public void A_package_round_trips_and_is_deterministic()
    {
        var id = CorpusPaths.FirstManifest("confluence", "search");
        var manifest = File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "manifests", id + ".json"));
        byte[] Blob(string sha) => File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "blobs", sha));
        var (first, sha1) = ModelPackage.Create(id, manifest, Blob);
        var (second, sha2) = ModelPackage.Create(id, manifest, Blob);
        Assert.Equal(sha1, sha2);
        Assert.Equal(first, second);
        Assert.Equal(id, ModelPackage.Open(first).Id);
    }
}
```

`ApiFixture.OpenAsync(string provider, Func<Scenario, Task> arrange)` is a new overload. It mirrors the existing `OpenAsync(provider)`, but calls `arrange(scenario)` in place of `ActivateAsync("initial")`.

`MemoryBundles.AddCorpusBundle(string scenario)` is a new test helper in `Scenario.cs`. It reads the scenario's first manifest from `CorpusPaths`, adds `manifests/<id>.json` and every blob it lists, and returns the id. The corpus manifests are full impact bundles, so `BundleReader` must accept them. They lack the `catalog`, `nquads`, `sourceInventory` and `projection` roles that the workspace requires, so `AddCorpusBundle` adds minimal valid objects for those four roles: an empty projection, the catalog bytes from the corpus, `""` for `nquads`, and `{"sources":[]}`. It rewrites the manifest and returns the new id.

Steps:
- [ ] Write the tests. Run them through `test-db.sh`, and confirm that they fail.
- [ ] Implement 22.1–22.3.
- [ ] Commit `feat: store impact models with snapshots (#198)` and push.

---

## Task 23: Replay rebuilds models

**Files:** modify `runtime/RuntimeKnowledge.Store/Replay.cs`, so `DigestAsync` includes the `package_sha256` of each impact model for the rebuilt generation, in `snapshot_id` order. Modify `runtime/RuntimeKnowledge.Tests/ReplayTests.cs`.

Add this test:

```csharp
[Fact]
[Trait("Category", "Database")]
public async Task A_rebuild_reproduces_impact_models()
{
    await using var scenario = await Scenario.OpenAsync();
    var id = scenario.Bundles.AddCorpusBundle("confluence");
    await scenario.Store.StageAsync(id, CancellationToken.None);
    var position = await scenario.Store.ActivateAsync(id, CancellationToken.None);
    var replay = new ReplayStore(scenario.Db, scenario.Bundles);
    var live = await replay.DescribeCurrentAsync(CancellationToken.None);
    var rebuilt = await replay.RebuildAsync(position, CancellationToken.None);
    Assert.Equal(live.StateDigest, rebuilt.StateDigest);
    await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
    await using var tx = await connection.BeginTransactionAsync();
    Assert.NotNull(await ImpactModels.ReadPackageAsync(tx, rebuilt.Generation, id, CancellationToken.None));
}
```

Steps:
- [ ] Write the test, and confirm that it fails: the digest is missing the models.
- [ ] Implement.
- [ ] Commit `feat: rebuild impact models on replay (#198)` and push.

---

## Task 24: Impact read API (B15, B16, Q2)

**Files:**
- create `runtime/RuntimeKnowledge.Contracts/Impact.cs`, `runtime/RuntimeKnowledge.Api/ImpactModelCache.cs`, `runtime/RuntimeKnowledge.Api/ImpactRoutes.cs`, `runtime/RuntimeKnowledge.Store/ImpactReads.cs` and `runtime/RuntimeKnowledge.Tests/ImpactApiTests.cs`;
- modify `Program.cs` (DI and `MapImpactRoutes`), `ProblemResponses.cs`, `WorkspaceOpenApi.cs`, `OpenApiExamples.cs` and `WorkspaceLinks.cs`, and regenerate the spec.

### 24.1 Contracts (`Impact.cs`)

```csharp
namespace RuntimeKnowledge.Contracts;

public sealed record ImpactKey(string SourceId, string NodeIri);
public sealed record ImpactSearchRequest(string? Query, string? ExactIri, string? SnapshotId = null, string? Cursor = null);
public sealed record ImpactPathsRequest(string SnapshotId, IReadOnlyList<ImpactKey> Starts, int? MaxDepth = null);
public sealed record ImpactViewRequest(string SnapshotId, ImpactKey Focus, string Level);

public sealed record PinnedSnapshot(string CatalogCommit, string CatalogSha256, IReadOnlyList<SourceRevision> Sources);
public sealed record SourceRevision(string Id, string Commit);
public sealed record MasterListIdentity(string Status, string? CatalogCommit = null, string? Sha256 = null);

public sealed record PageEvidence(string? Title, string? ConfluenceUrl, IReadOnlyList<string>? Lines);
public sealed record CitedEvidence(string Iri, string Path, int StartLine, int EndLine, string BlobId, string Role,
    PageEvidence? Page = null, IReadOnlyList<string>? Lines = null);          // Lines: workspace-only excerpt (B28, B38)
public sealed record LeadPageEvidence(string EvidenceIri, string Path, int StartLine, int EndLine,
    string? Title, string? ConfluenceUrl, IReadOnlyList<string>? Lines);
public sealed record ImpactLead(ImpactKey Key, string MatchedText, string MatchKind, string SourceCommit,
    bool Supported, string Reason, string Kind, IReadOnlyList<LeadPageEvidence>? PageEvidence = null);

public sealed record GapNode(string SourceId, string NodeIri);
public sealed record ImpactGap(string Code, string? Reason = null, string? SourceId = null, string? NodeIri = null,
    string? CoverageIri = null, string? Selector = null, string? Relationship = null, string? Term = null,
    string? SharedTerm = null, string? Path = null, string? EvidenceIri = null,
    IReadOnlyList<GapNode>? Nodes = null, int? Assertions = null);
public sealed record CoverageRecord(string SourceId, string Iri, string Selector, string Status, int Priority, string? Reason = null);
public sealed record NotFollowed(string Relationship, int Assertions);

public sealed record LinkCitation(string Source, string Local, string Shared, string Relation);
public sealed record ImpactStep(string Type,
    ImpactKey? Subject = null, ImpactKey? Object = null, string? Direction = null, string? AssertionIri = null,
    string? Relationship = null, string? Basis = null, IReadOnlyList<string>? EvidenceIris = null,
    IReadOnlyList<CitedEvidence>? Evidence = null, string? Commit = null,
    ImpactKey? From = null, ImpactKey? To = null, string? RegistryCommit = null,
    System.Text.Json.Nodes.JsonObject? IdentityEvidence = null,
    string? SharedTerm = null, IReadOnlyList<LinkCitation>? Links = null, string? CatalogCommit = null);
public sealed record ImpactPath(string Id, IReadOnlyList<ImpactStep> Steps);

public sealed record ImpactSearchResult(string SnapshotId, PinnedSnapshot Pinned, string RegistryCommit,
    MasterListIdentity MasterList, IReadOnlyList<CoverageRecord> Coverage, IReadOnlyList<ImpactLead> Leads,
    IReadOnlyList<ImpactGap> Gaps, string? NextCursor = null);
public sealed record ImpactPathsResult(string SnapshotId, PinnedSnapshot Pinned, string RegistryCommit,
    MasterListIdentity MasterList, IReadOnlyList<NotFollowed> NotFollowed, IReadOnlyList<CoverageRecord> Coverage,
    IReadOnlyList<ImpactPath> Paths, IReadOnlyList<ImpactGap> Gaps);
public sealed record C4ViewResult(string SnapshotId, System.Text.Json.Nodes.JsonObject View, string Markdown);
```

Mapping rules:
- Engine JSON is converted to these records with `JsonSerializer.Deserialize<T>(node, WorkspaceJson.Options)`, after renaming `snapshotId` to `pinned`. `SnapshotId` is the workspace manifest id.
- `MasterList` is `{status:'none'}` when the engine result has no `masterList`.
- `NotFollowed` is `[]` when absent.
- `CitedEvidence.Lines` is filled in by the API from `EvidenceText.Read` for **every** evidence record (B28, B38). It is `null` when the text is unavailable, and in that case the gap is already in `Gaps`.
- The view is returned as engine JSON with its Markdown. Its schema in the spec is `object`, with a description that points to the C4 pilot's view format. `JsonObject` keeps the C4 view's existing shape without duplicating it.

### 24.2 Store reads

`ImpactReads.ActiveAsync(Db db, CancellationToken token) → (Guid Generation, string SnapshotId)` opens a `RepeatableRead` transaction. It reads the state and calls `RequireSnapshot()`. `ImpactReads.PackageAsync(Db, Guid generation, string snapshotId, token) → byte[]?` returns the stored package.

### 24.3 `ImpactModelCache`

The cache is a singleton that keeps at most two models, keyed by `(generation, snapshotId)`. An entry is a `Lazy<Task<ImpactModel>>`. Inserting a third entry evicts the oldest.

`GetAsync(string? expectedSnapshot, CancellationToken)`:
1. Read the active `(generation, snapshotId)`.
2. If `expectedSnapshot` is set and differs, throw `WorkspaceException("snapshotChanged", "The workspace snapshot changed. Start again from a new search.")`.
3. Load and build the package. If there is no row, throw `WorkspaceException("impactUnavailable", "This snapshot has no impact model. Publish a new bundle.")`.
4. Return `(snapshotId, model)`.

### 24.4 Routes (`ImpactRoutes.MapImpactRoutes`)

All routes sit on `app.MapGroup("/api/impact").RequireAuthorization(Identity.ReadPolicy)`.

| Route | Handler | Errors |
| --- | --- | --- |
| `POST /api/impact/search` | Run `Candidates.Find` with `Query ?? ""` and `ExactIri`. Page the leads by keyset, 100 per page (24.5). | `invalidImpactRequest`, `invalidCursor`, `snapshotChanged`, `impactUnavailable`, `snapshotUnavailable` |
| `POST /api/impact/paths` | `Paths.Read(model, starts, maxDepth ?? 4)`, then fill in `Lines`. | `invalidImpactRequest`, `snapshotChanged`, `impactUnavailable`, `snapshotUnavailable` |
| `POST /api/impact/views` | `Views.Project`, then `ViewMarkdown.Render`. | the same as `paths` |

- `ImpactException` becomes `WorkspaceException("invalidImpactRequest", message)`.
- `ProblemResponses.StatusFor` gains: `snapshotChanged` (409), `impactUnavailable` (503), `assessmentNotFound` (404, used in I5). Everything else keeps the default of 422.
- `WorkspaceLinks` gains `ImpactSearch`, `ImpactSearchNext` (when `nextCursor` is set), `ImpactPaths` and `ImpactViews`, following `Traverse` and `TraverseNext`.
- `WorkspaceOpenApi` gives each operation an id (`searchImpact`, `traceImpact`, `viewC4`), a summary, an agent description, and an example from `OpenApiExamples`. The examples use invented `example-` sources and `https://example.test/` IRIs only.

### 24.5 Search cursor

The cursor is base64url of `EcmaJson.Stringify({v:1, s:snapshotId, q:sha256(query+"\u0000"+(exactIri??"")), k:[rank, sourceId, nodeIri, kind]})`. `rank` is 0 for `exact-iri`, 1 for `phrase` and 2 for `token`.
- A page holds the leads strictly after `k` in `(rank, sourceId, nodeIri, kind)` ordinal order, up to 100 of them.
- `nextCursor` is set only when more leads remain.
- A malformed cursor, or one whose `q` differs, is refused with `invalidCursor`.
- A cursor whose `s` differs from the active snapshot is refused with `snapshotChanged`.
- Every page carries the full `gaps` and `coverage`.

### 24.6 `ImpactApiTests.cs` (complete)

```csharp
using System.Net;
using System.Net.Http.Json;
using RuntimeKnowledge.Contracts;
using Xunit;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class ImpactApiTests
{
    private const string P = "https://example.test/policies/graph#";

    // ApiFixture owns and disposes its scenario. The new overload runs `arrange` in place of activating "initial".
    private static async Task<(ApiFixture Api, string Snapshot)> OpenAsync(string corpus = "confluence")
    {
        string? snapshot = null;
        var api = await ApiFixture.OpenAsync("Keycloak", async scenario =>
        {
            snapshot = scenario.Bundles.AddCorpusBundle(corpus);
            await scenario.Store.StageAsync(snapshot, CancellationToken.None);
            await scenario.Store.ActivateAsync(snapshot, CancellationToken.None);
        });
        return (api, snapshot!);
    }

    [Fact]
    public async Task A_reader_searches_and_gets_supported_leads_with_the_snapshot()       // B15
    {
        var (api, snapshot) = await OpenAsync();
        await using var _ = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var result = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("expense", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        Assert.Equal(snapshot, result!.SnapshotId);
        Assert.Contains(result.Leads, l => l.Key.NodeIri == P + "expense-policy" && l.Supported);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task Search_pages_by_cursor_at_most_100_and_ends_without_one()            // B15
    {
        var (api, _) = await OpenAsync("paging");      // corpus scenario 'paging': 250 nodes labelled "Claim <n>"
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var seen = new List<ImpactLead>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("claim", null, Cursor: cursor)))
                .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
            Assert.True(page!.Leads.Count <= 100);
            seen.AddRange(page.Leads);
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null);
        Assert.Equal(3, pages);
        Assert.Equal(250, seen.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public async Task A_cursor_for_another_query_is_invalid()
    {
        var (api, _) = await OpenAsync("paging");
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var first = await (await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("claim", null)))
            .Content.ReadFromJsonAsync<ImpactSearchResult>(WorkspaceJson.Options);
        var reused = await reader.PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("other", null, Cursor: first!.NextCursor));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Contains("invalidCursor", await reused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_reader_traces_paths_step_by_step_with_cited_lines()                // B16, B28
    {
        var (api, snapshot) = await OpenAsync();
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var result = await (await reader.PostAsJsonAsync("api/impact/paths",
            new ImpactPathsRequest(snapshot, [new ImpactKey("fixture/policies", P + "expense-policy")])))
            .Content.ReadFromJsonAsync<ImpactPathsResult>(WorkspaceJson.Options);
        var twoStep = result!.Paths.First(p => p.Steps.Count == 2);
        Assert.All(twoStep.Steps, step => Assert.NotNull(step.Evidence));
        Assert.Equal("Expense policy", twoStep.Steps[0].Evidence![0].Page!.Title);
        Assert.Equal(["Managers approve claims in the finance system."], twoStep.Steps[1].Evidence![0].Lines);
        Assert.DoesNotContain(result.Paths, p => p.Steps.Count == 1 && p.Steps[0].Object?.NodeIri == P + "ledger"
            && p.Steps[0].Subject?.NodeIri == P + "expense-policy");                    // never shown as a direct relationship
    }

    [Fact]
    public async Task Requests_for_another_snapshot_are_refused_as_snapshot_changed()      // B36 groundwork
    {
        var (api, _) = await OpenAsync();
        await using var a = api;
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var response = await reader.PostAsJsonAsync("api/impact/paths",
            new ImpactPathsRequest(new string('0', 64), [new ImpactKey("fixture/policies", P + "expense-policy")]));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("snapshotChanged", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_snapshot_without_a_model_is_impact_unavailable()
    {
        await using var api = await ApiFixture.OpenAsync("Keycloak");                  // activates "initial", which has no impactInput
        var response = await api.ClientWithToken(["ReadKnowledgeGraph"])
            .PostAsJsonAsync("api/impact/search", new ImpactSearchRequest("order", null));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("impactUnavailable", await response.Content.ReadAsStringAsync());
    }

    [Theory]                                                                           // Q2
    [InlineData("api/impact/search")]
    [InlineData("api/impact/paths")]
    [InlineData("api/impact/views")]
    public async Task Every_impact_route_refuses_unauthenticated_calls(string route)
    {
        var (api, _) = await OpenAsync();
        await using var a = api;
        var response = await api.CreateClient().PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

**`paging` corpus scenario.** Add a small fixture under `test/fixtures/impact/paging-repos.mjs`. It has one source with 250 nodes labelled `Claim 000` to `Claim 249`, each with one evidenced assertion to the next, and no mappings. Its scenario has one step, `search --query claim`. Capture and export it as in I1 Task 1.

Steps:
- [ ] Write the tests and the `paging` scenario, and confirm that they fail (404 on the routes).
- [ ] Implement 24.1–24.5, and regenerate the spec.
- [ ] Run the OpenApi category and the database category.
- [ ] Commit `feat: serve impact search, paths and views (#198)` and push.

---

## Task 25: Evidence text in the workspace, and the publication guard (B28, B29, Q7)

**Files:** create `test/impact-publication.test.mjs` and `runtime/RuntimeKnowledge.Tests/ImpactPublicationTests.cs`.

`test/impact-publication.test.mjs`:
1. With `pinGitEnvironment()` and `withConfluenceFixture`, run the catalog build, `runCli(['build', ...])`, into a temporary `dist/` against the fixture checkouts. Use the same flags as `test/representative-fixture.test.mjs`. The fixture division needs a `division.json` and a `core/ontology.ttl`; add minimal ones, copied from `test/fixtures/representative/setup.mjs`.
2. Read every file in that `dist/` recursively, and assert that none contains any of these strings: `Claims over 500 need a manager's approval.`, `Managers approve claims in the finance system.`, `The old ledger still exports claims nightly.` or `The finance team runs the claims job.` (B29).

`ImpactPublicationTests.cs` (category `Database`):
- **Unauthenticated responses (Q7).** Unauthenticated `GET /api/openapi/workspace-api.json`, `POST /api/impact/search` and `POST /api/impact/paths` bodies contain none of the four strings.
- **Spec examples.** The committed `runtime/openapi/workspace-api.json` contains none of them.
- **Signed-in readers (B28).** For a signed-in reader, paths from `expense-policy` include the lines of the ordinary document `legacy-notes.md` in `Lines`, which proves that excerpts reach signed-in readers only.

Steps:
- [ ] Write both tests. The Node guard passes at once, because `dist/` never had page text: it pins the behaviour. The C# test fails until Task 24's `Lines` mapping exists. If it passes because Task 24 already did this, keep it as the pin.
- [ ] Commit `test: guard cited text from public outputs (B29, Q7)` and push.

---

## Task 26: Search and paths pages (B15, B16, Q6)

**Files:**
- create `runtime/RuntimeKnowledge.Web/Pages/Impact.razor` and `Components/ImpactLeadList.razor`, `ImpactPathList.razor`, `EvidenceCitation.razor` and `GapList.razor`;
- modify `Services/WorkspaceClient.cs` (`ImpactSearch`, `ImpactPaths` and `ImpactView`) and `Components/WorkspaceLayout.razor` (a nav link, `<a href="impact">Impact</a>`);
- create `runtime/RuntimeKnowledge.Tests/ImpactUiTests.cs` (bUnit, following `WorkspaceUiTests.cs` for the fake handler and services).

### 26.1 Page behaviour

- **The route** is `@page "/impact"` with `[Authorize]`, as the other pages have.
- **Search.** A labelled search form (`<label for="impact-query">Change request description</label>`) calls `ImpactSearch` and lists the leads. Each lead is a native checkbox labelled with `{label} · {sourceId}`. Next to it are its support status as text (`Supported` or `Not supported`), its reason and its match kind. **Load more** appears only when `NextCursor` is set, and sends the same query with the cursor. The page keeps the `SnapshotId` of the first page and sends it with every later request.
- **Trace.** **Trace impact** is enabled when at least one lead is checked. It calls `ImpactPaths` with the chosen keys and shows `ImpactPathList`.
- **`ImpactPathList`** renders each path as an ordered list (`<ol>`) of steps. Each step's text is:

  | Step type | Text |
  | --- | --- |
  | `assertion` | `{subject} —{relationship} ({direction})→ {object}` |
  | `binding` | `Reviewed C4 binding: {from} → {to}` |
  | `reviewedLinkCrossing` | `Reviewed link crossing through {sharedTerm}: {from} → {to}` |

  Each step is followed by its evidence (`EvidenceCitation`). A path is never collapsed to its endpoints (B16).
- **`EvidenceCitation`** shows `{path}:{start}-{end} · {role} · {commit[..12]}`.
  - For a page, it also shows the title. When `ConfluenceUrl` is set, the title is a link (`<a href="{url}" rel="noopener noreferrer" target="_blank">`).
  - The cited lines go in `<pre class="evidence-lines">`, rendered as text, never as markup (Q4).
  - When `Lines` is null, it shows `Cited text unavailable`.
- **`GapList`** renders each gap as `<li><code>{code}</code> {reason}</li>`, under a heading `Limits of this result`.
- **A `snapshotChanged` problem** shows the message from the problem body and clears the page state. I5 builds on this for drafts.

### 26.2 `ImpactUiTests.cs`

Write these bUnit tests with the fake HTTP handler from `WorkspaceUiTests`, returning canned `ImpactSearchResult` and `ImpactPathsResult` JSON:
1. `Search_lists_leads_with_support_text_and_loads_more_with_the_cursor`. The second request body carries `cursor` and the same `query`.
2. `Trace_sends_the_chosen_keys_and_the_snapshot_of_the_first_search`.
3. `Paths_render_every_step_in_order_with_evidence_lines_as_text`. A step whose line contains `<b>x</b>` renders the literal text, and no `<b>` element exists.
4. `A_page_title_links_to_its_confluence_address_and_a_missing_text_says_unavailable`.
5. `Snapshot_changed_clears_results_and_shows_the_message`.
6. `Controls_are_native_and_labelled`: the search input has an associated label, the lead checkboxes are `input[type=checkbox]` with labels, and the buttons are `<button>` elements.

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement 26.1.
- [ ] Commit `feat: add workspace impact search and paths pages (#198)` and push.

---

## Task 27: Performance and accessibility checks (Q1, Q6)

**Files:**
- create `runtime/RuntimeKnowledge.Tests/ImpactPerformanceTests.cs` (category `Performance`) and `runtime/RuntimeKnowledge.Tests/Impact/LargeModel.cs`;
- create `deploy/workspace/verify-impact-browser.mjs`, and modify `deploy/workspace/verify-browser.mjs` to call it;
- modify `package.json`, adding `@axe-core/playwright` 4.10.2 to `devDependencies`, and `package-lock.json` through `npm install --save-exact`.

### 27.1 Performance (Q1)

`LargeModel.Bundle(int nodes = 10_000, int assertions = 50_000)` builds an in-memory `impactInput` in C#:
- one source, `fixture/large`;
- nodes `n00000` to `n09999`, labelled `Claim {i} {word}`, where `word` cycles through 50 fixed words;
- each assertion `a{j}` links node `j % nodes` to node `(j * 7919 + 1) % nodes`, with relationship `rel#r{j % 20}`;
- one shared evidence record over a 1-line file.

It returns manifest bytes and blobs that a `MemoryBundles.Add(manifestId, manifest, blobs)` helper can stage.

`ImpactPerformanceTests.Search_and_paths_meet_Q1_at_the_95th_percentile`:
1. Stage and activate the bundle, and open an `ApiFixture`.
2. Make one warm-up call, then record the cold-load time of the first `ImpactModelCache` load, by timing a call made after clearing the cache with an internal `Clear()`.
3. Time 100 `POST /api/impact/search` calls, for `"claim 4"`, at the API through the test server.
4. Time 100 `POST /api/impact/paths` calls from `n00042`, at depth 4.
5. Write `{search_p95_ms, paths_p95_ms, cold_load_ms}` to the test output.
6. Assert `search_p95 <= 2000` and `paths_p95 <= 5000`.

The test runs on demand on the reference container. The test server stands in for "at the API" in Q1.

```bash
bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter Category=Performance --logger "console;verbosity=detailed"
```

If either assertion fails, **do not** add a cap on paths. Record the numbers and raise pushback to the analyst (Q1 against the path enumeration), as `03-architecture.md` requires. Stop the increment there.

### 27.2 Accessibility (Q6)

`verify-impact-browser.mjs` exports `verifyImpactPages({ reader, origin })`. Use the same `login` contexts as `verify-browser.mjs`.

```js
import { AxeBuilder } from '@axe-core/playwright';
// …
const page = await reader.newPage();
await page.goto(origin + '/impact');
await page.getByLabel('Change request description').fill('order');
await page.keyboard.press('Enter');
await page.getByRole('checkbox').first().check();
await page.getByRole('button', { name: 'Trace impact' }).focus();
await page.keyboard.press('Enter');
await page.getByRole('heading', { name: 'Limits of this result' }).waitFor();
const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
const serious = results.violations.filter(v => ['serious', 'critical'].includes(v.impact));
assert.deepEqual(serious.map(v => v.id), [], 'Q6 impact pages');
```

The flow above uses only the keyboard after the first fill. Assert that each step works with `Tab` and `Enter`/`Space`: focus the checkbox with `Tab` and press `Space`, rather than calling `check()`. Then call it from `verify-browser.mjs` after `verifyGraphExplorer`.

Steps:
- [ ] Write both checks. Run `npm install --save-exact -D @axe-core/playwright@4.10.2`.
- [ ] Run the performance test, and the browser check against the Mac compose environment (`deploy/workspace/local/demo.sh`, then `deploy/workspace/verify.sh`).
- [ ] Commit `test: measure impact search and paths, and check accessibility (Q1, Q6)` and push.

## I4 close

- [ ] CI is green. The database suite passes. The performance and browser numbers are recorded.
- [ ] Post the handoff. It says I4 is done, lists B15, B16, B28 and B29, and gives the Q1 measurements and the cold-load time. I5 is next. **Stop.**
