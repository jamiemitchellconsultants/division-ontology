# Runtime knowledge workspace — detailed implementation plan

**Inputs:** [Vision](../../features/runtime-knowledge-workspace/01-vision.md), [behaviours B1–B25](../../features/runtime-knowledge-workspace/02-behaviours.md), [approved architecture and ADRs](../../features/runtime-knowledge-workspace/03-architecture.md), and the [task plan](../../superpowers/plans/runtime-knowledge-workspace/plan.md).

**Build rule:** Start on `feature/runtime-knowledge-workspace/dev-build` after the plan PR merges. Each task supplies a focused test and expected result. Write the test first, run it red, implement the task, and run it green. Run `npm test` and the available runtime suite, inspect the staged diff and commit and push the stated message. Stage explicit paths. Use the repo's catalogue validation if its inputs or builder change. Generated `dist/` stays outside this PR.

## Shared contracts

Use the solution at `runtime/RuntimeKnowledge.slnx`. Projects are `RuntimeKnowledge.Contracts` (JSON records and enums), `RuntimeKnowledge.Store` (Npgsql, SQL and AGE), `RuntimeKnowledge.Providers` (S3/MinIO and embedding adapters), `RuntimeKnowledge.Api`, `RuntimeKnowledge.Worker`, `RuntimeKnowledge.Web` and `RuntimeKnowledge.Tests`. Store, Providers and Web depend on Contracts. API and Worker depend on Store, Providers and Contracts. Tests reference the projects they exercise. Pin .NET 10 SDK and package versions at initial build and check in dependency locks. Pin a PostgreSQL 16 base image and compatible released AGE and pgvector versions for arm64 and amd64. Check source archive digests. See [AGE releases](https://age.apache.org/download/) and [pgvector installation](https://github.com/pgvector/pgvector#installation).

Use camelCase JSON and UTF-8. Represent stable identity separately from revision. Preserve IRIs as provided by the source. Never lower-case an IRI or derive identity from a label. `PublishedKey.SourceId` is the catalogue source ID, or `division-core` for the core. Resource kinds are `concept`, `node`, `relationshipType`, `assertion`, `reviewedLink`, `denotation` and `c4Binding`. The key is the tuple `(sourceId, kind, identifier)`. The identifier is the IRI where supplied, or a canonical JSON array of the corresponding identity fields for a reviewed link, denotation or binding. Keep `SourceCommit` and `SnapshotId` in provenance, outside the stable key. An object digest or snapshot ID is a lower-case SHA-256 hex string. A source commit is a 40-character Git commit ID. A runtime ID is a server-assigned UUID.

```csharp
public sealed record PublishedKey(string SourceId, string Kind, string Identifier);
public sealed record Actor(string Issuer, string Subject);
public sealed record Evidence(string Path, int StartLine, int EndLine, string BlobId, string Role);
public sealed record Provenance(PublishedKey Key, string SourceCommit, string SnapshotId,
    string? Basis, IReadOnlyList<Evidence> Evidence);
public sealed record EmbeddingProfile(string Provider, string Model, string Revision,
    int Dimensions, string TextProfile);
public sealed record ContributionResult(Guid Id, int Version, long Position);
public sealed record PageRequest(string? Cursor, int Limit = 50);
public sealed record SnapshotCandidate(string SnapshotId, string State);
public sealed record TraversalRequest(PublishedKey Start, string Direction = "outgoing",
    int MaxDepth = 2, bool IncludeUnavailable = false);
```

Read responses use these envelope fields. `InventoryPage` has `snapshotId, entities, relationshipTypes, gaps, nextCursor`; entity rows have `key, label, description, provenance`. `TraversalResult` has `snapshotId, paths, gaps, truncated`; each path has ordered steps, and each step carries its published key or runtime ID, endpoints, direction, origin, provenance, availability and last-known snapshot when unavailable. Reached entities carry active annotations. `HistoryPage` has `entries, nextCursor`; entries expose `position, type, actor, occurredAt, contributionId, revision, before, after, snapshotId`. `SearchResult` has `snapshotId, hits, indexPending`; hits expose entity key, optional annotation ID, matched text, score and provenance. In tests, `Scenario.TraverseAsync` flattens path steps and `Scenario.HistoryAsync` extracts the first page's entries. Fixture variants share keys and change revisions as specified below.

The journal accepts `snapshotActivated`, `annotationCreated`, `annotationEdited`, `annotationRemoved`, `relationshipCreated`, `relationshipEdited`, `relationshipRemoved` and `codificationAssociated` records. A row has a versioned payload, UUID event ID, consecutive committed position, actor and occurrence time, selected snapshot, and aggregate ID and revision where applicable. Contribution events preserve before and after data, including stable references and the snapshot evidence used at the time. A snapshot activation carries the manifest digest. Keep separate durable journal and derived projection schemas. Application roles can insert journal entries and read required state. Grant migration permissions to a separate role. Keep DDL out of user transactions.

Use the same named PostgreSQL volume for normal redeployments. Rebuilds derive new projections from the available journal and bundle store. Backup policy, off-host copies, point-in-time restore and disaster recovery are tracked in [#53](https://github.com/jamiemitchellconsultants/division-ontology/issues/53).

## Fixtures and test conventions

Use `test/acceptance/c4-fixture.mjs` and `test/fixtures/representative/setup.mjs` for real temporary Git commits in publisher tests. The production baseline remains the current division catalogue and JointBooking ontology at its recorded commit. InternalOntology trials and synthetic sources supply evidence-backed graph examples in engineering tests. Never publish test sources into production `sources.json`.

For the C# tests, use xUnit and Npgsql against a disposable database from the actual workspace database image. `DatabaseFixture.OpenAsync()` creates a unique database, applies migrations to the tested task, returns `IAsyncDisposable` with a connection string, and drops only that database on dispose. Provide it with `WORKSPACE_TEST_POSTGRES` for a disposable Docker container, never a production connection. Run a focused class with `dotnet test runtime/RuntimeKnowledge.slnx --filter FullyQualifiedName~ClassName`. Use a fixed clock, fixed IDs, isolated mutable fixtures and explicit concurrency barriers rather than sleeps. Exceptions named `WorkspaceException` carry a stable `Code` used by HTTP problem details. Use no real identity or cloud credentials in tests.

Define the following shared test fixture as Tasks 3–5 introduce the interfaces. `Scenario.OpenAsync()` wraps the database fixture, exposes `Store` (the real store), `Bundles` (an in-memory implementation of the immutable object contract), and `Actor` (issuer `https://issuer.test`, subject `contributor`). Keys `A` and `B` are in distinct sources and both labelled “Order”; `Isolated` is a concept with no edges. The published fixture contains `PublishedEdge` from A to B, with its assertion IRI and source evidence. `ActivateAsync(variant)` stages and activates a bundle with deterministic seed vectors for the test profile. Variants `initial`, `changed`, `missing`, `returned` retain A, give B its next source revision, omit B and its incident published records, then restore the same B identity. Each change has a distinct manifest. A fixture hook to throw before commit or hold a transaction at a barrier is injected through test wiring only. Each test queries real SQL and AGE projections.

## Task 1 — database and transaction foundation

**Files:** Create `runtime/global.json`, `runtime/RuntimeKnowledge.slnx`, `runtime/Directory.Packages.props`, `runtime/RuntimeKnowledge.Store/Db.cs`, `runtime/RuntimeKnowledge.Store/Sql/001_foundation.sql`, `runtime/RuntimeKnowledge.Tests/DatabaseFixture.cs`, `runtime/RuntimeKnowledge.Tests/DatabaseTests.cs`, `deploy/workspace/database/Dockerfile` and `deploy/workspace/database/test-db.sh`.

**Interfaces:** `Db.OpenAsync(CancellationToken) -> Task<NpgsqlConnection>`, `Db.MigrateAsync(CancellationToken) -> Task`, and `DatabaseFixture.OpenAsync() -> Task<DatabaseFixture>`. Provide migration command `deploy/workspace/database/test-db.sh start` to build and start the disposable image and print a test connection URL. Test fixture creates the database and applies the SQL migrations in numeric order. On each session that uses AGE, load AGE and set a fixed search path. Use fixed Cypher templates and AGE's prepared parameter support. See [AGE query format](https://age.apache.org/age-manual/master/intro/cypher.html).

**Test file** `DatabaseTests.cs`

```csharp
using Npgsql;
using Xunit;

public sealed class DatabaseTests
{
    [Fact]
    public async Task JournalSqlAndAgeShareRollback()
    {
        await using var fixture = await DatabaseFixture.OpenAsync();
        await using var db = new NpgsqlConnection(fixture.ConnectionString);
        await db.OpenAsync();
        await using var setup = new NpgsqlCommand(
            "LOAD 'age'; SET search_path = ag_catalog, public; CREATE TEMP TABLE proof (id integer);", db);
        await setup.ExecuteNonQueryAsync();
        await using var tx = await db.BeginTransactionAsync();
        await using var write = new NpgsqlCommand(
            "INSERT INTO proof VALUES (1); SELECT * FROM cypher('workspace_g1', $$ CREATE (:Entity {key: 'proof'}) $$) AS (v agtype);", db, tx);
        await write.ExecuteNonQueryAsync();
        await tx.RollbackAsync();
        await using var sqlCount = new NpgsqlCommand("SELECT count(*) FROM proof", db);
        Assert.Equal(0L, (long)(await sqlCount.ExecuteScalarAsync())!);
        await using var graphCount = new NpgsqlCommand(
            "SELECT * FROM cypher('workspace_g1', $$ MATCH (n:Entity) RETURN count(n) $$) AS (n agtype)", db);
        Assert.Equal("0", (await graphCount.ExecuteScalarAsync())!.ToString());
        await using var vector = new NpgsqlCommand("SELECT '[1,0]'::vector <=> '[1,0]'::vector", db);
        Assert.Equal(0d, (double)(await vector.ExecuteScalarAsync())!);
    }
}
```

- [ ] Write the test and run `dotnet test runtime/RuntimeKnowledge.slnx --filter FullyQualifiedName~DatabaseTests`. Expect failure until the image and migration enable both extensions and create the initial AGE graph.
- [ ] Build a multi-stage image that compiles the two extensions against the chosen PostgreSQL major. Use the matching runtime base. The migrations create a durable journal schema, empty journal/head control and the initial graph. Separate migration and application grants. Allocate journal positions under a row lock and a transactional counter, using a unique constraint on position and event ID.
- [ ] Run the focused test on Docker, then the build rule checks. Commit `feat(workspace): add transactional AGE and pgvector database foundation` and push.

## Task 2 — deterministic pinned bundles

**Files:** Create `src/workspace/bundle.mjs`, `src/workspace/bundle-cli.mjs` and `test/workspace-bundle.test.mjs`. Reuse `src/c4/git.mjs`, `snapshot.mjs`, `graph.mjs` and `registry.mjs` without changing their public contracts.

**Interfaces:** `buildBundle({divisionRoot,catalogCommit,registryCommit,checkouts}) -> {id,manifest,manifestBytes,blobs}`. `blobs` maps SHA-256 hex to exact `Buffer` values. `writeBundle(bundle,outputDir)` creates `manifests/<id>.json` and `blobs/<sha256>`. The CLI accepts `--catalog-commit`, optional `--registry-commit`, repeatable `--checkout owner/repo=/absolute/path` and required `--output`. It returns the manifest digest after success and fails before publishing output for an invalid input.

Read the exact bytes of each validated Git blob with an argument-array Git invocation. Verify the checkout and repository-relative path with the C4 helpers before reading. The manifest is `{schemaVersion:1,divisionCommit,catalogSha256,sources:[{id,commit,graphSchemaVersion,graphStatus}],registryCommit,files:[{role,sourceId,path,sha256,bytes}]}`. Sort object keys and order file rows by role, source and path with code-point comparison. Serialize UTF-8 and hash the exact bytes. Include pinned `dist/catalog.json`, `dist/combined.nq`, `sources.json`, configured graph Turtle, graph configuration, graph evidence blobs, included registry inputs and a normalized `projection.json` derived from validated records. Its shape is `{entities,relationshipTypes,edges,gaps}`. Entity and edge rows include published keys and provenance, edge rows include typed endpoints. Stamp snapshot ID from the manifest digest on import, rather than embedding a digest of a file in that same file. Keep raw source objects for provenance. Export only valid bindings and record unresolved mappings and optional missing graphs as gaps. Never infer edges from prose, type labels or matching names.

**Test file** `test/workspace-bundle.test.mjs`

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { setup, teardown } from './fixtures/representative/setup.mjs';
import { buildBundle } from '../src/workspace/bundle.mjs';

test('pins the catalog, verifies every object, and emits identical bundle bytes', async () => {
  const temp = await mkdtemp(join(tmpdir(), 'workspace-bundle-'));
  const root = join(temp, 'fixture');
  try {
    const f = await setup(root);
    const args = { divisionRoot: f.divisionRoot, catalogCommit: f.commit,
      registryCommit: f.commit, checkouts: f.checkouts };
    const bundle = buildBundle(args);
    assert.equal(bundle.id, createHash('sha256').update(bundle.manifestBytes).digest('hex'));
    assert.equal(bundle.manifest.divisionCommit, f.commit);
    for (const file of bundle.manifest.files) {
      const bytes = bundle.blobs.get(file.sha256);
      assert.equal(bytes.length, file.bytes);
      assert.equal(createHash('sha256').update(bytes).digest('hex'), file.sha256);
    }
    assert.deepEqual(buildBundle(args).manifestBytes, bundle.manifestBytes);
    assert.throws(() => buildBundle({ ...args, checkouts: new Map() }), /checkout/i);
  } finally {
    try { teardown(root); } catch (error) { if (error.code !== "ENOENT") throw error; }
    await rm(temp, { force: true, recursive: true });
  }
});
```

- [ ] Run `node --test test/workspace-bundle.test.mjs` and verify red. Confirm the fixture's exported fields before adapting setup. Add cases using committed wrong-origin and stale-evidence variants, optional graph absence, and one invalid and one valid C4 binding.
- [ ] Implement the builder and atomic local output. Check pinned source identity, graph schema and evidence with the existing loaders. Include file digest and length verification for every output object.
- [ ] Run the focused and repository tests. Commit `feat(workspace): package pinned catalog and source graph bundles` and push.

## Task 3 — snapshot staging, activation and graph reads

**Files:** `runtime/RuntimeKnowledge.Contracts/Published.cs`, `runtime/RuntimeKnowledge.Providers/BundleObjects.cs`, `runtime/RuntimeKnowledge.Store/Sql/002_snapshots.sql`, `Snapshots.cs`, `GraphQueries.cs`, `runtime/RuntimeKnowledge.Worker/BundleImport.cs`, `runtime/RuntimeKnowledge.Tests/SnapshotTests.cs`.

**Interfaces:** `IBundleObjects.OpenAsync(string objectKey, CancellationToken) -> Task<Stream>` and `PutImmutableAsync(string objectKey, Stream, CancellationToken) -> Task`. Use AWS SDK S3 with the standard regional endpoint on AWS and a configured service URL and path-style addressing for MinIO. A repeated identical upload succeeds and a mismatching object is an error. Runtime imports receive a manifest ID from a trusted publisher, fetch only derived hash paths, and check length and SHA-256 before accepting content. Reject unknown required schemas, duplicate keys and unresolved edge endpoints. The publisher/worker owns these operations outside the public role API.

`SnapshotStore.StageAsync(string manifestId, CancellationToken) -> Task<SnapshotCandidate>`, `ActivateAsync(string manifestId, CancellationToken) -> Task<long>`, `GraphQueries.InventoryAsync(PageRequest,CancellationToken) -> Task<InventoryPage>` and `TraverseAsync(TraversalRequest,CancellationToken) -> Task<TraversalResult>`. Stage records and baseline indexing requirements under the candidate ID. Once Task 7 lands, activate only when baseline vectors for the selected profile are ready. The snapshot integration fixture marks that prerequisite with deterministic vectors. Activation serializes on the journal head, appends the operational event and updates the active pointer in the same transaction. If it is already active, return the existing activation position. Keep the former snapshot available until the validated replacement is activated.

Store identity vertices independent of snapshot membership. Use versioned published edges and stable runtime edge identities in AGE, with SQL rows carrying full evidence and exact membership. Use repeatable-read queries to pin the projection generation and snapshot. Expand bounded directed paths only through eligible edges. Default depth 2, max depth 8, max 100 paths and explicit truncation when capped. Use source keys in graph data and fixed Cypher templates. A no-path result is distinct from missing graph coverage. Inventory is paginated, includes isolated nodes and relationship types and returns snapshot and gaps even when empty.

**Test file** `SnapshotTests.cs`

```csharp
using Xunit;

public sealed class SnapshotTests
{
    [Fact]
    public async Task InvalidReplacementKeepsOneCoherentBaseline()
    {
        await using var s = await Scenario.OpenAsync();
        var initial = await s.ActivateAsync("initial");
        await Assert.ThrowsAsync<WorkspaceException>(() => s.StageCorruptBundleAsync());
        var page = await s.InventoryAsync();
        Assert.Equal(initial, page.SnapshotId);
        Assert.Contains(page.Entities, entity => entity.Key == s.Isolated);
        Assert.Equal(2, page.Entities.Count(entity => entity.Label == "Order"));
        Assert.All(page.Entities, entity => Assert.Equal(initial, entity.Provenance.SnapshotId));
        Assert.Contains(await s.TraverseAsync(s.A), edge => edge.Key == s.PublishedEdge);
    }
}
```

- [ ] Run the class filter red. Add a staged-but-inactive candidate check and activation idempotency. Check inventory cursor order and snapshot mismatch, a corrupt blob and a missing optional graph.
- [ ] Implement the store and import worker. Keep the active pointer unchanged on staging errors. Bind pagination cursors to the snapshot, query shape and last stable sort key. If the snapshot advances between pages, return a stale-cursor error.
- [ ] Run focused and prior checks. Commit `feat(workspace): activate verified snapshots and query graph inventory` and push.

## Task 4 — journalled contributions and history

**Files:** `runtime/RuntimeKnowledge.Contracts/Contributions.cs`, `runtime/RuntimeKnowledge.Store/Contributions.cs`, `Sql/003_contributions.sql`, `GraphQueries.cs`, `runtime/RuntimeKnowledge.Tests/ContributionTests.cs`.

**Interfaces:** `ContributionStore.ApplyAsync(Actor, ContributionCommand, CancellationToken) -> Task<ContributionResult>` and `HistoryAsync(Guid? contributionId, PageRequest,CancellationToken) -> Task<HistoryPage>`. Commands are `CreateAnnotation(target,category,text)`, `CreateRelationship(from,to,meaning)`, `EditAnnotation(id,expectedVersion,category,text)`, `EditRelationship(id,expectedVersion,from,to,meaning)`, `RemoveContribution(id,expectedVersion)` and `AssociateCodification(id,expectedVersion,publishedRelationship)`. Category is `businessExplanation`, `wayOfWorking` or `businessRbac`. Meaning is exactly one published type reference or a nonblank custom label and explanation. Enforce machine-readable constraints when supplied. Preserve the directed from and to endpoints. The actor is supplied by trusted application context.

Acquire the same journal head lock as activation before reading the current snapshot or allocating a position. Validate new references against that snapshot and versions against the current record. A retained reference may remain unavailable during edits that keep it. A new target, replacement endpoint or new association target must resolve under the accepted rules. Return `endpointUnavailable`, `invalidMeaning` or `versionConflict` as appropriate. On success append a versioned event, update SQL and the AGE projection, and create any durable index job inside one transaction. Roll back all effects together. Current views hide removals. Traversal excludes unavailable endpoints by default and includes clearly marked last-known evidence when requested. Associations preserve separate relationship IDs and both provenances. History reads contribution events and excludes operational activation records.

**Test file** `ContributionTests.cs`

```csharp
using Xunit;

public sealed class ContributionTests
{
    [Fact]
    public async Task SuccessfulEdgeAndHistoryAreImmediatelyVisible()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var edge = await s.CreateRelationshipAsync(s.A, s.B, "reconciles", "Connects order to accounting.");
        Assert.Contains(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
        var entry = Assert.Single(await s.HistoryAsync(edge.Id));
        Assert.Equal(s.Actor, entry.Actor);
        Assert.Equal(edge.Position, entry.Position);
        Assert.Equal("relationshipCreated", entry.Type);
    }

    [Fact]
    public async Task ARejectedSaveLeavesNeitherHistoryNorGap()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var first = await s.CreateRelationshipAsync(s.A, s.B, "uses", "Approved link.");
        await Assert.ThrowsAsync<InjectedFailureException>(() =>
            s.FailNextSaveBeforeCommitAsync(s.A, s.B));
        var second = await s.CreateRelationshipAsync(s.A, s.B, "reads", "A second link.");
        Assert.Equal(first.Position + 1, second.Position);
        Assert.Equal(2, (await s.TraverseAsync(s.A)).Count(edge => edge.RuntimeId.HasValue));
        Assert.Equal(2, (await s.HistoryAsync()).Count);
    }

    [Fact]
    public async Task PublicationWinsBeforeAnEndpointWriteAndRetainedRecordsRemainEditable()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var edge = await s.CreateRelationshipAsync(s.A, s.B, "uses", "Before replacement.");
        await s.ActivateBeforeWaitingSaveAsync("missing", s.A, s.B);
        Assert.Equal("endpointUnavailable", s.LastRejectedWrite!.Code);
        Assert.DoesNotContain(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
        Assert.Contains(await s.TraverseAsync(s.A, includeUnavailable: true),
            item => item.RuntimeId == edge.Id && item.EndpointUnavailable);
        await s.EditMeaningAsync(edge.Id, edge.Version, "uses", "Clarified after replacement.");
        var stale = await Assert.ThrowsAsync<WorkspaceException>(() => s.RemoveAsync(edge.Id, edge.Version));
        Assert.Equal("versionConflict", stale.Code);
        await s.ActivateAsync("returned");
        Assert.Contains(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
    }
}
```

- [ ] Run red. Use a barrier after the publisher acquires the transaction lock so the waiting-save test is deterministic. Add tests for nonblank annotations and categories, descriptive RBAC during traversal, read-after-edit, removal history, missing meaning, both kinds of meaning, two distinct associated edges and loss of the published record.
- [ ] Implement current views, journal and AGE writes. A stale version returns the current record or removal state with its revision. An invalid request allocates no committed position.
- [ ] Run focused and prior tests. Commit `feat(workspace): journal contributions and graph changes atomically` and push.

## Task 5 — ordered replay and projection cutover

**Files:** `runtime/RuntimeKnowledge.Store/Replay.cs`, `Sql/004_replay.sql`, `runtime/RuntimeKnowledge.Worker/ReplayCommand.cs`, `runtime/RuntimeKnowledge.Tests/ReplayTests.cs`.

**Interfaces:** `ReplayStore.RebuildAsync(long throughPosition, CancellationToken) -> Task<RebuildReport>` and `CutoverAsync(Guid generation, long expectedJournalHead, CancellationToken) -> Task`. Worker CLI `replay --through POSITION` returns a candidate generation and verified state digest. `activate-replay --generation UUID --expect-position POSITION` performs the guarded switch. Build a new generation of relational views and its corresponding AGE graph in isolation. Run the same versioned event application used for live writes. Reuse the verified bundle reader for each activation. Replay recorded effects, actors, versions and times. Recreate endpoint availability and last-known provenance in committed order. Validate consecutive positions, unique event IDs and known versions. Preserve the journal on error. Rebuild queues are derived from current active content and the selected profile.

Require an operator-controlled write pause before final cutover. Under the head lock, confirm the expected journal position, complete rebuild and invariant checks. Switch the generation pointer atomically and release the pause after verifying reads. A stale candidate or corrupt bundle leaves the original generation available. The worker does not run user authorization during replay.

**Test file** `ReplayTests.cs`

```csharp
using Xunit;

public sealed class ReplayTests
{
    [Fact]
    public async Task EmptyProjectionReplaysLastPublishedProvenanceAndRemovals()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var edge = await s.CreateRelationshipAsync(s.A, s.B, "uses", "Retained edge.");
        var note = await s.CreateAnnotationAsync(s.A, "businessExplanation", "Removed later.");
        await s.RemoveAsync(note.Id, note.Version);
        var updated = await s.ActivateAsync("changed");
        await s.ActivateAsync("missing");
        var before = await s.DigestAsync();
        var rebuilt = await s.RebuildAndCutoverAsync();
        Assert.Equal(before, rebuilt.StateDigest);
        Assert.Equal(before, await s.DigestAsync());
        Assert.Contains(await s.TraverseAsync(s.A, includeUnavailable: true),
            item => item.RuntimeId == edge.Id && item.LastKnownSnapshotId == updated);
        Assert.Equal(2, (await s.HistoryAsync(note.Id)).Count);
        Assert.DoesNotContain(await s.AnnotationsAsync(s.A), item => item.Id == note.Id);
    }
}
```

- [ ] Run red. Add deterministic tests for missing required bundle, unknown event schema, duplicate/inconsistent event IDs, journal gap, stale head at cutover, and a later activation restoring the same identity. Canonical digest includes snapshot, records and history ordered by stable IDs and position and excludes generation IDs and transient search jobs.
- [ ] Implement staging, replay and guarded cutover. Disable side-effectful provider calls during event application. Regenerate derived search work from current content.
- [ ] Run focused and prior tests. Commit `feat(workspace): rebuild projections from snapshots and journal events` and push.

## Task 6 — API and external identity

**Files:** `runtime/RuntimeKnowledge.Api/Program.cs`, `Identity.cs`, `WorkspaceRoutes.cs`, `ProblemResponses.cs`, `runtime/RuntimeKnowledge.Contracts/Api.cs`, `runtime/RuntimeKnowledge.Tests/IdentityTests.cs` and `ApiFixture.cs`.

**Interfaces:** Configure one provider with `Workspace:Identity:Provider` (`Keycloak` or `Entra`), `Issuer`, `Audience`, and for Keycloak `ClientId`. Use standard ASP.NET JWT bearer validation, signature and signing-key metadata, issuer, audience, lifetime and subject. Use the configured Keycloak client's `resource_access[clientId].roles` and Entra's `roles` to derive the two workspace capabilities. Expose `GET /api/session` to authenticated workspace users for effective capabilities. Use issuer plus subject for acting identity, derived from the validated principal. Deny a valid token without a workspace role. Apply policies at every route. Invalid token returns 401 and insufficient permissions return 403.

| Operation | Route and result |
| --- | --- |
| Inventory | `GET /api/inventory?cursor=&limit=` returns the page, gaps and snapshot. |
| Detail | `POST /api/entities/detail`, `POST /api/relationships/detail` take a key in JSON, or a runtime ID for relationship detail, and return both published and runtime context as applicable. |
| Traverse | `POST /api/traverse` takes `{start,direction,maxDepth,includeUnavailable}`. |
| History | `GET /api/history?contributionId=&cursor=&limit=` returns attributed changes. |
| Contribute | `POST /api/contributions/annotations`, `POST /api/contributions/relationships`, `PUT /api/contributions/{id}`, `POST /api/contributions/{id}/remove`, `POST /api/contributions/{id}/codifications`. Edit and remove bodies carry the expected version. |
| Search | Task 7 adds `POST /api/search` with `{query,limit}`. |

Use JSON problem details with stable `code`. Invalid command is 422, missing resource is 404, stale version or cursor is 409, unavailable dependency is 503. Include latest contribution version or removal state on a 409 where authorised. Never echo tokens, connection strings or provider errors with secrets.

**Test file** `IdentityTests.cs`

```csharp
using System.Net;
using Xunit;

public sealed class IdentityTests
{
    [Theory]
    [InlineData("Keycloak")]
    [InlineData("Entra")]
    public async Task WorkspaceRolesAreRecheckedOnEveryRequest(string provider)
    {
        await using var f = await ApiFixture.OpenAsync(provider);
        using var reader = f.ClientWithToken("ReadKnowledgeGraph");
        using var manager = f.ClientWithToken("ManageKnowledgeGraph");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.PostAnnotationAsync(reader)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await f.PostAnnotationAsync(manager)).StatusCode);
        using var expired = f.ClientWithToken("ManageKnowledgeGraph", expired: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.PostAnnotationAsync(expired)).StatusCode);
        using var unrelated = f.ClientWithToken("OtherRole");
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync("/api/inventory")).StatusCode);
        Assert.Equal(1, await f.AnnotationCountAsync());
    }
}
```

- [ ] Run red. The fixture activates the initial Scenario snapshot, serves fixture metadata and signing keys, and uses the production validation path with tokens signed by the fixture key. Add cases for missing token, wrong issuer, wrong audience, bad signature, no role, combined roles, current token reduced from manage to read and Keycloak role under an unrelated client. Use an expiry outside configured clock skew.
- [ ] Wire routes, JWT validation, provider adapters and the single role policy. Forward cancellation and enforce request bounds. Keep browser user tokens out of publisher and worker operations.
- [ ] Run focused and prior tests. Commit `feat(workspace): secure the workspace API with provider role policies` and push.

## Task 7 — semantic discovery and index lifecycle

**Files:** `runtime/RuntimeKnowledge.Contracts/Search.cs`, `runtime/RuntimeKnowledge.Providers/QwenEmbeddings.cs`, `TitanEmbeddings.cs`, `runtime/RuntimeKnowledge.Store/Sql/005_search.sql`, `Search.cs`, `runtime/RuntimeKnowledge.Worker/IndexCommand.cs`, `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs`, `runtime/RuntimeKnowledge.Tests/SearchTests.cs` and `EmbeddingAdapterTests.cs`.

**Interfaces:** `IEmbeddingProvider.EmbedAsync(EmbeddingProfile profile, string input, EmbeddingPurpose purpose, CancellationToken) -> Task<float[]>`, `IndexWorker.RunBatchAsync(int limit, CancellationToken) -> Task<IndexBatchResult>`, `SearchStore.SearchAsync(string query,int limit,CancellationToken) -> Task<SearchResult>`. Providers validate the selected profile and return exactly that dimension, with finite nonzero data. Call Qwen through a configured OpenAI-compatible embeddings endpoint for Ollama or the LocalAI model server. Use `qwen3-embedding:0.6b` or the pinned 8B Q4 model at 1,024 and 4,096 dimensions respectively. Use Bedrock Runtime `InvokeModel` with `amazon.titan-embed-text-v2:0`, `{inputText,dimensions:1024,normalize:true}`. Pin the actual model artifact revision per deployment. Tests use stub HTTP responses and a recording Bedrock client. [Titan request reference](https://docs.aws.amazon.com/bedrock/latest/userguide/model-card-amazon-titan-text-embeddings-v2.html)

Embed published labels and descriptions and active annotation text separately so results can cite the source passage. Key published vectors by snapshot and key annotation vectors by contribution revision and profile. Resolve annotation visibility against the active snapshot at query time so a retained annotation follows its source identity across baseline changes. Record a text profile version and distinguish query instruction text from document text where the provider profile requires it. Use exact pgvector cosine distance. Scope results to the active snapshot, profile, current record version and endpoint availability. Bound result count and enforce a configured similarity cutoff. Pin the profile before embedding a query and recheck it with the snapshot used for retrieval. Retry once on concurrent profile activation and surface a retryable status if it keeps changing.

Enqueue jobs keyed by scope, record ID, revision and embedding profile. Claim bounded batches with expiring leases, perform model calls outside database transactions, and upsert only if the queued record still has the requested version and scope. Retries can complete the same job once. Stage baseline vectors before baseline activation. Build replacement profile vectors before switching and catch up journal changes under the activation lock. Return pending coverage separately from genuine no matches and report a provider outage as 503. Graph reads, saves and traversal work while the embedding provider is unavailable.

**Test file** `SearchTests.cs`

```csharp
using Xunit;

