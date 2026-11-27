# I2. Evidenced graphs and documents: detailed implementation plan

**Inputs:**
- [Behaviours](../../features/generalise-impact-assessment/02-behaviours.md): B1, B2, B3, B5, B25, B26, B27 and B38 are delivered here, together with the command-line parts of B30 and B32.
- ADRs [0030](../../adr/0030-hold-evidence-excerpts-in-a-per-snapshot-impact-model.md) and [0031](../../adr/0031-read-page-metadata-from-yaml-front-matter.md).
- [Task overview](../../superpowers/plans/generalise-impact-assessment/plan.md).
- [I1 plan](i1-c4-parity.md), whose conventions all apply here.

**Start:** I1 is done, its handoff is posted, and the branch `feature/generalise-impact-assessment/dev-build` is up to date. From now on, `npm run c4` and the baseline capture run on the .NET engine. Re-capturing the baseline therefore records the engine's own outputs. Every capture diff must be reviewed against the task's tests and B37's list before it is committed.

**Baseline update procedure** (used by Tasks 13, 14, 15 and 17):
1. Commit the engine change and its tests.
2. Run `node test/impact-corpus/capture.mjs`, then `node test/impact-corpus/export-bundles.mjs`.
3. Read `git diff --stat test/impact-corpus` and the full diff. Every change must be an instance of the B37 item the task names. Anything else is a defect: fix the engine rather than committing it.
4. Commit only `test/impact-corpus`, as `test: baseline <change> (B37 item N)`, and push.

---

## Task 12: Confluence-shaped fixture

**Files:**
- create `test/fixtures/impact/confluence-repos.mjs`;
- add scenarios `confluence` to `test/impact-corpus/scenarios.mjs`;
- add `'confluence'` to the `pending` set in `test/impact-corpus.test.mjs`.

### 12.1 Fixture content

`withConfluenceFixture(run)` follows `withPilotFixture`: a temporary root, Git repositories with pinned identities (the caller pins them), and a `finally` that removes the root. It creates two sources, `fixture/policies` and `fixture/operations`, at `https://github.com/fixture/policies.git` and `https://github.com/fixture/operations.git`.

`fixture/policies` contains:

| Path | Content (exact) |
| --- | --- |
| `pages/expense-policy.md` | `---\ntitle: Expense policy\nconfluence_url: https://confluence.example.test/pages/1001\n---\n# Expense policy\nClaims over 500 need a manager's approval.\nThe approval process records every claim.\n` |
| `pages/approval-process.md` | `---\ntitle: Approval process\n---\n# Approval process\nManagers approve claims in the finance system.\n` |
| `pages/legacy-notes.md` | `# Legacy notes\nThe old ledger still exports claims nightly.\n` |
| `pages/injected.md` | `---\ntitle: "<script>alert(1)</script> | # Heading"\nconfluence_url: https://confluence.example.test/pages/1004\n---\nLine with <b>markup</b> and | pipes\n# Not a heading\n` |
| `ontology.config.json` | `{"sourceGraph":{"enabled":true,"root":"graph","evidence":{"selected-document":["pages/**"]}}}` |
| `graph/manifest.ttl` | written with `n3`'s `Writer`, as in `fixture-repos.mjs`, at schema version 2 (see below) |

Graph (prefix `P = https://example.test/policies/graph#`, `K` = kit schema):
- **Nodes** (`kit:GraphNode`, each with a `kit:kind` from `https://example.test/kinds#` and an `rdfs:label`):

  | Node | Label | Kind |
  | --- | --- | --- |
  | `P:expense-policy` | `Expense policy` | `policy` |
  | `P:approval` | `Claim approval` | `process` |
  | `P:ledger` | `Legacy ledger` | `system` |
  | `P:injected` | `Injected page` | `policy` |
  | `P:orphan` | `Expense archive` | `record` |

  `P:orphan` has no assertions and no evidence.
- **Evidence** (`kit:role-selected-document`):

  | Evidence | Page | Lines |
  | --- | --- | --- |
  | `P:ev-policy` | `pages/expense-policy.md` | 6–7 |
  | `P:ev-approval` | `pages/approval-process.md` | 5 |
  | `P:ev-legacy` | `pages/legacy-notes.md` | 2 |
  | `P:ev-injected` | `pages/injected.md` | 5–6 |
