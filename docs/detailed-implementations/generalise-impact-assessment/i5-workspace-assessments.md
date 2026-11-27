# I5. Workspace drafts, saves and sharing: detailed implementation plan

**Inputs:**
- [Behaviours](../../features/generalise-impact-assessment/02-behaviours.md): B17, B18, B19, B20, B22, B23, B30, B31, B32, B33, B34 and B36 are delivered here, respecting Q2, Q3, Q4, Q5 and Q6.
- ADRs [0033](../../adr/0033-freeze-saved-assessments-in-the-journal.md), [0034](../../adr/0034-keep-assessment-drafts-in-browser-memory.md), [0005](../../adr/0005-keep-graph-history-and-vectors-in-postgresql.md) and [0008](../../adr/0008-rebuild-workspace-from-snapshots-and-events.md).
- [Task overview](../../superpowers/plans/generalise-impact-assessment/plan.md), and I4's helpers (`AddCorpusBundle`, `ApiFixture.OpenAsync(provider, arrange)`).

**Start:** I4 is done and its handoff is posted.

## Decisions fixed by this plan, within the architecture

- **Byte-exact storage (Q5).** `jsonb` reorders keys, so frozen content and Markdown are stored as UTF-8 `bytea` written by `EcmaJson`. In the journal (`payload jsonb`), they travel as JSON **strings**, `frozen` and `markdown`, and `jsonb` preserves strings exactly.
- **Two snapshot errors.**
  - `snapshotChanged` (409) refuses a **draft** action whose `snapshotId` is not the active snapshot (B36).
  - `snapshotMismatch` (409) refuses an **edit of a saved assessment** whose own snapshot is no longer active. B33 names this error.
- **Assessment events are not contributions.** `ContributionStore.HistoryAsync` must keep listing only contribution events. Its query gains `AND event_type NOT LIKE 'assessment%'`, so assessment events never reach `ContributionChange` parsing.

---

## Task 28: Assessment storage

**Files:** create `runtime/RuntimeKnowledge.Store/Sql/007_assessments.sql`. Add a case to `runtime/RuntimeKnowledge.Tests/DatabaseTests.cs` asserting that both tables exist with the listed grants.

```sql
CREATE TABLE workspace_projection.assessments (
    generation uuid NOT NULL,
    assessment_id uuid NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    snapshot_id text NOT NULL CHECK (snapshot_id ~ '^[0-9a-f]{64}$'),
    request_id text NOT NULL,
    author_issuer text NOT NULL,
    author_subject text NOT NULL,
    saved_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    frozen bytea NOT NULL,
    markdown bytea NOT NULL,
    started_from uuid,
    PRIMARY KEY (generation, assessment_id)
);
CREATE INDEX assessments_newest
    ON workspace_projection.assessments (generation, saved_at DESC, assessment_id DESC);

CREATE TABLE workspace_projection.assessment_revisions (
    generation uuid NOT NULL,
    assessment_id uuid NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    editor_issuer text NOT NULL,
    editor_subject text NOT NULL,
    edited_at timestamptz NOT NULL,
    frozen bytea NOT NULL,
    markdown bytea NOT NULL,
    PRIMARY KEY (generation, assessment_id, version)
);

GRANT SELECT, INSERT, UPDATE ON workspace_projection.assessments TO workspace_api, workspace_publisher;
GRANT SELECT, INSERT ON workspace_projection.assessment_revisions TO workspace_api, workspace_publisher;
```

Version 1 is written to `assessment_revisions` at save time too, so revision history is complete (B22).

- [ ] Write the test, and confirm that it fails.
- [ ] Add the migration.
- [ ] Commit `feat: add assessment tables (#198)` and push.

---

## Task 29: Assessment store and replay (B18, B20, B22, B31, B33, B34, Q5)