public sealed class SearchTests
{
    [Fact]
    public async Task DelayedJobsCannotExposeStaleOrRemovedAnnotations()
    {
        await using var s = await SearchScenario.OpenAsync();
        var annotation = await s.CreateAnnotationAsync("Reconciliation closes invoices.");
        Assert.True((await s.SearchAsync("reconcile unpaid invoices")).IndexPending);
        await s.RunIndexBatchAsync();
        Assert.Contains((await s.SearchAsync("reconcile unpaid invoices")).Hits,
            hit => hit.AnnotationId == annotation.Id);
        await s.EditAnnotationAsync(annotation.Id, annotation.Version, "Archive a delivery note.");
        await s.CompleteOldJobAsync(annotation.Id, annotation.Version);
        Assert.DoesNotContain((await s.SearchAsync("reconcile unpaid invoices")).Hits,
            hit => hit.AnnotationId == annotation.Id);
        await s.RunIndexBatchAsync();
        await s.RemoveAsync(annotation.Id, annotation.Version + 1);
        Assert.DoesNotContain((await s.SearchAsync("archive a delivery note")).Hits,
            hit => hit.AnnotationId == annotation.Id);
    }
}
```

- [ ] Run the class filters red. `SearchScenario` wraps the real PostgreSQL store with an embedding stub that maps the two example text pairs to distinct unit vectors and irrelevant text to an orthogonal vector. Use a fixed cutoff of 0.8 in the fixture. Check actual provenance and no-match response, old profile isolation, wrong dimensions, concurrent profile change, model outage and stale-job completion. Test the actual Qwen and Titan request and response adapters in `EmbeddingAdapterTests`.
- [ ] Implement adapters, job leasing, staged profile activation and the route. Measure exact query latency and concurrent memory use at a representative home-lab volume before suggesting a different index.
- [ ] Run focused and prior tests. Commit `feat(workspace): index and search active knowledge with provider embeddings` and push.

## Task 8 — standalone Blazor workspace

**Files:** `runtime/RuntimeKnowledge.Web/Program.cs`, `Services/WorkspaceClient.cs`, `Services/DraftState.cs`, `Pages/Explore.razor`, `Pages/Entity.razor`, `Pages/Relationship.razor`, `Pages/History.razor`, `Components/ContributionEditor.razor`, `wwwroot/appsettings.json`, `runtime/RuntimeKnowledge.Tests/WorkspaceUiTests.cs`.

**Interfaces:** Browser OIDC configuration supplies authority, SPA client, audience/API scope and redirect path. Use authorization code and PKCE. Send the API an access token, retrieve capabilities from `/api/session` and render manager controls from that result. `WorkspaceClient` calls the documented routes and translates problem responses. `DraftState` holds draft text, selected endpoints, meaning and the originally read revision through conflict or reauthentication in the same browser session. For a stale save, show the newest server record beside the unsaved draft and require a deliberate review and resubmit action. On sign-in expiry, preserve the draft and require a deliberate retry after authentication. Keep draft storage scoped to the session and clear it on explicit sign-out or successful submission.

Render source IDs, origin, snapshot, basis and evidence on detail and path views. Include isolated inventory records, direction and an explicit unavailable-endpoint option. Render business RBAC as context. Provide annotation categories, published type and custom meaning entry, and explicit association controls. Search distinguishes a pending index, provider unavailability and no matches. History and associations show both origins. Keep UI navigation and editable values keyed by stable IDs.

**Test file** `WorkspaceUiTests.cs`

```csharp
using Xunit;