- **Assertions** (basis `kit:direct`):

  | Subject | Relationship | Object | Evidence |
  | --- | --- | --- | --- |
  | `P:expense-policy` | `https://example.test/rel#governs` | `P:approval` | `P:ev-policy` |
  | `P:approval` | `https://example.test/rel#uses` | `P:ledger` | `P:ev-approval` |
  | `P:ledger` | `https://example.test/rel#exports` | `P:approval` | `P:ev-legacy` |
  | `P:injected` | `https://example.test/rel#governs` | `P:approval` | `P:ev-injected` |
- **Coverage:** `pages/**`, priority 1, `kit:covered`.

`fixture/operations` has one page, `pages/runbook.md` (`---\ntitle: Finance runbook\nconfluence_url: https://confluence.example.test/pages/2001\n---\nThe finance team runs the claims job.\n`). Its graph has the node `O:claims-job` (`Claims job`, kind `process`), the node `O:team` (`Finance team`, kind `team`), and the assertion `O:team` `https://example.test/rel#runs` `O:claims-job` with evidence on line 5.

The division has:
- `sources.json` with both sources;
- `dist/catalog.json` = `{"schemaVersion":1,"sources":[...pins],"concepts":[],"relations":[],"links":[]}`;
- `c4/mappings.json` = `{"schemaVersion":1,"mappings":[]}`;
- `c4/bindings.json` = `{"schemaVersion":1,"bindings":[]}`.

`run` receives `{divisionRoot, catalogCommit, registryCommit: catalogCommit, checkouts, outputDir, keys, ...}`. `keys` holds `policy`, `approval`, `ledger`, `injected`, `orphan`, `job` and `team`, as `{sourceId, nodeIri}`.

### 12.2 Scenario `confluence`

```js
async confluence(run) {
  await withConfluenceFixture(async f => {
    const c = [...common(f), '--output', f.outputDir], o = { cwd: f.divisionRoot };
    const temp = name => join(f.divisionRoot, '..', name);
    await run(['search', ...c, '--query', 'expense'], o);                       // 0: B1, B2
    await run(['search', ...c, '--query', 'approval'], o);                      // 1
    await run(['paths', ...c, '--start', at(f.keys.policy)], o);                // 2: B3, B25
    const traced = JSON.parse(readFileSync(join(f.outputDir, 'paths.json'), 'utf8'));
    await run(['paths', ...c, '--start', at(f.keys.orphan)], o);                // 3: B3 refused start
    await run(['paths', ...c, '--start', at(f.keys.injected)], o);              // 4: Q4 data
    await run(['view', ...c, '--level', 'context', '--focus', at(f.keys.policy)], o);   // 5: B5
    const description = temp('description.txt');
    writeFileSync(description, 'Raise the expense approval threshold\n');
    await run(['assessment-init', ...c, '--request-id', 'CR-9', '--description-file', description], o);  // 6
    const record = JSON.parse(readFileSync(join(f.outputDir, 'assessment.json'), 'utf8'));
    const path = traced.paths.find(p => p.steps.length === 2);
    Object.assign(record, { assessor: 'Analyst A', confirmedLeads: [f.keys.policy],
      likelyImpacts: [{ key: f.keys.ledger, reason: 'Ledger supports approvals', pathIds: [path.id],
        evidenceIris: path.steps.flatMap(s => s.evidenceIris) }],
      unresolved: [{ code: 'threshold-owner', reason: 'Who owns the threshold?' }] });
    const input = temp('assessment-input.json');
    writeFileSync(input, JSON.stringify(record));
    await run(['assessment-render', ...c, '--input', input], o);                // 7: B25, B30, B32
  });
}
```

Steps:
- [ ] Write the fixture and scenario, and add `'confluence'` to `pending`.
- [ ] Run `node test/impact-corpus/capture.mjs confluence`. It records today's engine behaviour: leads unsupported, steps 2 and 4 refused, the view `unsupported-focus`, and the render failing because there is no `architect`. Commit it, so later diffs show each behaviour arriving.
- [ ] Run `node test/impact-corpus/export-bundles.mjs`.
- [ ] Commit `test: add the Confluence-shaped impact fixture (#198)` and push.

---

## Task 13: Evidence makes a lead supported (B1, B2)

**Files:** modify `Engine/Candidates.cs` and `Model/ImpactModel.cs`, adding `SourceModel.CitingRoles(string nodeIri)`. Create `Tests/Impact/SupportTests.cs`.