**Files:**
- create `runtime/RuntimeKnowledge.Store/Assessments.cs`;
- modify `Contributions.cs` (the history filter) and `Replay.cs` (event branch and digest);
- modify `runtime/RuntimeKnowledge.Contracts/Impact.cs` (the records below);
- create `runtime/RuntimeKnowledge.Tests/AssessmentStoreTests.cs`.

### 29.1 Contracts

```csharp
public sealed record AssessmentDraftRequest(string SnapshotId, JsonObject Record, IReadOnlyList<JsonObject>? Views = null);
public sealed record AssessmentEditRequest(string SnapshotId, int ExpectedVersion, JsonObject Record, IReadOnlyList<JsonObject>? Views = null);
public sealed record AssessmentPreview(string SnapshotId, string Digest, JsonObject Frozen, string Markdown);
public sealed record AssessmentSaved(Guid Id, int Version, long Position);
public sealed record AssessmentRevision(int Version, Actor Editor, DateTimeOffset EditedAt, JsonObject Frozen, string Markdown);
public sealed record SavedAssessment(Guid Id, int Version, string SnapshotId, string RequestId, Actor Author,
    DateTimeOffset SavedAt, DateTimeOffset UpdatedAt, bool PinnedToEarlierSnapshot, Guid? StartedFrom,
    JsonObject Frozen, string Markdown, IReadOnlyList<AssessmentRevision> Revisions);
```

### 29.2 `AssessmentStore`

```csharp
public sealed class AssessmentStore(Db db, TimeProvider? clock = null)
{
    internal Func<Task>? BeforeCommit { get; set; }            // test hook: throw here to prove atomicity (Q5)

    public Task<AssessmentSaved> SaveAsync(Actor actor, string expectedSnapshot, string requestId,
        string frozenJson, string markdown, Guid? startedFrom, CancellationToken token);
    public Task<AssessmentSaved> ReviseAsync(Actor actor, Guid id, int expectedVersion, string expectedSnapshot,
        string frozenJson, string markdown, CancellationToken token);
    public Task<SavedAssessment?> GetAsync(Guid id, CancellationToken token);
    public Task<byte[]?> MarkdownAsync(Guid id, CancellationToken token);
    internal static Task ApplyRecordedAsync(NpgsqlTransaction tx, ProjectionState state, string type,
        Guid id, int version, Actor actor, DateTimeOffset at, string snapshotId, AssessmentEvent payload, CancellationToken token);
}
internal sealed record AssessmentEvent(Guid AssessmentId, int Version, string RequestId, string Frozen, string Markdown, Guid? StartedFrom);
```

**`SaveAsync`** follows `ContributionStore.ApplyAsync`:
1. Check the actor, then open a transaction and call `LockHeadAsync`.
2. Read the state, `RequireSnapshot`, and refuse if writes are paused (`writesPaused`).
3. If the active snapshot differs from `expectedSnapshot`, throw `snapshotChanged`.
4. Create `id = Guid.NewGuid()`, and append the event `assessmentSaved`, aggregate `id`, revision 1, with the payload `AssessmentEvent`.
5. `ApplyRecordedAsync` inserts the `assessments` row and revision 1.
6. Await `BeforeCommit` if it is set, then commit.

**`ReviseAsync`**:
1. Lock the head and read the row with `FOR UPDATE`. If there is none, throw `assessmentNotFound`.
2. If `row.version != expectedVersion`, throw `versionConflict`.
3. If `row.snapshot_id` is not the active snapshot, throw `snapshotMismatch` with the message `This assessment is pinned to an earlier snapshot and cannot be edited. Start a new assessment from it.` (B33).
4. If the active snapshot differs from `expectedSnapshot`, throw `snapshotChanged`.
5. Append `assessmentRevised` with revision `version + 1`. Update the row: `version`, `updated_at`, `frozen` and `markdown`. Insert the revision.

**`GetAsync`** reads the row, every revision ordered by version, and the active snapshot. It sets `PinnedToEarlierSnapshot = row.snapshot_id != active` (B34), and decodes `frozen` with `JsonNode.Parse`, which preserves key order.

