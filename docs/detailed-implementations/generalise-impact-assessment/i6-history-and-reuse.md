# I6. History and reuse: detailed implementation plan

**Inputs:**
- [Behaviours](../../features/generalise-impact-assessment/02-behaviours.md): B21 and B35 are delivered here, respecting Q2 and Q6.
- [Task overview](../../superpowers/plans/generalise-impact-assessment/plan.md), and I5's store, routes and pages.

**Start:** I5 is done and its handoff is posted.

---

## Task 34: Saved assessment list (B21)

**Files:**
- modify `runtime/RuntimeKnowledge.Store/Assessments.cs` (`ListAsync`), `runtime/RuntimeKnowledge.Contracts/Impact.cs`, `runtime/RuntimeKnowledge.Api/AssessmentRoutes.cs`, `WorkspaceOpenApi.cs` and `WorkspaceLinks.cs`, and regenerate the spec;
- create `runtime/RuntimeKnowledge.Web/Pages/Assessments.razor` (`/assessments`), and add a nav link `Assessments` in `WorkspaceLayout.razor`;
- create `runtime/RuntimeKnowledge.Tests/AssessmentListTests.cs` (category `Database`), and add bUnit cases to `AssessmentUiTests.cs`.

### 34.1 Contract and store

```csharp
public sealed record AssessmentSummary(Guid Id, string RequestId, Actor Author, DateTimeOffset SavedAt, string SnapshotId, bool PinnedToEarlierSnapshot);
public sealed record AssessmentPage(IReadOnlyList<AssessmentSummary> Assessments, string? NextCursor);
```

`AssessmentStore.ListAsync(string? cursor, int limit, CancellationToken token)`:
- **Limit.** `limit` defaults to 50 and must be from 1 to 50. Otherwise throw `invalidLimit`, with the message `Assessment list limit must be from 1 to 50.`
- **Order.** Newest first by `saved_at`, then by `assessment_id` descending, which breaks ties on the same time.
- **The cursor** is base64url of `{"v":1,"t":"<saved_at as ISO 8601, round-trip format>","id":"<uuid>"}`. The next page reads `WHERE (saved_at, assessment_id) < ($t, $id)` with `ORDER BY saved_at DESC, assessment_id DESC LIMIT limit + 1`, as `HistoryAsync` does.
- **Errors.** A malformed cursor is `invalidCursor`.
- **Last page.** `NextCursor` is null when no more rows remain.
- **No offsets** anywhere.

The route is `GET /api/assessments?cursor=&limit=`, with the read policy, an `assessmentList` operation id, and a `next` link through `WorkspaceLinks.NextPage("/api/assessments", ...)`. Its errors are `invalidLimit`, `invalidCursor` and `snapshotUnavailable`.

### 34.2 `AssessmentListTests.cs`

```csharp
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class AssessmentListTests
{
    private const string Frozen = "{\"format\":1,\"record\":{}}";
    private static readonly Actor Manager = new("https://issuer.test", "manager");

    private sealed class SteppingClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public bool Freeze { get; set; }
        public override DateTimeOffset GetUtcNow() { var now = _now; if (!Freeze) _now = _now.AddSeconds(1); return now; }
    }

    [Fact]
    public async Task Lists_newest_first_in_pages_of_at_most_50_with_a_cursor()
    {
        await using var scenario = await Scenario.OpenAsync();
        var snapshot = await scenario.ActivateAsync("initial");
        var store = new AssessmentStore(scenario.Db, new SteppingClock(DateTimeOffset.Parse("2026-11-27T00:00:00Z")));
        var ids = new List<Guid>();
        for (var i = 0; i < 120; i++) ids.Add((await store.SaveAsync(Manager, snapshot, $"CR-{i:000}", Frozen, "# x\n", null, CancellationToken.None)).Id);
        var seen = new List<AssessmentSummary>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await store.ListAsync(cursor, 50, CancellationToken.None);
            Assert.True(page.Assessments.Count <= 50);
            seen.AddRange(page.Assessments);
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null);
        Assert.Equal(3, pages);
        Assert.Equal(Enumerable.Reverse(ids), seen.Select(s => s.Id));
        Assert.Equal("CR-119", seen[0].RequestId);
        Assert.All(seen, s => Assert.Equal(snapshot, s.SnapshotId));
    }

    [Fact]
    public async Task Equal_save_times_keep_a_stable_order_across_pages()
    {
        await using var scenario = await Scenario.OpenAsync();
        var snapshot = await scenario.ActivateAsync("initial");
        var store = new AssessmentStore(scenario.Db, new SteppingClock(DateTimeOffset.Parse("2026-11-27T00:00:00Z")) { Freeze = true });
        for (var i = 0; i < 7; i++) await store.SaveAsync(Manager, snapshot, $"CR-{i}", Frozen, "# x\n", null, CancellationToken.None);
        async Task<List<Guid>> ReadAll()
        {
            var ids = new List<Guid>();
            string? cursor = null;
            do
            {
                var page = await store.ListAsync(cursor, 3, CancellationToken.None);
                ids.AddRange(page.Assessments.Select(a => a.Id));
                cursor = page.NextCursor;
            } while (cursor is not null);
            return ids;
        }
        var firstRead = await ReadAll();
        Assert.Equal(7, firstRead.Distinct().Count());              // no row repeated or skipped across page boundaries
        Assert.Equal(firstRead, await ReadAll());                   // the tie order is stable between reads
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Limits_outside_1_to_50_are_refused(int limit)
    {
        await using var scenario = await Scenario.OpenAsync();
        await scenario.ActivateAsync("initial");
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => new AssessmentStore(scenario.Db).ListAsync(null, limit, CancellationToken.None));
        Assert.Equal("invalidLimit", error.Code);
    }

    [Fact]
    public async Task A_malformed_cursor_is_invalid()
    {
        await using var scenario = await Scenario.OpenAsync();
        await scenario.ActivateAsync("initial");
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => new AssessmentStore(scenario.Db).ListAsync("not-a-cursor", 50, CancellationToken.None));
        Assert.Equal("invalidCursor", error.Code);
    }
}
```

