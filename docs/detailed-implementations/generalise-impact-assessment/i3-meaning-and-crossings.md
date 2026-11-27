# I3. Relationship meaning and crossings: detailed implementation plan

**Inputs:**
- [Behaviours](../../features/generalise-impact-assessment/02-behaviours.md): B6, B7, B8, B10, B11, B12 and B13 are delivered here.
- ADR [0032](../../adr/0032-pin-the-relationship-master-list-to-the-catalog-commit.md).
- [Task overview](../../superpowers/plans/generalise-impact-assessment/plan.md).
- [I1 conventions](i1-c4-parity.md#conventions-used-in-every-engine-task) and the [I2 baseline update procedure](i2-evidenced-graphs.md).

**Start:** I2 is done and its handoff is posted.

The C4 fixtures have no `denotes`, no reviewed links and no master list. Every existing baseline file must therefore stay byte-identical through this increment. The new behaviours are proved by the `meaning` scenario and its unit tests.

---

## Task 18: Master list packaging (B10)

**Files:**
- create `src/c4/master-list.mjs` and `test/impact-master-list.test.mjs`;
- modify `src/c4/impact-input.mjs`, `src/c4/impact-bundle.mjs`, `src/workspace/bundle.mjs` and `src/c4/cli.mjs`, so the front end passes `divisionRoot` and `catalogCommit` through.

### 18.1 `src/c4/master-list.mjs`

```js
import { createHash } from 'node:crypto';
import { readAtCommit } from './git.mjs';

export const MASTER_LIST_PATH = 'relationships/master-list.json';
const IMPACTS = new Set(['subject-to-object', 'object-to-subject', 'both', 'none']);

// Reads the division's relationship master list at the catalog commit (ADR 0032).
// The engine relies only on schemaVersion and relationships[].{iri, impact}; other fields are kept but ignored.
export function loadMasterList({ divisionRoot, catalogCommit }) {
  const raw = readAtCommit(divisionRoot, catalogCommit, MASTER_LIST_PATH);
  if (raw === null) return null;
  let list;
  try { list = JSON.parse(raw); } catch { throw new Error('invalid relationship master list'); }
  const seen = new Set();
  if (list?.schemaVersion !== 1 || !Array.isArray(list.relationships)) throw new Error('invalid relationship master list');
  for (const entry of list.relationships) {
    if (typeof entry?.iri !== 'string' || !entry.iri || !IMPACTS.has(entry.impact) || seen.has(entry.iri))
      throw new Error('invalid relationship master list');
    seen.add(entry.iri);
  }
  const bytes = Buffer.from(raw, 'utf8');
  return { bytes, identity: { status: 'listed', catalogCommit, sha256: createHash('sha256').update(bytes).digest('hex') } };
}
```

When the list is present, `buildImpactInput` adds a final key, `masterList: identity`. When it is absent, `masterList` is omitted. The impact bundle and the workspace bundle both `include('relationshipMasterList', 'division-core', MASTER_LIST_PATH, list.bytes)`. The front end calls `loadMasterList` after `loadRegistries`, so a bad list fails before any output, with the message `invalid relationship master list`.

### 18.2 `test/impact-master-list.test.mjs`

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { withPilotFixture } from './fixtures/c4/fixture-repos.mjs';
import { loadMasterList } from '../src/c4/master-list.mjs';
import { loadSnapshot } from '../src/c4/snapshot.mjs';
import { buildImpactInput } from '../src/c4/impact-input.mjs';

const list = { schemaVersion: 1, relationships: [
  { iri: 'https://ontology-kit.dev/schema#contains', impact: 'subject-to-object', definition: 'kept, ignored' },
  { iri: 'https://ontology-kit.dev/schema#communicates-with', impact: 'both' }] };

test('a missing list is null, so the engine applies B9', () => withPilotFixture(f => {
  assert.equal(loadMasterList(f), null);
  assert.equal('masterList' in buildImpactInput({ snapshot: loadSnapshot(f), registry: null }), false);
}));

test('the list is read at the catalog commit with its digest as identity', () => withPilotFixture(f => {
  const text = JSON.stringify(list);
  f.writeDivision('relationships/master-list.json', text);
  const catalogCommit = f.commitDivision('add list');
  const loaded = loadMasterList({ divisionRoot: f.divisionRoot, catalogCommit });
  assert.deepEqual(loaded.identity, { status: 'listed', catalogCommit,
    sha256: createHash('sha256').update(text).digest('hex') });
  assert.equal(loadMasterList(f), null, 'an earlier catalog commit has no list');
}));

for (const [name, bad] of [
  ['wrong schema', { schemaVersion: 2, relationships: [] }],
  ['unknown impact', { schemaVersion: 1, relationships: [{ iri: 'x', impact: 'sometimes' }] }],
  ['duplicate iri', { schemaVersion: 1, relationships: [{ iri: 'x', impact: 'none' }, { iri: 'x', impact: 'both' }] }],
  ['missing iri', { schemaVersion: 1, relationships: [{ impact: 'none' }] }]]) {
  test(`an invalid list fails packaging: ${name}`, () => withPilotFixture(f => {
    f.writeDivision('relationships/master-list.json', JSON.stringify(bad));
    const catalogCommit = f.commitDivision('bad list');
    assert.throws(() => loadMasterList({ divisionRoot: f.divisionRoot, catalogCommit }), /invalid relationship master list/);
  }));
}
```

Steps:
- [ ] Write the test, and confirm that it fails.
- [ ] Implement 18.1. Run `npm test`: the baseline is unchanged, because no fixture has a list.
- [ ] Commit `feat: package the relationship master list (#198)` and push.

---

## Task 19: Impact directions (B6, B7, B8, B10)

**Files:**
- modify `Model/ImpactModel.cs`, adding `MasterList? MasterList`, where `MasterList` holds an `Identity` `JsonObject` and `Impact(string relationshipIri) → string?`;
- modify `Engine/Paths.cs` and `Engine/Assessments.cs` (pins);
- create `Tests/Impact/DirectionsTests.cs`.

**Model.** When `impactInput.masterList` is present, read the `relationshipMasterList` file. Check that its SHA-256 equals `masterList.sha256`, or throw `invalid impact bundle: master list digest`. Then parse its `relationships`.

**Walk rules (B6–B9).** With no list, I1's walk is unchanged: `direction: 'outgoing'` and the B9 gap. With a list, for the current node `N` in source `S`, take each assertion `A` of `S`, in Map order:

| Impact of `A.relationship` | `N` is the subject | `N` is the object |
| --- | --- | --- |
| `subject-to-object` | step to the object, `direction: 'subject-to-object'` | none |
| `object-to-subject` | none | step to the subject, `direction: 'object-to-subject'` |
| `both` | as `subject-to-object` | as `object-to-subject` |
| `none` | count `A.iri` under its relationship (B7) | count `A.iri` under its relationship (B7) |
| unlisted | record `A.iri` and `N` under the relationship (B8) | record `A.iri` and `N` under the relationship (B8) |

- Step objects keep I1's key order, with the direction value as above. `subject` and `object` are always the assertion's own subject and object. Never swap them.
- Candidate ordering is `OrderBy(key(target)).ThenBy(assertionIri ?? "").ThenBy(type).ThenBy(direction)`.

**Result additions with a list (B7, B8, B10).** Two keys go before `paths`, so the result order is `{snapshotId, registryCommit, masterList, notFollowed, coverage, paths, gaps}`:
- `masterList`: the identity.
- `notFollowed: [{relationship, assertions}]`: the number of distinct assertion IRIs per `none` relationship, sorted by relationship.

There is one gap per unlisted relationship: `{code:'unlisted-relationship', relationship, nodes:[{sourceId, nodeIri}], assertions, reason:'The impact of this relationship is unknown: it is not in the relationship master list'}`. Its `nodes` are distinct and sorted by `key`, and `assertions` is the number of distinct IRIs. These gaps are sorted by relationship and placed after the evidence gaps. Without a list, the result keys stay exactly as in I1, which keeps the baseline stable.

**Pins (B10).** `Assessments.New` sets `pins.masterList` to the model's identity, or to `{status:'none'}`. Freezing and rendering already print it.

### `DirectionsTests.cs`

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class DirectionsTests
{
    private const string A = "https://alpha.example/#", K = "https://ontology-kit.dev/schema#";

    private static ImpactModel WithList(params (string Iri, string Impact)[] entries) =>
        TestModels.MakeModel(masterList: new JsonObject
        {
            ["schemaVersion"] = 1,
            ["relationships"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject { ["iri"] = e.Iri, ["impact"] = e.Impact }).ToArray())
        });

    private static IEnumerable<JsonObject> Steps(JsonObject r) =>
        r["paths"]!.AsArray().SelectMany(p => p!["steps"]!.AsArray()).Select(s => s!.AsObject());

    [Fact]
    public void Subject_to_object_is_followed_forwards_only()                                    // B6
    {
        var r = Paths.Read(WithList((K + "contains", "subject-to-object"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "container")]);
        Assert.All(Steps(r).Where(s => s["type"]!.GetValue<string>() == "assertion"), s => Assert.Equal("subject-to-object", s["direction"]!.GetValue<string>()));
        Assert.DoesNotContain(Steps(r), s => s["object"]?["nodeIri"]?.GetValue<string>() == A + "container" && s["direction"]!.GetValue<string>() == "object-to-subject");
        Assert.Contains(Steps(r), s => s["object"]!["nodeIri"]!.GetValue<string>() == A + "component");
    }

    [Fact]
    public void Object_to_subject_steps_from_the_object_and_keeps_the_assertion_orientation()   // B6
    {
        var r = Paths.Read(WithList((K + "contains", "object-to-subject"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "container")]);
        var step = Assert.Single(Steps(r), s => s["direction"]!.GetValue<string>() == "object-to-subject" && s["subject"]!["nodeIri"]!.GetValue<string>() == A + "system");
        Assert.Equal(A + "container", step["object"]!["nodeIri"]!.GetValue<string>());
        Assert.DoesNotContain(Steps(r), s => s["object"]!["nodeIri"]!.GetValue<string>() == A + "component");
    }

    [Fact]
    public void Both_follows_each_way()                                                           // B6
    {
        var r = Paths.Read(WithList((K + "contains", "both"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "container")]);
        Assert.Contains(Steps(r), s => s["direction"]!.GetValue<string>() == "subject-to-object");
        Assert.Contains(Steps(r), s => s["direction"]!.GetValue<string>() == "object-to-subject");
    }

    [Fact]
    public void None_is_counted_and_never_a_gap()                                                 // B7
    {
        var r = Paths.Read(WithList((K + "contains", "subject-to-object"), (K + "communicates-with", "none")), [("fixture/Alpha", A + "system")]);
        var counted = Assert.Single(r["notFollowed"]!.AsArray());
        Assert.Equal(K + "communicates-with", counted!["relationship"]!.GetValue<string>());
        Assert.Equal(1, counted["assertions"]!.GetValue<int>());
        Assert.DoesNotContain(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() is "unlisted-relationship" or "no-relationship-master-list");
        Assert.DoesNotContain(Steps(r), s => s["relationship"]?.GetValue<string>() == K + "communicates-with");
    }

    [Fact]
    public void An_unlisted_relationship_is_one_gap_per_iri_and_never_a_step()                   // B8
    {
        var r = Paths.Read(WithList((K + "contains", "subject-to-object")), [("fixture/Alpha", A + "system")]);
        var gap = Assert.Single(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unlisted-relationship")!;
        Assert.Equal(K + "communicates-with", gap["relationship"]!.GetValue<string>());
        Assert.Equal(A + "system", gap["nodes"]![0]!["nodeIri"]!.GetValue<string>());
        Assert.Contains("unknown", gap["reason"]!.GetValue<string>());
        Assert.DoesNotContain(Steps(r), s => s["relationship"]?.GetValue<string>() == K + "communicates-with");
    }

    [Fact]
    public void The_list_identity_is_on_results_and_record_pins()                                // B10
    {
        var model = WithList((K + "contains", "both"));
        var r = Paths.Read(model, [("fixture/Alpha", A + "system")]);
        Assert.Equal("listed", r["masterList"]!["status"]!.GetValue<string>());
        Assert.Equal(["snapshotId", "registryCommit", "masterList", "notFollowed", "coverage", "paths", "gaps"], r.Select(p => p.Key));
        Assert.Equal(r["masterList"]!.ToJsonString(), Assessments.New(model, "CR", "x")["pins"]!["masterList"]!.ToJsonString());
    }
}
```

`TestModels.MakeModel(change, blobOverride, masterList)` gains `JsonObject? masterList`. When it is set, it adds the `relationshipMasterList` file to the in-memory bundle, and `input.masterList = {status:'listed', catalogCommit: snapshotId.catalogCommit, sha256}`.

Steps:
- [ ] Write the tests, and confirm that they fail.
- [ ] Implement. Run the conformance tests: they are unchanged.
- [ ] Commit `feat: follow relationship impact directions (B6, B7, B8, B10)` and push.

---

## Task 20: Reviewed-link crossings (B11, B12, B13)

**Files:** modify `Model/ImpactModel.cs`, adding `TermOf(sourceId, nodeIri)`, `LinksFrom(sourceId, term)` and `NodesDenoting(sourceId, term)`. Modify `Engine/Paths.cs`, and create `Tests/Impact/CrossingsTests.cs`.

### 20.1 Rules (U3, U9, U19)

- **A node's term** is its `denotes`.
  - A node without `denotes` takes part in no crossing and raises no gap (B13).
  - A term counts as linked `exact` to a shared term `S` either when a reviewed link `{source: nodeSource, local: T, shared: S, relation}` exists, or when `T` is itself a `division-core` concept IRI, in which case `S = T` (a synthetic `exact` link, written in steps as `{source: nodeSource, local: T, shared: T, relation: 'exact'}`).
- **When the walk visits node `A`**, at the start and at every step target, with term `T` in source `sA`, three cases apply:
  - **No links** (and `T` is not a core term): add `A` to the `unlinked-term` record for `(sA, T)`.
  - **A `specializes` link to `S`:** add a `specialization-boundary` record for `(sA, T, S)`. Do not cross through `S` (B12).
  - **An `exact` link to `S`:** every node `B` in **another** source `sB` whose term `TB` links to `S` (`exact` or `specializes`, or a synthetic `exact`) is a crossing target. Same-source nodes are never targets.
- **The crossing step** keeps this key order:

  ```text
  { type: 'reviewedLinkCrossing', from: {sourceId, nodeIri}, to: {sourceId, nodeIri},
    sharedTerm: S, links: [fromLink, toLink], catalogCommit }
  ```

  Each link is `{source, local, shared, relation}`. `catalogCommit` is `snapshotId.catalogCommit`. A crossing is one step towards the depth limit (U8). It sorts with the other candidate edges by `key(target)`, then `""` (it has no assertion IRI), then by type.
- **Gaps** are added after the unlisted-relationship gaps:
  - `unlinked-term`, one per `(sourceId, term)` (U19): `{code:'unlinked-term', sourceId, term, nodes:[{sourceId, nodeIri}, ...sorted by key], reason:'Term has no reviewed link, so other sources are not reached from it'}`. The `nodes` shape matches `unlisted-relationship`, so every gap has one contract.
  - `specialization-boundary`, one per `(sourceId, term, sharedTerm)`: `{code:'specialization-boundary', sourceId, term, sharedTerm, reason:'Impact is not followed from a specialised term to its general term'}`.

  Both groups are sorted by `EcmaJson.Stringify`.
- **Labels are never compared** anywhere in this code (B13). A unit test in 20.2 pins this.

### 20.2 `CrossingsTests.cs`

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class CrossingsTests
{
    private const string Core = "https://example.test/core#Claim";

    // Two sources, each with one node labelled "Claim" that denotes its own local term.
    private static ImpactModel TwoSources(string? linkA, string? linkB) => TestModels.TwoSourceModel(
        termA: "https://example.test/a/ontology#Claim", termB: "https://example.test/b/ontology#Claim", shared: Core,
        relationA: linkA, relationB: linkB);

    private static IEnumerable<JsonObject> Crossings(JsonObject r) => r["paths"]!.AsArray()
        .SelectMany(p => p!["steps"]!.AsArray()).Select(s => s!.AsObject())
        .Where(s => s["type"]!.GetValue<string>() == "reviewedLinkCrossing");

    [Fact]
    public void Exact_links_are_crossed_in_both_directions_and_cite_both_links()                 // B11
    {
        var model = TwoSources("exact", "exact");
        var forward = Assert.Single(Crossings(Paths.Read(model, [("fixture/a", TestModels.NodeA)])));
        Assert.Equal("fixture/b", forward["to"]!["sourceId"]!.GetValue<string>());
        Assert.Equal(2, forward["links"]!.AsArray().Count);
        Assert.Equal(Core, forward["sharedTerm"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{40}$", forward["catalogCommit"]!.GetValue<string>());
        Assert.Single(Crossings(Paths.Read(model, [("fixture/b", TestModels.NodeB)])));
    }

    [Fact]
    public void Specializes_is_crossed_only_from_general_to_specific()                            // B12
    {
        var model = TwoSources("exact", "specializes");
        Assert.Single(Crossings(Paths.Read(model, [("fixture/a", TestModels.NodeA)])));
        var up = Paths.Read(model, [("fixture/b", TestModels.NodeB)]);
        Assert.Empty(Crossings(up));
        var gap = Assert.Single(up["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "specialization-boundary")!;
        Assert.Equal("https://example.test/b/ontology#Claim", gap["term"]!.GetValue<string>());
        Assert.Equal(Core, gap["sharedTerm"]!.GetValue<string>());
    }

    [Fact]
    public void Identical_labels_without_links_never_cross_and_report_one_gap_per_term()          // B13, U19
    {
        var r = Paths.Read(TwoSources(null, null), [("fixture/a", TestModels.NodeA), ("fixture/a", TestModels.NodeA2)]);
        Assert.Empty(Crossings(r));
        Assert.DoesNotContain(r["paths"]!.AsArray(), p => p!["steps"]!.AsArray().Any(s => s!["to"]?["sourceId"]?.GetValue<string>() == "fixture/b"));
        var gap = Assert.Single(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unlinked-term" && g["sourceId"]!.GetValue<string>() == "fixture/a")!;
        Assert.Equal([TestModels.NodeA, TestModels.NodeA2], gap["nodes"]!.AsArray().Select(n => n!["nodeIri"]!.GetValue<string>()));
    }

    [Fact]
    public void A_node_without_a_denoted_term_reports_nothing()
    {
        var r = Paths.Read(TestModels.MakeModel(), [("fixture/Alpha", "https://alpha.example/#system")]);
        Assert.DoesNotContain(r["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() is "unlinked-term" or "specialization-boundary");
    }

    [Fact]
    public void A_crossing_counts_as_one_step_towards_the_depth_limit()                            // U8
    {
        var r = Paths.Read(TwoSources("exact", "exact"), [("fixture/a", TestModels.NodeA)], maxDepth: 1);
        Assert.All(r["paths"]!.AsArray(), p => Assert.Single(p!["steps"]!.AsArray()));
        Assert.Single(Crossings(r));
    }
}
```

**`TestModels.TwoSourceModel(termA, termB, shared, relationA, relationB)`** builds an `impactInput` with:
- sources `fixture/a` and `fixture/b`;
- `NodeA` = `https://example.test/a/graph#claim` and `NodeA2` = `https://example.test/a/graph#claim-copy`, both labelled `Claim` and both denoting `termA`;
- `NodeB` = `https://example.test/b/graph#claim`, labelled `Claim` and denoting `termB`;
- one evidenced assertion `NodeA` `rel#next` `NodeA2`, so `NodeA2` is reached and supported;
- a master list with `rel#next` set to `subject-to-object`;
- catalog concepts for `termA`, `termB` and `shared` (`shared` from `division-core`);
- `reviewedLinks`: one per non-null relation;
- no registry rows.

Both nodes are supported through the assertion's evidence. Give `NodeB` a self-contained evidenced assertion to an `https://example.test/b/graph#ledger` node, so that it is supported.

Steps:
- [ ] Write the tests and the `TwoSourceModel` helper, and confirm that the tests fail.
- [ ] Implement. Run the conformance tests: they are unchanged, because the C4 fixtures have no `denotes`. If any baseline file changes, it must be an `unlinked-term` gap only. If so, follow the baseline update procedure for **B37 item 5**.
- [ ] Commit `feat: cross sources only through reviewed links (B11, B12, B13)` and push.

---

## Task 21: Command-line walkthrough for meaning and crossings

**Files:**
- create `test/fixtures/impact/meaning-repos.mjs`, with two sources `fixture/claims` and `fixture/ledger`, each with one exported page, nodes denoting local terms linked `exact` to `division-core` `https://example.test/core#Claim`, and a division `relationships/master-list.json` with one relationship of each impact value;
- add a scenario `meaning` to `scenarios.mjs`;
- create `test/impact-meaning.test.mjs`.

`test/impact-meaning.test.mjs` runs the scenario through `runC4Cli`, and asserts from the JSON outputs:

| Case | Expected |
| --- | --- |
| `search` | the leads are supported |
| `paths` from the claims node | it crosses into `fixture/ledger` with a `reviewedLinkCrossing` step; `masterList.status === 'listed'`; one `unlisted-relationship` gap for the unlisted IRI; `notFollowed` counts the `none` relationship |
| `assessment-render` | prints `Relationship master list: <catalogCommit> (sha256 <digest>)` |
| a second catalog commit where the link is removed | no crossing, and one `unlinked-term` |
| a corrupted master list commit | `runC4Cli` exits 1 with `invalid relationship master list` |

Steps:
- [ ] Write the fixture, scenario and test.
- [ ] Run `node test/impact-corpus/capture.mjs meaning` and `export-bundles.mjs`, then `npm test`.
- [ ] Commit `test: cover meaning and crossings end to end (#198)` and push.

## I3 close

- [ ] CI is green.
- [ ] Post the handoff. It says I3 is done, lists B6, B7, B8, B10, B11, B12 and B13, and gives I4 as next. It also notes that the master list format is now a contract that #205 must keep (ADR 0032). **Stop.**