**Replay.** In `RebuildAsync`'s loop, events whose `Type` is `assessmentSaved` or `assessmentRevised` are read as `AssessmentEvent`. They are checked: the candidate snapshot equals `entry.SnapshotId`, and the actor is present. Then they go to `AssessmentStore.ApplyRecordedAsync`, instead of the contribution path. `DigestAsync` gains a section listing `assessment_id, version, snapshot_id, encode(sha256(frozen),'hex'), encode(sha256(markdown),'hex')`, ordered by `assessment_id`. The same section is added for `assessment_revisions`, ordered by `(assessment_id, version)`.

**Store-level refusals.** The store refuses a frozen body that contains the `approved` or `approvalStatus` keys, or any key containing `signoff`, at the top level of `record`. It throws `approvalNotSupported`. This is a second guard behind the engine's (B31).

### 29.3 `AssessmentStoreTests.cs`

```csharp
using System.Text;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class AssessmentStoreTests
{
    private const string Frozen = "{\"format\":1,\"record\":{\"requestId\":\"CR-9\",\"z\":1,\"a\":2},\"digest\":\"d\"}";
    private const string Markdown = "# Impact assessment: CR-9\n\nbody | &#124;\n";
    private static readonly Actor Manager = new("https://issuer.test", "manager");

    private static async Task<(Scenario Scenario, AssessmentStore Store, string Snapshot)> OpenAsync()
    {
        var scenario = await Scenario.OpenAsync();
        var snapshot = await scenario.ActivateAsync("initial");
        return (scenario, new AssessmentStore(scenario.Db), snapshot);
    }

    [Fact]
    public async Task A_save_stores_exact_bytes_and_reads_back_identically()                   // B18, B20, Q5
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var read = await store.GetAsync(saved.Id, CancellationToken.None);
        Assert.Equal(Frozen, read!.Frozen.ToJsonString());
        Assert.Equal(Markdown, read.Markdown);
        Assert.Equal(Encoding.UTF8.GetBytes(Markdown), await store.MarkdownAsync(saved.Id, CancellationToken.None));
        Assert.Equal(Manager, read.Author);
        Assert.Equal(1, read.Version);
        Assert.False(read.PinnedToEarlierSnapshot);
        Assert.Single(read.Revisions);
    }

    [Fact]
    public async Task A_failed_save_leaves_nothing_behind()                                    // B18, Q5
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        store.BeforeCommit = () => throw new InvalidOperationException("interrupted");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None));
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var count = new Npgsql.NpgsqlCommand(
            "SELECT (SELECT count(*) FROM workspace_projection.assessments) + (SELECT count(*) FROM workspace_journal.events WHERE event_type LIKE 'assessment%')", connection);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Saving_against_another_snapshot_is_snapshot_changed()                    // B36
    {
        var (scenario, store, _) = await OpenAsync();
        await using var __ = scenario;
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => store.SaveAsync(Manager, new string('0', 64), "CR-9", Frozen, Markdown, null, CancellationToken.None));
        Assert.Equal("snapshotChanged", error.Code);
    }

    [Fact]
    public async Task An_edit_keeps_the_previous_content_editor_and_time()                    // B22
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var editor = new Actor("https://issuer.test", "second-manager");
        await store.ReviseAsync(editor, saved.Id, 1, snapshot, Frozen.Replace("\"z\":1", "\"z\":3"), Markdown + "more\n", CancellationToken.None);
        var read = await store.GetAsync(saved.Id, CancellationToken.None);
        Assert.Equal(2, read!.Version);
        Assert.Equal([1, 2], read.Revisions.Select(r => r.Version));
        Assert.Equal(Frozen, read.Revisions[0].Frozen.ToJsonString());
        Assert.Equal(editor, read.Revisions[1].Editor);
        var stale = await Assert.ThrowsAsync<WorkspaceException>(() => store.ReviseAsync(editor, saved.Id, 1, snapshot, Frozen, Markdown, CancellationToken.None));
        Assert.Equal("versionConflict", stale.Code);
    }

    [Fact]
    public async Task After_a_new_snapshot_it_is_readable_unchanged_and_edits_are_refused()   // B33, B34
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var newer = await scenario.ActivateAsync("changed");
        var read = await store.GetAsync(saved.Id, CancellationToken.None);
        Assert.True(read!.PinnedToEarlierSnapshot);
        Assert.Equal(Frozen, read.Frozen.ToJsonString());
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => store.ReviseAsync(Manager, saved.Id, 1, newer, Frozen, Markdown, CancellationToken.None));
        Assert.Equal("snapshotMismatch", error.Code);
    }

    [Fact]
    public async Task Approval_fields_are_refused_by_the_store()                              // B31
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => store.SaveAsync(Manager, snapshot, "CR-9",
            "{\"format\":1,\"record\":{\"approvalStatus\":\"approved\"}}", Markdown, null, CancellationToken.None));
        Assert.Equal("approvalNotSupported", error.Code);
    }

    [Fact]
    public async Task A_rebuild_copies_assessments_byte_for_byte_and_history_ignores_them()   // Q5
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var replay = new ReplayStore(scenario.Db, scenario.Bundles);
        var live = await replay.DescribeCurrentAsync(CancellationToken.None);
        var rebuilt = await replay.RebuildAsync(saved.Position, CancellationToken.None);
        Assert.Equal(live.StateDigest, rebuilt.StateDigest);
        var history = await new ContributionStore(scenario.Db).HistoryAsync(null, new PageRequest(null, 50), CancellationToken.None);
        Assert.Empty(history.Entries);
    }
}
```

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement 29.1 and 29.2. Add `snapshotMismatch` and `approvalNotSupported` to the error catalogue: `snapshotMismatch` is 409, and `approvalNotSupported` and `invalidAssessment` stay at the default of 422.
- [ ] Commit `feat: save and revise assessments in the journal (#198)` and push.