The tie test compares the store's order with itself rather than with .NET's `Guid` ordering, because PostgreSQL orders `uuid` values by their bytes.

### 34.3 Page

`Assessments.razor` is a table with the columns Request id (a link to `/assessments/{id}`), Author, Saved, Snapshot and Pinned. Pinned shows `Pinned to an earlier snapshot` when it applies. **Load more** follows `NextCursor`. The bUnit cases are: the rows render, and Load more sends the cursor.

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement, and regenerate the spec.
- [ ] Commit `feat: list saved assessments (B21)` and push.

---

## Task 35: Start from an earlier assessment (B35)

**Files:** modify `AssessmentRoutes.cs`, `WorkspaceOpenApi.cs`, `Contracts/Impact.cs`, `WorkspaceClient.cs`, `Pages/Assessment.razor` and `Services/AssessmentDraft.cs` (`Start` already accepts `startedFrom`). Create `runtime/RuntimeKnowledge.Tests/AssessmentSuccessorTests.cs` (category `Database`).

### 35.1 Route

`POST /api/assessments/{id:guid}/successor`, with **`ManagePolicy`** (B35, as amended in the architecture):
1. Load the saved assessment, or raise `assessmentNotFound`.
2. Get the active model through `ImpactModelCache.GetAsync(null)`.
3. Return `AssessmentSuccessor(string SnapshotId, JsonObject Record)`. The `Record` is `Assessments.New(model, requestId, description)`, with `record["startedFrom"] = {"assessmentId": id}`, and holds nothing else from the earlier assessment.

Nothing is stored. The errors are `assessmentNotFound`, `impactUnavailable` and `snapshotUnavailable`.

**Save.** `SaveAsync` already takes `startedFrom`. The save route reads it from `record.startedFrom.assessmentId` when present. The store checks that this id exists, or raises `assessmentNotFound`. `SavedAssessment.StartedFrom` links back (B35).

### 35.2 `AssessmentSuccessorTests.cs`

1. `A_manager_starts_a_new_draft_on_the_active_snapshot_holding_only_the_request`:
   - Save on the confluence snapshot, then activate a second corpus bundle of the same scenario. Make it differ by adding `inputs/999/marker.txt` as an `inspectedSource` file, so its manifest id differs while the model input does not.
   - Call `successor`.
   - Assert that `SnapshotId` is the new active id, that `Record.requestId` and `Record.description` equal the earlier ones, and that `confirmedLeads`, `rejectedLeads`, `likelyImpacts` and `unresolved` are empty.
   - Assert that `Record.startedFrom.assessmentId` is the earlier id, and that the pins are the active model's.
2. `A_reader_without_manage_gets_403`.
3. `Saving_the_successor_links_back_to_the_earlier_assessment`: `GET` the new one, and check that `StartedFrom` equals the earlier id.
4. `An_unknown_earlier_assessment_is_not_found`: 404, `assessmentNotFound`.

### 35.3 Page

`Assessment.razor` shows **Start a new assessment from this one** only to managers. It calls `successor`, then `Draft.Start(...)` with the returned record's fields and `startedFrom`, and navigates to `/assessments/draft`. The saved view of an assessment that has `StartedFrom` shows `Started from` with a link to the earlier assessment.

The bUnit cases are: the button is hidden for readers, and clicking it starts a draft with `startedFrom`.

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement, and regenerate the spec.
- [ ] Commit `feat: start a new assessment from a saved one (B35)` and push.

## I6 close and build phase completion

- [ ] CI is green, and the database, browser and conformance suites pass.
- [ ] Tick the [Dev] Build criteria, open the build pull request that closes [Dev] Build, and post the handoff to the user role, following the feature-dev skill's build step 4. **Stop.**