public sealed class WorkspaceUiTests
{
    [Fact]
    public async Task ConflictRequiresReviewAndPreservesTheDraft()
    {
        using var f = WorkspaceUiFixture.Create();
        var editor = f.RenderAnnotationEditor("First revision", version: 1);
        editor.EnterText("My unsaved change");
        f.Api.NextSaveReturnsConflict("Other user's change", currentVersion: 2);
        await editor.SaveAsync();
        Assert.Equal("My unsaved change", editor.DraftText);
        Assert.Contains("Other user's change", editor.VisibleText);
        Assert.Equal(1, f.Api.SaveCount);
        Assert.False(editor.CanResubmit);
        editor.ConfirmReviewed();
        await editor.SaveAsync();
        Assert.Equal(2, f.Api.LastSubmittedVersion);
    }
}
```

- [ ] Run the class filter red. Build `WorkspaceUiFixture` with a bUnit render context, stub API and the component's actual inputs and controls. Exercise a 401 after draft entry, reader controls, distinct identical labels, unavailable path controls, indexed versus pending state, and separate codification provenances. Use accessible names and visible results for test selectors.
- [ ] Implement the UI and statically publish it with runtime-configurable nonsecret identity settings. Use a relative `/api` base URL. Keep the API as the permission boundary.
- [ ] Run focused and prior tests. Commit `feat(workspace): add the Blazor knowledge exploration and contribution UI` and push.

## Task 9 — full Docker Compose on Mac

**Files:** `deploy/workspace/local/compose.yml`, `realm.json`, `bootstrap.sh`, `smoke.sh`, `Caddyfile`, `deploy/workspace/app/Dockerfile`, `deploy/workspace/web/Dockerfile`, `src/workspace/seed-fixture.mjs` and `deploy/workspace/verify.sh`.

**Interfaces:** `docker compose -f deploy/workspace/local/compose.yml up --build -d --wait` starts web, API, publisher/worker, Keycloak, MinIO, PostgreSQL and a local Qwen endpoint. Declare persistent named DB and object volumes. Run migrations with a separate bootstrap role. Keycloak has a public SPA client with API audience mapping and two development-only users/roles. The same issuer must be reachable from browser and API. Route through one development host, with an internal DNS alias for that same issuer. Use distinct private service roles for import and indexing. Bootstrap creates the bucket, prepares the selected model, uploads a synthetic pinned bundle and activates it through the normal worker path. Serve Blazor assets with SPA fallback and forward `/api` to the backend. Health checks cover dependencies, migrations and model readiness before running smoke.

**Test file** `deploy/workspace/local/smoke.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
compose=(docker compose -f "$root/deploy/workspace/local/compose.yml")
"${compose[@]}" up --build -d --wait
"$root/deploy/workspace/local/bootstrap.sh"
"$root/deploy/workspace/verify.sh" mac
"${compose[@]}" restart api db
"${compose[@]}" up -d --wait
"$root/deploy/workspace/verify.sh" mac --check-persistence
```

- [ ] Run the smoke test red in an isolated Compose project. Implement an initial `verify.sh mac` runner here with a real Keycloak browser login, inventory including the seed snapshot, manager save and reader traversal. Task 12 extends the runner to the full scenario. For persistence, save the scenario IDs and expected snapshot in the temporary test project and reread them after restart.
- [ ] Implement images and compose wiring with reproducible nonsecret defaults, including the arm64 path. Keep the smoke credentials and fixture project isolated from real deployments. Use an ephemeral shell path or generated local values for secrets. The validation runner does not remove unrelated Docker volumes.
- [ ] Run the smoke and prior checks. Commit `feat(workspace): ship the full Mac Docker development stack` and push.

## Task 10 — LocalAI home-lab deployment contract

**Files:** `deploy/workspace/home-lab/compose.yml`, `Caddyfile.fragment`, `realm-template.json`, `deployment-contract.md`, `test.env`, `verify-config.sh`. Reuse the app, web and database images.

**Interfaces:** Publish application artefacts and a required configuration contract for LocalAI to install by release or commit. Use existing shared Keycloak and Caddy, workspace client roles, a configured Qwen3 Embedding 8B Q4 endpoint, and MinIO and the database on persistent storage. Only web and API join the ingress network. Require explicit host, issuer, API audience, DB and object endpoint and model revision values. Keep journal and object volumes across deployment. Keep secrets outside the checked-in files. Document which resources the release supplies and which settings LocalAI owns. Do not add deployment state or credentials to the application repository.

**Test file** `deploy/workspace/home-lab/verify-config.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
docker compose --env-file "$root/deploy/workspace/home-lab/test.env" -f "$root/deploy/workspace/home-lab/compose.yml" config --format json |
  jq -e '
    .services.db.volumes | any(.target == "/var/lib/postgresql/data")
  '