---

## Task 30: Assessment API (B17, B18, B19, B20, B22, B31, B32, B33, B36, Q2)

**Files:**
- create `runtime/RuntimeKnowledge.Api/AssessmentRoutes.cs`;
- modify `Program.cs` (register `AssessmentStore` and map the routes), `WorkspaceOpenApi.cs`, `OpenApiExamples.cs` and `WorkspaceLinks.cs`, and regenerate the spec;
- create `runtime/RuntimeKnowledge.Tests/AssessmentApiTests.cs`.

### 30.1 Routes

| Route | Policy | Handler |
| --- | --- | --- |
| `POST /api/assessments/preview` | read | `Frozen = Freeze(model, request)`. Returns `AssessmentPreview`, with the Markdown from `AssessmentMarkdown.Render(frozen)`. Stores nothing. |
| `POST /api/assessments` | manage | The same freeze and render, then `SaveAsync(actor, request.SnapshotId, record.requestId, EcmaJson.Stringify(frozen), markdown, startedFrom from record, ...)`. Returns `201 Created` with `Location: /api/assessments/{id}`. |
| `GET /api/assessments/{id:guid}` | read | `SavedAssessment`, or `assessmentNotFound`. |
| `GET /api/assessments/{id:guid}/markdown` | read | The stored bytes, with `text/markdown; charset=utf-8`. |
| `PUT /api/assessments/{id:guid}` | manage | Freeze and render against the active model, then `ReviseAsync(..., request.ExpectedVersion, request.SnapshotId, ...)`. |

`Freeze(model, request)` does the following:
1. Get the model through `ImpactModelCache.GetAsync(request.SnapshotId)`. This gives `snapshotChanged` on a mismatch (B36).
2. Compute `paths = Paths.Read(model, confirmedLeads)["paths"]`, or `[]` when there are no confirmed leads, and `views = request.Views ?? []`.
3. Call `Assessments.Validate`, then `Freezing.Freeze`.
4. Map an `ImpactException` with the message `approval is not supported` to `approvalNotSupported`, and any other to `invalidAssessment`, keeping the message.
5. Require `record.schemaVersion == 2`. A schema 1 record is `invalidAssessment`, with the message `The workspace accepts schema 2 records only`.