**Rule (B1, U18):** a node's citing roles are:
- the roles of its own `evidenceIris`, as on design records;
- plus the roles of every evidence record on every assertion where the node is subject or object;
- de-duplicated and sorted ordinally.

A graph lead is supported by its mapping first: if `MappingFor` is not null, it is supported with the reason `Reviewed mapping`. Otherwise, if it has citing roles, it is supported with the reason `Verified evidence: ` followed by the roles joined with `, `. Otherwise it is unsupported with the reason `No verified evidence or supported mapping` (B2). Concept leads are unchanged: unsupported, with `Catalog concept is not a supported graph node`.

### `SupportTests.cs`

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class SupportTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "search")));
    private static JsonObject Lead(JsonObject result, string iri) =>
        result["leads"]!.AsArray().Select(l => l!.AsObject()).Single(l => l["key"]!["nodeIri"]!.GetValue<string>() == iri);

    [Fact]
    public void Evidence_through_an_assertion_makes_an_unmapped_lead_supported()         // B1
    {
        var lead = Lead(Candidates.Find(Confluence(), "expense"), P + "expense-policy");
        Assert.True(lead["supported"]!.GetValue<bool>());
        Assert.Equal("Verified evidence: selected-document", lead["reason"]!.GetValue<string>());
        Assert.Equal("fixture/policies", lead["key"]!["sourceId"]!.GetValue<string>());
        Assert.Equal("phrase", lead["matchKind"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{40}$", lead["sourceCommit"]!.GetValue<string>());
    }

    [Fact]
    public void A_node_without_evidence_or_mapping_stays_unsupported()                   // B2
    {
        var lead = Lead(Candidates.Find(Confluence(), "expense"), P + "orphan");
        Assert.False(lead["supported"]!.GetValue<bool>());
        Assert.Equal("No verified evidence or supported mapping", lead["reason"]!.GetValue<string>());
    }

    [Fact]
    public void A_mapped_lead_keeps_the_reviewed_mapping_reason()                       // B37
    {
        var lead = Lead(Candidates.Find(TestModels.MakeModel(), "Alpha API"), "https://alpha.example/#system");
        Assert.Equal("Reviewed mapping", lead["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Roles_are_named_once_in_sorted_order()
    {
        var model = TestModels.MakeModel(input =>
        {
            var graph = input["sources"]![0]!["graph"]!;
            graph["evidence"]!.AsArray().Add(new JsonObject { ["iri"] = "t", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1, ["blobId"] = new string('1', 40), ["role"] = "test" });
            graph["assertions"]!.AsArray().Add(new JsonObject { ["iri"] = "x", ["subject"] = "https://alpha.example/#code", ["object"] = "https://alpha.example/#external",
                ["relationship"] = "calls", ["basis"] = "direct", ["evidenceIris"] = new JsonArray("t", "https://alpha.example/#evidence") });
            input["registry"]!["mappings"] = new JsonArray();
        });
        Assert.Equal("Verified evidence: implementation, test", Lead(Candidates.Find(model, "handle"), "https://alpha.example/#code")["reason"]!.GetValue<string>());
    }
}
```

Steps:
- [ ] Write the test and run `--filter Support`. It fails: the leads are unsupported.
- [ ] Implement.
- [ ] Commit `feat: support leads that verified evidence cites (B1, B2)`.
- [ ] Follow the baseline update procedure for **B37 item 1**. Expect `supported` and `reason` changes on unmapped graph leads only, across the pilot, walkthrough and Confluence scenarios. Push.

---

## Task 14: Paths start from any supported lead (B3)

**Files:** modify `Engine/Paths.cs`, adding `ImpactModel.IsSupported(sourceId, nodeIri)` shared with `Candidates`. Create `Tests/Impact/StartsTests.cs`.

A start is accepted when `IsSupported` is true. Otherwise the gap is `{code:'unsupported-start', sourceId, nodeIri, reason:'Start lacks verified evidence or a supported mapping'}`, with keys in that order, as today's spread of `...start`.

### `StartsTests.cs`

```csharp
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class StartsTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "paths")));

    [Fact]
    public void A_supported_unmapped_start_produces_paths_without_an_unsupported_start_gap()
    {
        var r = Paths.Read(Confluence(), [("fixture/policies", P + "expense-policy")]);
        Assert.NotEmpty(r["paths"]!.AsArray());
        Assert.DoesNotContain(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-start");
        Assert.Equal(P + "approval", r["paths"]![0]!["steps"]![0]!["object"]!["nodeIri"]!.GetValue<string>());
    }

    [Fact]
    public void An_unsupported_start_is_skipped_with_the_new_reason()
    {
        var r = Paths.Read(Confluence(), [("fixture/policies", P + "orphan")]);
        var gap = Assert.Single(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-start");
        Assert.Equal("Start lacks verified evidence or a supported mapping", gap!["reason"]!.GetValue<string>());
        Assert.Equal(["code", "sourceId", "nodeIri", "reason"], gap.AsObject().Select(p => p.Key));
    }
}
```

Steps:
- [ ] Write the test, and confirm that it fails.
- [ ] Implement.
- [ ] Commit `feat: start impact paths from any supported lead (B3)`.
- [ ] Follow the baseline update procedure for **B37 items 2 and 3**. Previously refused unmapped starts now have paths, and every refused start shows the new reason. Push.

---

## Task 15: C4 views are optional (B5)

**Files:** modify `Engine/Views.cs`, and add `ViewsTests` cases.

When the focus has no mapping at all, the gap is `{code:'no-c4-mapping', sourceId, nodeIri, reason:'Node has no C4 mapping; C4 views need a reviewed mapping'}`, and the view returns as it does today for `unsupported-focus`. A focus that is mapped at the wrong level keeps today's `unsupported-focus` gap and reason, unchanged.

```csharp
[Fact]
public void An_unmapped_focus_is_refused_with_no_c4_mapping()          // B5
{
    var v = Views.Project(TestModels.MakeModel(), ("fixture/Beta", "https://beta.example/#system"), "container");
    var gap = Assert.Single(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "no-c4-mapping");
    Assert.Equal("https://beta.example/#system", gap!["nodeIri"]!.GetValue<string>());
    Assert.DoesNotContain(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-focus");
    Assert.Empty(v["items"]!.AsArray());
}

[Fact]
public void A_focus_mapped_at_another_level_keeps_unsupported_focus()
{
    var v = Views.Project(TestModels.MakeModel(), ("fixture/Alpha", "https://alpha.example/#container"), "container");
    Assert.Contains(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unsupported-focus");
    Assert.DoesNotContain(v["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "no-c4-mapping");
}
```

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement.
- [ ] Commit `feat: refuse C4 views on unmapped nodes with no-c4-mapping (B5)`.
- [ ] Follow the baseline update procedure for **B37 item 6**. Only the pilot's step 010 and the Confluence step 5 change. Push.

---

## Task 16: Evidence excerpts and page metadata (B25, B26, B27, B38)

**Files:** create `Model/EvidenceText.cs`, `Model/FrontMatter.cs` and `Tests/Impact/EvidenceTextTests.cs`. Modify `Engine/Paths.cs` and `Engine/Candidates.cs`, which attach page evidence.

### 16.1 Interfaces

```csharp
namespace RuntimeKnowledge.Impact.Model;
public sealed record PageMetadata(string? Title, string? ConfluenceUrl);
public sealed record CitedText(string SourceId, string EvidenceIri, string Path, int StartLine, int EndLine,
    string Role, IReadOnlyList<string>? Lines, PageMetadata? Page, JsonObject? Gap);
public static class EvidenceText
{
    /// <summary>Reads the cited lines of one evidence record from the bundle, strictly as UTF-8.</summary>
    public static CitedText Read(ImpactModel model, string sourceId, JsonObject evidence);
}
public static class FrontMatter
{
    /// <summary>null when the file is not an exported page: it does not open with a front-matter block.</summary>
    public static PageMetadata? Parse(string text);
}
```

**Rules:**
- **Reading.** Read the bytes with `model.Bundle.Read("graphEvidence", sourceId, path)`. Decode with `new UTF8Encoding(false, throwOnInvalidBytes: true)`. Split lines on `\n`, removing one trailing `\r` from each line. `Lines` is lines `[startLine-1 .. endLine-1]`. Cache the decoded file per `(sourceId, path)`.
- **Unreadable text (B27).** If the bytes are `null`, cannot be decoded, or the range is out of bounds, then `Lines` is `null`. `Gap` is then `{code:'evidence-text-unavailable', sourceId, evidenceIri, path, reason:'Cited text could not be read at the pinned commit'}`. No other text is used.
- **Exported page (U17, ADR 0031 as amended).** Only `role == "selected-document"` files are considered. Line 1 must be exactly `---`, and a later line must be exactly `---`. Between them, read lines of the form `key: value`, where `key` matches `^[A-Za-z_][A-Za-z0-9_]*$`. Strip one pair of surrounding `"` or `'`.
  - A value that starts with `&`, `*`, `!`, `|`, `>`, `[` or `{` is treated as absent.
  - `title` must be non-empty after trimming.
  - `confluence_url` must satisfy `Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https"`.
  - Any other key is ignored.
- **Metadata gap (B26).** For an exported page, if `Title` or `ConfluenceUrl` is null, `Gap` is `{code:'page-metadata-missing', sourceId, path, reason}`. The reason is `Exported page has no title`, `Exported page has no Confluence address`, or `Exported page has no title or Confluence address`.
- **Not a page (B38).** When `Page` is null and the role is not `selected-document`, or the file is an ordinary document, no metadata gap is raised.

### 16.2 Where page evidence appears in engine outputs

Path ids must stay stable for code and ordinary-document evidence, so nothing changes for them.

- **Path steps.** For an assertion step whose evidence is an exported page, the cloned evidence object gains a final key `page: {title?, confluenceUrl?, lines}`. The title and URL appear only when present. `lines` is the cited lines, or the key is absent when the text is unavailable. The path id is computed after this is added, so it covers the page text.
- **Gaps from paths.** These are de-duplicated by `EcmaJson.Stringify(gap)`, sorted ordinally, and placed after the `no-relationship-master-list` gap.
- **Leads.** A supported graph lead whose citing evidence includes exported pages gains a final key `pageEvidence: [{evidenceIri, path, startLine, endLine, title?, confluenceUrl?, lines?}]`, sorted by `evidenceIri`. Its gaps join the search `gaps`, de-duplicated the same way. Leads with no page evidence have no `pageEvidence` key.

### 16.3 `EvidenceTextTests.cs`

```csharp
using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class EvidenceTextTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "paths")));
    private static JsonObject Evidence(ImpactModel m, string iri) => m.Source("fixture/policies")!.Evidence(P + iri)!;

    [Fact]
    public void An_exported_page_shows_title_address_and_cited_lines()                  // B25
    {
        var m = Confluence();
        var text = EvidenceText.Read(m, "fixture/policies", Evidence(m, "ev-policy"));
        Assert.Equal(new PageMetadata("Expense policy", "https://confluence.example.test/pages/1001"), text.Page);
        Assert.Equal(["Claims over 500 need a manager's approval.", "The approval process records every claim."], text.Lines);
        Assert.Null(text.Gap);
    }

    [Fact]
    public void A_page_without_an_address_reports_page_metadata_missing()               // B26
    {
        var m = Confluence();
        var text = EvidenceText.Read(m, "fixture/policies", Evidence(m, "ev-approval"));
        Assert.Equal("Approval process", text.Page!.Title);
        Assert.Null(text.Page.ConfluenceUrl);
        Assert.Equal("page-metadata-missing", text.Gap!["code"]!.GetValue<string>());
        Assert.Equal("Exported page has no Confluence address", text.Gap["reason"]!.GetValue<string>());
        Assert.Equal(["Managers approve claims in the finance system."], text.Lines);
    }

    [Fact]
    public void An_ordinary_document_has_no_page_and_no_gap()                             // B38
    {
        var m = Confluence();
        var text = EvidenceText.Read(m, "fixture/policies", Evidence(m, "ev-legacy"));
        Assert.Null(text.Page);
        Assert.Null(text.Gap);
        Assert.Equal(["The old ledger still exports claims nightly."], text.Lines);
    }

    [Theory]
    [InlineData("---\ntitle: T\nconfluence_url: http://insecure.test/x\n---\n", "Exported page has no Confluence address")]
    [InlineData("---\nconfluence_url: https://c.test/x\n---\n", "Exported page has no title")]
    [InlineData("---\ntitle: &anchor\n---\n", "Exported page has no title or Confluence address")]
    public void Invalid_or_missing_keys_are_reported(string page, string reason)
    {
        var parsed = FrontMatter.Parse(page)!;
        Assert.Equal(reason, (parsed.Title, parsed.ConfluenceUrl) switch
        {
            (null, null) => "Exported page has no title or Confluence address",
            (null, _) => "Exported page has no title",
            (_, null) => "Exported page has no Confluence address",
            _ => "ok"
        });
    }

    [Fact]
    public void A_file_without_a_closing_marker_is_not_a_page() =>
        Assert.Null(FrontMatter.Parse("---\ntitle: never closed\n"));

    [Fact]
    public void Missing_or_undecodable_text_is_a_gap_and_never_replaced()                // B27
    {
        var model = TestModels.MakeModel(blobOverride: _ => new byte[] { 0xff, 0xfe, 0x0a });
        var evidence = model.Source("fixture/Alpha")!.Evidence("https://alpha.example/#evidence")!;
        var text = EvidenceText.Read(model, "fixture/Alpha", evidence);
        Assert.Null(text.Lines);
        Assert.Equal("evidence-text-unavailable", text.Gap!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Page_text_is_attached_to_steps_and_code_steps_keep_their_ids()
    {
        var r = Paths.Read(Confluence(), [("fixture/policies", P + "expense-policy")]);
        var first = r["paths"]![0]!["steps"]![0]!["evidence"]![0]!.AsObject();
        Assert.Equal("Expense policy", first["page"]!["title"]!.GetValue<string>());
        Assert.Contains(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "page-metadata-missing");
        var pilot = Paths.Read(TestModels.MakeModel(), [("fixture/Alpha", "https://alpha.example/#system")]);
        Assert.DoesNotContain(pilot["paths"]!.AsArray().SelectMany(p => p!["steps"]!.AsArray()),
            s => s!["evidence"] is JsonArray e && e.Any(x => x!.AsObject().ContainsKey("page")));
    }
}
```

`TestModels.MakeModel(change, blobOverride)` gains an optional `Func<string, byte[]>? blobOverride` that replaces the evidence blob bytes. The blob's manifest digest is computed from those bytes, so the bundle still verifies.

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement.
- [ ] Run the conformance tests. The pilot and walkthrough must still match: neither has exported pages.
- [ ] Re-capture the `confluence` scenario only (`node test/impact-corpus/capture.mjs confluence`). Its steps 0, 2 and 4 now carry pages and gaps.
- [ ] Commit `feat: read cited text and page metadata (B25, B26, B27, B38)` and push.

---

## Task 17: Schema 2 records, freezing and the assessment renderer

**Files:**
- create `Engine/Freezing.cs`, `Render/AssessmentMarkdown.cs` and `Tests/Impact/FreezingTests.cs`;
- modify `Engine/Assessments.cs` and `Engine/CommandRunner.cs`;
- modify `test/acceptance/c4-walkthrough.mjs` (`assessor`), and `test/acceptance/c4.test.mjs`, line 91 (`Sample architect` is still printed in feedback, so it stays);
- remove `'confluence'` from `pending`.

### 17.1 Schema 2 record (written by `assessment-init`)

```text
{ schemaVersion: 2, requestId, description, assessor: '',
  pins: { snapshotId,                                   // model.SnapshotId
          masterList: { status: 'none' },               // I3 adds { status:'listed', catalogCommit, sha256 }
          registry: { commit, digests: { mappings, bindings } } | null },   // null exactly when impactInput.registry is null (a workspace bundle built without a registry)
  confirmedLeads: [], rejectedLeads: [], likelyImpacts: [], unresolved: [] }
```

A `startedFrom` key is allowed. I6 writes it, and the record is `{assessmentId}`.

### 17.2 Validation

`Assessments.Validate` dispatches on `schemaVersion`:
- **1:** today's rules, unchanged.
- **2:** the same lead, impact, view and unresolved rules. Then:
  - the pins must equal the model's, with these messages: `snapshot mismatch` (canonical compare); `master list mismatch`; `registry mismatch`, for a commit or either digest;
  - `approval is not supported` when the record has a key `approved` or `approvalStatus`, or any key whose lower-case form contains `signoff` or `sign-off`, at the top level or in any likely impact (B31);
  - `assessor is required` on render.
- **Anything else:** `invalid assessment`.

### 17.3 Freezing (`Freezing.Freeze(record, model, paths, views) → JsonObject`)

```text
{ format: 1,
  record,                                                      // clone of the validated record
  pins,                                                        // schema 2: record.pins; schema 1: built from snapshotId and registry fields, masterList {status:'none'}
  leads: { confirmed: [{key, label, sourceCommit}], rejected: [{key, label, reason}] },   // label from the node; key order as written
  paths: [ cited paths, in record order of first citation, cloned ],
  evidence: [ { evidenceIri, sourceId, commit, role, path, startLine, endLine, blobId,
                lines?, page?: {title?, confluenceUrl?} } ],   // every evidence IRI cited by impacts, sorted by (sourceId, evidenceIri)
  gaps: { snapshot: Coverage.Gaps(model),
          traces: gaps of Paths.Read(model, confirmedLeads) minus any equal to a snapshot gap, in result order,
          evidence: the EvidenceText gaps of frozen evidence, de-duplicated and sorted,
          unresolved: record.unresolved },
  digest }                                                     // EcmaJson.CanonicalDigest of this object without 'digest'
```

`Assessments.Validate` now returns the frozen `digest` for both schemas. This keeps `feedback-render` and the printed `Assessment digest` in step.

### 17.4 Rendering

`AssessmentMarkdown.Render(frozen) → string` renders schema 2. It uses `escaped()` from I1 for every interpolated value, and joins lines with `\n`, with a final `\n`:

```text
# Impact assessment: {requestId}
(blank)
{description}
(blank)
Assessor: {assessor}
Snapshot: {pins.snapshotId.catalogCommit}
Relationship master list: none | {catalogCommit} (sha256 {sha256})
C4 registry: {pins.registry.commit} | none
Assessment digest: {digest}
(blank)
## Confirmed leads
- {sourceId}: {nodeIri} — {label}
(blank)
## Not expected to be affected
- {sourceId}: {nodeIri} — {reason}
(blank)
## Likely impacts
- {sourceId}: {nodeIri} — {reason}; paths {ids, ", "}; evidence {iris, ", "}; views {ids, ", "}
(blank)
## Evidence
- {evidenceIri} — {role} in {sourceId} {path}:{startLine}-{endLine} at {commit}
  Page: {title or "untitled"} ({confluenceUrl or "no Confluence address"})        ← exported pages only
      {escaped line}                                                             ← exported pages only, one per cited line, 6 spaces
(blank)
## Unresolved gaps and questions
- {code}: {reason}
(blank)
## Snapshot coverage gaps
- {code}: {sourceId} {selector} — {reason}
(blank)
## Gaps from cited path traces
- {code}: {reason}
(blank)
## Evidence gaps
- {code}: {sourceId} {path} — {reason}
```

Absent values render as empty strings, as `escaped(undefined)` does. An empty section keeps its heading with no items, exactly as today's renderer does.

`AssessmentMarkdownV1.Render` (schema 1) keeps today's output byte for byte. After its `## Unresolved gaps and questions` section, it appends the three new sections, `## Snapshot coverage gaps`, `## Gaps from cited path traces` and `## Evidence gaps`, in the same format (B37 item 7, B30). The printed digest becomes the frozen digest, which changes the `Assessment digest:` line for schema 1 renders. This is part of B37 item 7, because the gap sections are now part of what is digested. Note this in the baseline commit message.

### 17.5 `FreezingTests.cs`

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Impact.Render;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class FreezingTests
{
    private const string P = "https://example.test/policies/graph#";
    private static ImpactModel Confluence() =>
        ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("confluence", "assessment-render")));

    private static (JsonObject Record, ImpactModel Model, JsonArray Paths) Draft(string? reason = null)
    {
        var model = Confluence();
        var record = Assessments.New(model, "CR-9", "Raise the expense approval threshold\n");
        var paths = Paths.Read(model, [("fixture/policies", P + "expense-policy")])["paths"]!.AsArray();
        var path = paths.First(p => p!["steps"]!.AsArray().Count == 2)!;
        record["assessor"] = "Analyst A";
        record["confirmedLeads"] = new JsonArray(new JsonObject { ["sourceId"] = "fixture/policies", ["nodeIri"] = P + "expense-policy" });
        record["likelyImpacts"] = new JsonArray(new JsonObject
        {
            ["key"] = new JsonObject { ["sourceId"] = "fixture/policies", ["nodeIri"] = P + "ledger" },
            ["reason"] = reason ?? "Ledger supports approvals",
            ["pathIds"] = new JsonArray(path["id"]!.GetValue<string>()),
            ["evidenceIris"] = new JsonArray(path["steps"]!.AsArray().SelectMany(s => s!["evidenceIris"]!.AsArray()).Select(e => (JsonNode?)e!.GetValue<string>()).ToArray())
        });
        return (record, model, paths);
    }

    [Fact]
    public void Init_writes_schema_2_with_pins()                                     // B32 (command line)
    {
        var record = Assessments.New(Confluence(), "CR-9", "x");
        Assert.Equal(2, record["schemaVersion"]!.GetValue<int>());
        Assert.Equal("none", record["pins"]!["masterList"]!["status"]!.GetValue<string>());
        Assert.False(record.ContainsKey("architect"));
    }

    [Fact]
    public void Rendered_schema_2_shows_pins_page_evidence_and_every_gap_section()   // B25, B30, B32
    {
        var (record, model, paths) = Draft();
        var md = AssessmentMarkdown.Render(Freezing.Freeze(record, model, paths, []));
        Assert.Contains("Relationship master list: none", md);
        Assert.Contains("  Page: Expense policy (https://confluence.example.test/pages/1001)", md);
        Assert.Contains("      Claims over 500 need a manager's approval.", md);
        Assert.Contains("  Page: Approval process (no Confluence address)", md);
        Assert.Contains("## Snapshot coverage gaps", md);
        Assert.Contains("- no-relationship-master-list:", md);
        Assert.Contains("- page-metadata-missing: fixture/policies pages/approval-process.md — Exported page has no Confluence address", md);
        Assert.DoesNotContain("The old ledger still exports", md);          // ordinary document lines are not printed (B38)
    }

    [Fact]
    public void Injected_markup_renders_as_inert_text()                              // Q4
    {
        var (record, model, paths) = Draft("<script>x</script> | # Heading\n## Fake");
        var md = AssessmentMarkdown.Render(Freezing.Freeze(record, model, paths, []));
        Assert.DoesNotContain("<script>", md);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt; &#124; # Heading ## Fake", md);
        Assert.DoesNotContain("\n## Fake", md);
    }

    [Fact]
    public void The_digest_is_the_canonical_digest_of_the_frozen_assessment()        // Q3
    {
        var (record, model, paths) = Draft();
        var frozen = Freezing.Freeze(record, model, paths, []);
        var withoutDigest = frozen.DeepClone().AsObject();
        withoutDigest.Remove("digest");
        Assert.Equal(EcmaJson.CanonicalDigest(withoutDigest), frozen["digest"]!.GetValue<string>());
        Assert.Equal(frozen["digest"]!.GetValue<string>(), Assessments.Validate(record, model, paths, []));
    }

    [Fact]
    public void Sign_off_and_mismatched_pins_are_refused()                           // B31, B33
    {
        var (record, model, paths) = Draft();
        var signed = record.DeepClone().AsObject(); signed["managerSignOff"] = "yes";
        Assert.Equal("approval is not supported", Assert.Throws<ImpactException>(() => Assessments.Validate(signed, model, paths, [])).Message);
        var moved = record.DeepClone().AsObject(); moved["pins"]!["masterList"] = new JsonObject { ["status"] = "listed", ["catalogCommit"] = new string('a', 40), ["sha256"] = new string('b', 64) };
        Assert.Equal("master list mismatch", Assert.Throws<ImpactException>(() => Assessments.Validate(moved, model, paths, [])).Message);
    }
}
```

### 17.6 Steps

- [ ] Write the tests, and confirm that they fail.
- [ ] Implement 17.1–17.4. Change `CommandRunner` so that `assessment-init` writes schema 2 and `assessment-render` renders either schema.
- [ ] In `c4-walkthrough.mjs`, set `assessor: 'Sample architect'` in place of `architect` on the assessment record only. The feedback record keeps `architect`, because the feedback schema is unchanged. Run `node --test test/acceptance/c4.test.mjs`. Adjust only the assertions that read the assessment header, from `Architect:` to `Assessor:`.
- [ ] Add a pilot step that renders a **schema 1** record: a committed copy of today's `inputs/013/assessment-input.json`, at `test/impact-corpus/pilot/fixtures/assessment-v1.json`. Its expected output is today's `expected/013/assessment.md` plus the three gap sections and the new digest. It proves that schema 1 rendering is still supported.
- [ ] Commit `feat: freeze and render schema 2 assessments (B30, B32 command line)`.
- [ ] Follow the baseline update procedure for **B37 item 7**. The changes are: the `assessment-init` outputs become schema 2 (a record format change; `assessment-init` is not in B37's unchanged list); assessment renders gain the gap sections and new digests; feedback inputs and outputs follow the digest. Push.

## I2 close

- [ ] CI is green on the branch.
- [ ] Post the handoff. It says I2 is done, lists B1, B2, B3, B5, B25, B26, B27 and B38 and the command-line parts of B30 and B32, and gives I3 as next. **Stop.**