docker compose --env-file "$root/deploy/workspace/home-lab/test.env" -f "$root/deploy/workspace/home-lab/compose.yml" config --format json |
  jq -e '
    .services.api.environment.Workspace__Embeddings__Dimensions == "4096"
    and ((.services.db.ports // []) | length == 0)
    and ((.services.minio.ports // []) | length == 0)
  '
```

- [ ] Create the synthetic `test.env` with placeholders and run the check red. Check the web and API shared route, no DB and MinIO ingress, required secrets and model endpoint and Keycloak client config without seeded users. When the operator deploys the reviewed release, run the home-lab scenario through actual Caddy and Keycloak. Record the 8B model revision, exact search measurements and state across restart.
- [ ] Implement the versioned artefacts and confirm the rendered contract follows [LocalAI’s JointBooking installation boundary](https://github.com/jamiemitchellconsultants/LocalAI/blob/main/docs/deploy-jointbooking.md). Make the home-lab target consumable from LocalAI. Coordinate host changes through that repo's workflow when authorised.
- [ ] Run focused and prior checks. Commit `feat(workspace): provide the LocalAI home-lab deployment contract` and push.

## Task 11 — AWS Terraform and local simulation

**Files:** `deploy/workspace/aws/versions.tf`, `variables.tf`, `network.tf`, `storage.tf`, `database.tf`, `application.tf`, `identity.tf`, `outputs.tf`, `bootstrap-db.sh.tftpl`, `tests/workspace.tftest.hcl`, `runtime/RuntimeKnowledge.Api/LambdaHosting.cs`, `runtime/RuntimeKnowledge.Worker/LambdaEntry.cs`, `runtime/RuntimeKnowledge.Tests/AwsSimulationTests.cs`.

**Interfaces:** Terraform inputs include region, environment prefix, VPC/subnets or their CIDRs, Entra issuer/audience/SPA client, private database image reference, encrypted volume size, deployable web and function artifacts, and referenced secrets ARNs. Provision private S3 for web and bundle objects, CloudFront origin access and SPA routing, and forward `/api/*` to an API Gateway HTTP API. API Lambda uses the same ASP.NET routes through Lambda hosting. Scheduled worker Lambda invokes one bounded index/import batch per run. Import state is resumable between bounded runs. A larger job can use the same container worker entry point if measured Lambda limits warrant it. See [AWS ASP.NET Lambda hosting](https://docs.aws.amazon.com/lambda/latest/dg/csharp-package-asp.html).

Run the database image on a private EC2 instance. Use a dedicated encrypted EBS data volume with lifecycle protection and mount it before starting the container. Initialise an empty data device once and reuse an existing filesystem on redeploy. Permit PostgreSQL only from application and worker security groups. Bound each Lambda's pool and reserved concurrency to the agreed database connection budget. Provide outbound HTTPS for Entra signing metadata and AWS endpoints as needed. Configure IAM per workload with scoped S3 access, Bedrock invocation for the configured model, Secrets Manager reads and CloudWatch logging. Inject credentials at runtime. Entra app registrations and role assignments are externally managed.

**Test file** `deploy/workspace/aws/tests/workspace.tftest.hcl`

```hcl
mock_provider "aws" {}

run "private_database_and_serverless_api" {
  command = plan

  variables {
    environment = "workspace-test"
    aws_region = "eu-west-2"
    entra_issuer = "https://login.example.test/tenant/v2.0"
    entra_audience = "api://fixture"
    entra_client_id = "fixture-client"
    database_image = "registry.example.test/workspace-db:fixture"
    database_secret_arn = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:fixture"
  }

  assert {
    condition = aws_ebs_volume.database.encrypted && !aws_instance.database.associate_public_ip_address
    error_message = "Workspace PostgreSQL needs encrypted durable storage on a private host."
  }

  assert {
    condition = aws_lambda_function.api.reserved_concurrent_executions > 0
    error_message = "The API must bound Lambda concurrency for database connections."
  }

  assert {
    condition = aws_s3_bucket_public_access_block.bundles.block_public_policy
    error_message = "Published bundles must be private at the AWS storage boundary."
  }
}
```

- [ ] Run `terraform -chdir=deploy/workspace/aws init -backend=false`, `validate` and `test` with mocks. Verify the new assertions fail before wiring. Supply synthetic defaults or fixture inputs for all other required variables. Assert the AWS model, Entra provider, application/worker IAM boundaries, private ingress and EBS persistence.
- [ ] In `AwsSimulationTests`, invoke the real API Gateway Lambda entry and the worker handler with a signed Entra-shaped token and fake metadata, S3 client and Bedrock adapter. Assert HTTP role denials and successful commit/traversal against Docker PostgreSQL. Assert that the worker can stop and resume without duplicate journal events. Add S3 and Titan request serialization checks. Use stub transports that fail on attempted live egress.
- [ ] Implement Terraform and Lambda wiring. Run the mocked suite and handler simulation with no AWS credentials. No apply. Record any cloud-only assumptions for later verification.
- [ ] Run focused and prior checks. Commit `feat(workspace): define simulated AWS deployment with Docker PostgreSQL` and push.

## Task 12 — cross-component behaviour verification

**Files:** `runtime/RuntimeKnowledge.Tests/WorkspaceScenarioTests.cs`, `runtime/RuntimeKnowledge.Tests/ScenarioRunner.cs`, `deploy/workspace/verify.sh`.

**Interfaces:** `deploy/workspace/verify.sh mac|home-lab|aws-sim [--check-persistence]` runs the real API contract against a dedicated synthetic workspace or AWS simulation. Real deployed scenarios use controlled reader and manager identities supplied by the operator. Use namespaced fixtures and record the environment, application version, active snapshot and model revision. The build produces engineering evidence for the user phase.

**Test file** `WorkspaceScenarioTests.cs`

```csharp
using Xunit;

public sealed class WorkspaceScenarioTests
{
    [Fact]
    public async Task BaselineAndRuntimeKnowledgeSurviveRebuild()
    {
        await using var s = await WorkspaceScenario.OpenAsync();
        var first = await s.ActivateAsync("initial");
        var edge = await s.Manager.CreateRelationshipAsync(s.A, s.B);
        var note = await s.Manager.AnnotateAsync(s.A, "businessRbac", "Finance approvers reconcile orders.");
        await s.WaitForCurrentIndexAsync(note.Id);
        Assert.Contains((await s.Reader.SearchAsync("who reconciles orders")).Hits,
            hit => hit.AnnotationId == note.Id && hit.Provenance.SnapshotId == first);
        Assert.Contains(await s.Reader.TraverseAsync(s.A), hit => hit.RuntimeId == edge.Id);
        await s.Manager.AssociateAsync(edge.Id, edge.Version, s.PublishedEdge);
        var changed = await s.ActivateAsync("changed");
        await s.ActivateAsync("missing");
        var before = await s.DigestAsync();
        await s.RebuildAndCutoverAsync();
        Assert.Equal(before, await s.DigestAsync());
        Assert.DoesNotContain(await s.Reader.TraverseAsync(s.A), hit => hit.RuntimeId == edge.Id);
        Assert.Contains(await s.Reader.TraverseAsync(s.A, includeUnavailable: true),
            hit => hit.RuntimeId == edge.Id && hit.LastKnownSnapshotId == changed);
        await s.ActivateAsync("returned");
        Assert.Contains(await s.Reader.TraverseAsync(s.A), hit => hit.RuntimeId == edge.Id);
    }
}
```

- [ ] Run the scenario initially and identify the missing end-to-end assertion or entry point. Back the runner with actual APIs, real database queries and deterministic model fixtures for the simulation, and the selected model for environment smoke. Keep all destructive replay tests inside an isolated scenario.
- [ ] Add scenario checks for no graph coverage, distinct same-label identities, reader denials, manager edit conflict, inactive records and explicit codification. For B13, collect the meaning, context and provenance a fixture owner would need. B14 is performed by the nominated people in the user phase. Include a clean rebuild into empty projections without dropping the journal.
- [ ] Run the repository tests, runtime integration suite, local full stack, home-lab checks when available and credential-free AWS simulation. Record actual results and identify any unexecuted target. Review the complete build diff and task commits.
- [ ] Commit `test(workspace): verify the integrated knowledge and replay workflows` and push.