The record's `assessor` is required by the renderer, and the pages fill it in (Task 31).

### 30.2 `AssessmentApiTests.cs`

Use I4's `OpenAsync("confluence")` pattern. Build the draft record from `test/impact-corpus/confluence/inputs/007/assessment-input.json`, which is schema 2 with `assessor: 'Analyst A'`. Its pins equal the model's pins, because the corpus bundle's `impactInput` is unchanged.

```csharp
[Fact] public async Task Preview_validates_returns_markdown_and_stores_nothing()                 // B17
[Fact] public async Task Preview_refuses_an_impact_not_in_a_cited_path()                         // B17: 422 invalidAssessment, message "impact is not present in a cited path"
[Fact] public async Task A_manager_saves_and_another_reader_opens_the_same_content()             // B18, B20, B32: GET by a second reader token (subject "reader-2") returns Frozen and Markdown equal to the preview; Frozen.pins present
[Fact] public async Task A_reader_cannot_save_and_gets_403()                                     // B19
[Fact] public async Task Approval_fields_are_refused_at_the_api()                                // B31: record["approved"]=true gives 422 approvalNotSupported
[Fact] public async Task Draft_actions_on_another_snapshot_are_snapshot_changed()                // B36: preview and save with snapshotId 0x00.. give 409 snapshotChanged
[Fact] public async Task A_manager_edits_on_the_same_snapshot_and_history_shows_who_changed_it() // B22
[Fact] public async Task Edits_after_a_new_snapshot_are_snapshot_mismatch()                      // B33: activate "changed" through the scenario, then PUT gives 409 snapshotMismatch
[Theory, InlineData("POST", "api/assessments/preview"), InlineData("POST", "api/assessments"),
 InlineData("GET", "api/assessments/00000000-0000-0000-0000-000000000001"),
 InlineData("PUT", "api/assessments/00000000-0000-0000-0000-000000000001")]
public async Task Every_assessment_route_refuses_unauthenticated_calls(string method, string route) // Q2
[Theory, InlineData("POST", "api/assessments"), InlineData("PUT", "api/assessments/00000000-0000-0000-0000-000000000001")]
public async Task Save_and_edit_refuse_readers_without_manage(string method, string route)          // Q2: 403
```

Write each body fully, in the style of `ImpactApiTests`. The comment on each line is its specification. For B33, the activation happens after the save: expose `ApiFixture.Scenario` (internal), and call `api.Scenario.ActivateAsync("changed")`.

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement, and regenerate the spec. Run the OpenApi and database categories.
- [ ] Commit `feat: serve assessment preview, save, read and edit (#198)` and push.

---

## Task 31: Draft, saved and edit pages (B17, B19, B20, B22, B31, B32, B34, B36, Q6)

**Files:**
- create `runtime/RuntimeKnowledge.Web/Services/AssessmentDraft.cs` (registered `AddSingleton` in the WebAssembly host, so the draft lives in memory for the tab);
- create `Pages/AssessmentDraftPage.razor` (`/assessments/draft`), `Pages/Assessment.razor` (`/assessments/{Id:guid}`) and `Components/AssessmentDraftEditor.razor`;
- modify `Pages/Impact.razor` (a **Start assessment** button) and `WorkspaceClient.cs` (`PreviewAssessment`, `SaveAssessment`, `GetAssessment`, `AssessmentMarkdown` and `EditAssessment`);
- create `runtime/RuntimeKnowledge.Tests/AssessmentUiTests.cs`.

### 31.1 `AssessmentDraft`

```csharp
public sealed class AssessmentDraft
{
    public string? SnapshotId { get; private set; }
    public JsonObject? Record { get; private set; }                 // schema 2, built by Start
    public IReadOnlyList<ImpactPath> Paths { get; private set; } = [];
    public Guid? EditingId { get; private set; }
    public int? ExpectedVersion { get; private set; }
    public event Action? Changed;

    public void Start(string snapshotId, string requestId, string description, string assessor, JsonObject pins, Guid? startedFrom = null);
    public void LoadForEdit(SavedAssessment saved);                // Record = saved.Frozen["record"], ExpectedVersion = saved.Version
    public void UsePaths(IReadOnlyList<ImpactPath> paths);
    public void Confirm(ImpactKey key);
    public void Reject(ImpactKey key, string reason);               // reason must have non-whitespace text, or ArgumentException
    public void AddImpact(ImpactKey key, string reason, IReadOnlyList<string> pathIds, IReadOnlyList<string> evidenceIris);
    public void AddGap(string code, string reason);
    public void Discard();                                          // clears every field, raises Changed
}
```

`Start` needs the pins, so the API exposes them without saving anything. The record is built **in the browser**, with `pins` taken from the search result:

```text
pins = { snapshotId: result.Pinned, masterList: result.MasterList (as the engine writes it), registry: from result.RegistryCommit and digests }
```

To get the registry digests, which the search result does not carry, add `RegistryDigests? Registry` to `ImpactSearchResult`, where `RegistryDigests` is `(string Commit, string Mappings, string Bindings)`, null when the bundle has no registry. Regenerate the spec. This is an additive field.

### 31.2 Pages

- **`Impact.razor` — Start assessment.** This button is enabled after a search. It asks for a request id in a labelled input. The description is the search text, and the assessor is the signed-in user's display name from the token's `name` claim, or the subject. It calls `Draft.Start`, then navigates to `/assessments/draft`. **Trace impact** results are passed to `Draft.UsePaths`.
- **`AssessmentDraftPage.razor`** hosts `AssessmentDraftEditor`:
  - **Leads.** Confirm or Reject buttons for each traced start. Reject opens a labelled reason input, and the reason is required (B17).
  - **Likely impacts.** Choose a node that appears in a traced path, then tick the paths and evidence to cite. Only evidence IRIs from the chosen paths are offered (B17).
  - **Unresolved gaps.** Add a code and a reason.
  - **Preview** calls `PreviewAssessment`. It shows the Markdown as text in `<pre>`, never rendered as HTML (Q4), and the gap sections (B30).
  - **Download Markdown** builds a `data:text/markdown;charset=utf-8;base64,` link from the preview's Markdown, with `download="{requestId}.md"`. It works for readers (B19).
  - **Save** is shown only when `Session().CanManage`. A 403 shows `Only workspace managers can save. Your draft is kept and can still be exported.` and keeps the draft (B19).
  - **`snapshotChanged`** from any call runs `Draft.Discard()`, and shows `The workspace snapshot changed, so this draft can no longer be saved. Exports you downloaded are unaffected.` (B36).
  - **No approve control.** There is no approve, sign-off or status control anywhere (B31).
- **`Assessment.razor`** loads `GetAssessment(id)` and shows:
  - the request, the description, the author and the time saved;
  - the **pins**: catalog commit, each source revision, the master list, and the registry commit (B32);
  - leads, rejected leads, impacts, the cited paths step by step (reusing `ImpactPathList`), the evidence (`EvidenceCitation`, with lines from `frozen.evidence`) and every gap section (B20, B30);
  - a banner `Pinned to an earlier snapshot` when `PinnedToEarlierSnapshot` is set (B34);
  - **Download Markdown**, using the `/markdown` route;
  - **Edit**, shown only to managers and only when the assessment is not pinned to an earlier snapshot (B22, B33). It runs `Draft.LoadForEdit` and opens the editor, which then saves through `EditAssessment`;
  - **History**, listing the revisions with editor and time. **Show** displays a revision's Markdown as text.

### 31.3 `AssessmentUiTests.cs` (bUnit, with the `WorkspaceUiTests` fake handler)

1. `The_draft_lives_in_memory_and_requests_carry_its_snapshot`: the preview request body has `snapshotId`.
2. `Reject_needs_a_reason_and_impacts_offer_only_evidence_from_chosen_paths`.
3. `Preview_shows_markdown_as_text`: `<script>` appears as text, and there is no `script` element.
4. `Save_is_absent_for_readers_and_a_403_keeps_the_draft_and_export`.
5. `Snapshot_changed_discards_the_draft_with_the_message`.
6. `The_saved_view_shows_pins_banner_history_and_no_approve_control`: there is no button whose name matches `/approve|sign[- ]?off/i`.
7. `Edit_is_hidden_when_pinned_to_an_earlier_snapshot`.
8. `Controls_are_native_and_labelled`.

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement, and regenerate the spec for the additive `Registry` field.
- [ ] Commit `feat: add workspace assessment drafting and saved views (#198)` and push.

---

## Task 32: Byte identity and durability (B23, B30, Q3, Q5)

**Files:** create `runtime/RuntimeKnowledge.Tests/AssessmentIdentityTests.cs` (category `Database`).

1. **`Workspace_export_equals_the_command_line_render_byte_for_byte` (B23, Q3).**
   - Run `CommandRunner.Run(["assessment-render", "--bundle", CorpusPaths.Objects, "--manifest", <confluence step 7 manifest>, "--staging", tmp, "--input", <inputs/007/assessment-input.json>])`. Read `assessment.md`.
   - Save the same record through the API, on the `AddCorpusBundle("confluence")` snapshot, and `GET /markdown`.
   - Assert that the bytes are equal. Also assert that a preview of the same record twice returns identical `Frozen` JSON and Markdown.
2. **`A_saved_assessment_opens_identically_after_restart_activation_and_rebuild` (Q5).** Save, and capture the `GET` body and the Markdown bytes. Then:
   - dispose the `ApiFixture`'s web factory only, start a new factory on the same database, and `GET` again;
   - activate `"initial"` (a new snapshot), and `GET` again;
   - pause writes, rebuild, cut over, resume, and `GET` again.

   Each time, `Frozen.ToJsonString()` and the Markdown bytes must equal the first capture. Expose the factory restart as `ApiFixture.RestartAsync()`.

- [ ] Write the tests, and confirm that they fail or pass. They should pass if Tasks 29 and 30 are right: in that case they pin the behaviour, and the commit says so.
- [ ] Commit `test: prove workspace and command-line Markdown identity and durability (B23, Q5)` and push.

---

## Task 33: End-to-end workspace scenario and accessibility (Q6)

**Files:** modify `runtime/RuntimeKnowledge.Tests/WorkspaceScenarioTests.cs` and `ScenarioRunner.cs`, and `deploy/workspace/verify-impact-browser.mjs`.

- **`WorkspaceScenarioTests`.** Add `An_analyst_assesses_in_the_workspace_and_another_reader_opens_it`. A manager token searches, traces, previews, saves, and downloads the Markdown. A second reader token opens the saved assessment and gets equal content. The reader's own save gets 403.
- **`verify-impact-browser.mjs`.** Extend it with `verifyAssessmentPages({ manager, reader, origin })`. The manager runs the keyboard flow for search, trace, start, confirm, preview and save. The reader opens the saved link and sees no Edit button. Axe must report no serious or critical violations on the draft page and the saved page. Call it from `verify-browser.mjs`.

Steps:
- [ ] Write both, run them against the Mac compose environment, and record the results.
- [ ] Commit `test: cover the workspace assessment flow end to end (Q6)` and push.

## I5 close

- [ ] CI is green, and the database and browser checks pass.
- [ ] Post the handoff. It says I5 is done, lists B17, B18, B19, B20, B22, B23, B30, B31, B32, B33, B34 and B36, and gives I6 as next. **Stop.**
