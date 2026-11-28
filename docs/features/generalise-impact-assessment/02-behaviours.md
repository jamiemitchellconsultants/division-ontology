---
feature: 198
slug: generalise-impact-assessment
phase: analyst
sources: [01-vision.md]
---

# Generalise impact assessment beyond C4 — behaviours

**Feature:** [#198](https://github.com/jamiemitchellconsultants/division-ontology/issues/198)

**Phase:** [#200 — Analyst behaviours](https://github.com/jamiemitchellconsultants/division-ontology/issues/200)

**Basis:** [Accepted vision](01-vision.md) (PR #206), the product-owner handoff on #198, and the analyst decisions recorded with the user on 26 November 2026. Each numbered outcome below matches the vision. Scenarios specify observable behaviour. They do not prescribe components, storage or deployment.

**Amended** on 27 November 2026 in answer to the developer's pushback on #200, which feature-analyze raised before developer planning:
- B4 is retired and replaced by B37.
- B38 is added.
- B1, B5, B13, B25 and B26 are refined, and keep their meaning.
- U16 to U20 are added.
- The ImpactPath definition is corrected.

**Amended** on 28 November 2026 in answer to the developer's pushback on #200, raised while applying the PR #220 review (items 5, 6 and 7):
- B39 is added: a reviewed-link crossing substantiates a likely impact.
- B17, B30 and B34 are refined, and keep their meaning.
- U21 to U23 are added.

**Amended** on 28 November 2026 in answer to the developer's pushback on #200, raised by the PR #220 re-review (finding 37):
- B40 is added: a trace returns at most 500 paths, and says when more exist.
- U24 is added.

An **assessor** is the architect or analyst carrying out an assessment. A **reader** holds `ReadKnowledgeGraph`. A **manager** holds `ManageKnowledgeGraph`. The **Confluence fixture** is a test source shaped like a Confluence group graph. Its nodes and assertions cite `selected-document` evidence: line ranges of exported pages. It has no C4 mappings. The **C4 fixtures** are the existing C4 pilot fixtures, used with the same inputs as today, including the design-record fixtures.

Evidence **cites a node** in two cases: the node carries the evidence itself, as a design record does, or the node is the subject or object of an assertion that carries the evidence. An **exported page** is a Markdown file, cited by `selected-document` evidence, that opens with a front-matter metadata block. Any other file cited by `selected-document` evidence is an **ordinary document**.

Gap codes are written in `code` form. Every gap carries a reason, and no gap ever states that something is unaffected.

## Behaviours

### Outcome 1 — Any evidenced graph can be assessed, with or without C4 mappings

#### B1 — Evidence makes a lead supported

**Priority:** P1

**Given** the Confluence fixture, and a change request description that matches the label of a node cited by verified `selected-document` evidence, **when** an assessor searches, **then** that node is returned as an ImpactLead with `supported: true`. The ImpactLead shows its source id, its node IRI, its source commit, how it matched, and a reason that names each distinct evidence role citing it, in sorted order (here, `selected-document`). Evidence counts when it verifies at the pinned commit, and any evidence role counts. A node that is the subject or object of an evidenced assertion is cited by that assertion's evidence, even if it carries no evidence itself.

#### B2 — A node with no evidence and no mapping is an unsupported lead

**Priority:** P1

**Given** a matching graph node that no verified evidence cites and no C4 mapping backs, **when** an assessor searches, **then** the node is still returned, with `supported: false` and the reason `No verified evidence or supported mapping`. Catalog concepts that are not graph nodes stay unsupported, as they are today.

#### B3 — Impact paths start from any supported lead

**Priority:** P1

**Given** a supported ImpactLead from the Confluence fixture, **when** an assessor traces impact paths from it, **then** paths start from that node, and no `unsupported-start` gap is reported for it. **Given** a start that is not supported under B1, **when** paths are traced, **then** the start is skipped with an `unsupported-start` gap, and its reason is `Start lacks verified evidence or a supported mapping`.

#### B37 — C4 fixtures behave as today, apart from an exhaustive list of intended changes

**Priority:** P1

Replaces B4.

**Given** the C4 fixtures, with no relationship master list and the same command-line inputs as today, **when** search, paths, C4 views and assessment rendering run, **then** these are unchanged from today:

- the identity, order and match of every lead;
- the identity, order and steps of every path from a C4-mapped start;
- the items and edges of every C4 view;
- every rendered assessment whose record cites only C4-mapped starts.

Only these differences appear, and any other difference fails:

- unmapped nodes that verified evidence cites become supported, with an evidence reason (B1);
- starts that were refused only because they had no mapping now produce paths (B3);
- a start that is still refused carries B3's reason, `Start lacks verified evidence or a supported mapping`;
- every path result and assessment carries the `no-relationship-master-list` gap (B9);
- a path result carries one `unlinked-term` gap for each denoted term without a reviewed link (B13);
- a view focused on a node with no C4 mapping reports `no-c4-mapping` in place of `unsupported-focus` (B5);
- every rendered assessment also lists the snapshot's coverage gaps, and the gaps from the searches and path traces it cites (B30).

A C4-mapped lead keeps the reason `Reviewed mapping`. Ordinary-document evidence on the design-record fixtures renders as today (B38).

#### B5 — C4 views are optional

**Priority:** P2

**Given** a supported node with no C4 mapping, **when** an assessor asks for a C4 view focused on it, **then** the view is refused with a `no-c4-mapping` gap that names the node. Search, paths and the assessment itself carry on unaffected. For a node with no C4 mapping at all, `no-c4-mapping` takes the place of today's `unsupported-focus`. A focus that is mapped, but at a level other than the one the view needs, still reports `unsupported-focus`.

### Outcome 2 — Impact follows the meaning of each relationship

#### B6 — A listed relationship is followed only in its impact direction

**Priority:** P1

**Given** a division relationship master list that gives relationship R the impact direction subject-to-object, and relationship D the direction object-to-subject, **when** a path reaches a node that is the subject of an R assertion, **then** the path steps from subject to object. **When** a path reaches a node that is the object of a D assertion, **then** the path steps from object to subject. Each step shows the relationship, the direction followed, the assertion and its evidence. A relationship listed as carrying impact both ways is followed in both directions.

#### B7 — A relationship listed as carrying no impact is not followed

**Priority:** P1

**Given** a master list that defines relationship N as carrying no impact, **when** a path reaches an N assertion, **then** no step is taken through it, and no gap is reported for it. The path result shows a count of the assertions not followed under each such relationship.

#### B8 — An unlisted relationship is a gap, not a step

**Priority:** P1

**Given** a master list that does not contain relationship U, **when** a path reaches a node with a U assertion in either direction, **then** no step is taken through it. The result reports one `unlisted-relationship` gap for each relationship IRI, naming the relationship, the nodes it was met at and the number of assertions skipped. The reason states that the relationship's impact is unknown.

#### B9 — No master list yet: today's rule plus a gap

**Priority:** P1

**Given** a division with no relationship master list, **when** paths are traced, **then** every assertion is followed from subject to object, as the C4 pilot does today, and the result carries exactly one `no-relationship-master-list` gap. Its reason states that impact direction is assumed, not defined.

#### B10 — The master list used is recorded

**Priority:** P1

**Given** paths traced or an assessment created, **when** the result or record is produced, **then** it identifies the master list used (its commit and digest), or records that none existed. A saved assessment is pinned to that identity along with its snapshot (B32).

### Outcome 3 — Impact crosses sources only through reviewed links

#### B11 — An `exact` link is crossed in either direction

**Priority:** P1

**Given** two sources whose nodes A and B denote local terms that each have a steward-reviewed `exact` link to the same shared term, **when** a path reaches A, **then** it crosses to B in one ReviewedLinkCrossing step that cites both links (their kind, their local and shared terms and the catalog commit). The same holds from B to A. A crossing counts as one step towards the maximum depth.

#### B12 — A `specializes` link is crossed only from general to specific

**Priority:** P1

**Given** node A denotes a term with an `exact` link to shared term S, and node B denotes a term that `specializes` S, **when** a path reaches A, **then** it crosses to B, citing both links. **Given** instead that A's term `specializes` S, **when** a path reaches A, **then** it does not climb to S or to anything else linked to S. It reports a `specialization-boundary` gap that names A's term and S.

#### B13 — No reviewed link means no crossing, whatever the labels

**Priority:** P1

**Given** the B11 fixture with the links removed, so that A's and B's terms have identical labels but no reviewed link, **when** a path reaches A, **then** no path reaches B. The result reports an `unlinked-term` gap for A's term, stating that other sources are not reached from it. Labels are never compared to make a crossing.

A result reports one `unlinked-term` gap for each source and denoted term, however many nodes or paths meet that term. The gap names the nodes it was met at, in sorted order. A node that denotes no term reports no `unlinked-term` gap.

#### B14 — C4 bindings are still crossed

**Priority:** P1

**Given** a reviewed C4 binding between nodes in two sources, **when** a path reaches the bound node, **then** it crosses the binding as it does today, citing the registry commit and the identity evidence.

#### B39 — A reviewed-link crossing substantiates a likely impact

**Priority:** P1

**Given** a traced path whose step arriving at node N is a ReviewedLinkCrossing (B11, B12), **when** an assessor records a likely impact on N that cites that path, **then** the impact is valid without citing any evidence IRI: the crossing's two reviewed links and its catalog commit are its support. The preview, the saved assessment and the export show those links and that commit as the impact's support. An impact on N that cites no such path still cites evidence from its paths, as B17 says. The command line applies the same rule.

#### B40 — A trace is bounded, and says when it is

**Priority:** P1

**Given** starts and a depth from 1 to 6, **when** paths are traced, in the workspace or on the command line, **then** the result holds at most 500 ImpactPaths: the first 500 in the order the trace finds them, the same order as today. When more paths exist, the result also reports one `paths-truncated` gap. Its reason states that more paths exist than are shown, and that fewer starts or a lower depth narrows the trace. A trace that finds 500 paths or fewer reports no such gap. An assessment may cite only paths in the bounded result, so the workspace and the command line accept the same citations (B17, B23).

### Outcome 4 — Analysts assess in the workspace, and readers open the result

#### B15 — A reader searches in the browser

**Priority:** P1

**Given** a signed-in reader and the active PublishedKnowledgeSnapshot, **when** the reader enters a change request description in the workspace, **then** the workspace lists ImpactLeads as B1 and B2 describe, in pages of at most 100. Each page carries a cursor for the next one, and the last page has none. No command line or source checkout is involved.

#### B16 — A reader traces paths in the browser

**Priority:** P1

**Given** a reader who has picked one or more leads, **when** they trace impact, **then** the workspace shows each ImpactPath step by step: its direction, endpoints, relationship or crossing, evidence and gaps, as B6 to B14 describe. A two-step path is never shown as a direct relationship between its endpoints.

#### B17 — A reader drafts and previews an assessment

**Priority:** P1

**Given** a reader with traced paths, **when** they confirm leads, reject leads with reasons, record likely impacts citing paths and evidence, and add unresolved gaps, **then** the workspace shows a preview of the ImpactAssessment and offers its Markdown export, without saving anything. The same validation applies as on the command line: an impact must appear in a path it cites, it may cite only evidence from those paths, it cites at least one piece of that evidence unless B39 applies, and a rejected lead needs a reason.

#### B18 — A manager saves an assessment

**Priority:** P1

**Given** a manager with a valid draft, **when** they save it, **then** the ImpactAssessment is stored with a stable identifier and link, the author's identity, the time, and the pins from B32. The save either succeeds whole or leaves nothing behind.

#### B19 — A reader without the manage role cannot save

**Priority:** P1

**Given** a reader who is not a manager, **when** they try to save, **then** the save is refused as forbidden. Their draft stays in the browser, and the Markdown export still works.

#### B20 — Another reader opens a saved assessment

**Priority:** P1

**Given** a saved ImpactAssessment, **when** a different reader opens its link, **then** they see the same request, leads, rejected leads, likely impacts, paths, evidence and gaps that the author saved.

#### B21 — Saved assessments can be listed

**Priority:** P2

**Given** saved assessments, **when** a reader lists them, **then** each appears with its request id, author, time saved and snapshot, newest first, in cursor-paged pages of at most 50.

#### B22 — A manager edits a saved assessment on its own snapshot

**Priority:** P2

**Given** a saved assessment whose snapshot is still active, **when** a manager edits it, **then** the change is validated as in B17 and saved. The change keeps its editor, its time and the previous content, so a reader can see what changed and who changed it.

#### B23 — The workspace and the command line render the same Markdown

**Priority:** P1

**Given** the same snapshot, master list, C4 registry and assessment record, **when** the record is exported from the workspace and rendered by the command line, **then** the two Markdown files are byte-identical.

#### B24 — The command line keeps working from checkouts

**Priority:** P1

**Given** an architect with clean source checkouts, **when** they run search, paths, view, assessment init and assessment render, **then** each command works as today, with B1 to B14 applied. No workspace is needed.

### Outcome 5 — Document evidence can be read where it is cited

#### B25 — Page evidence shows its title, address and cited lines

**Priority:** P1

**Given** `selected-document` evidence citing lines of an exported page, as defined above, **when** a lead, a path step or an assessment shows that evidence, **then** it shows the page's title, its original Confluence address and the exact cited lines, all read from the exported file at the pinned commit. Every cited line range in the assessment is shown this way.

#### B26 — Missing page metadata is a gap, not a blank

**Priority:** P1

**Given** an exported page whose front-matter block records no title, or no Confluence address, or neither, **when** its evidence is shown, **then** the file path and the cited lines are shown, along with a `page-metadata-missing` gap for that page.

#### B38 — Ordinary document evidence is shown like code evidence

**Priority:** P1

**Given** `selected-document` evidence citing an ordinary document, such as a feature document or an ADR with no front-matter block, **when** a lead, a path step or an assessment shows it, **then** it shows the path, line range, commit and role, as B28 does for code evidence. No `page-metadata-missing` gap is reported for it. In the workspace, the cited lines are also shown. In exported Markdown, its cited lines are not shown, as for code evidence.

#### B27 — Cited text that cannot be read is a gap

**Priority:** P2

**Given** evidence whose cited text cannot be read from the pinned bundle or checkout, **when** it is shown, **then** its path, line range, commit and role are shown, along with an `evidence-text-unavailable` gap. No text from another commit is shown in its place.

#### B28 — Code evidence is shown as it is today

**Priority:** P1

**Given** `implementation`, `test`, `ontology` or `deployment` evidence, **when** it is shown, **then** it shows the path, line range, commit and role, as the C4 pilot does today. In the workspace it also shows the cited lines.

#### B29 — Evidence text stays inside the signed-in workspace

**Priority:** P1

**Given** a source with page evidence, **when** the catalog is built and published, **then** no cited text appears in `dist/` or in any unauthenticated workspace response. Only signed-in readers receive cited text.

### Outcome 6 — Every assessment stays pinned and honest about its limits

#### B30 — An assessment carries the snapshot's coverage and its own gaps

**Priority:** P1

**Given** a snapshot with coverage gaps, and an assessment with unresolved gaps, **when** the assessment is previewed, saved, opened or exported, **then** it lists both: the snapshot's coverage gaps and the assessment's own unresolved gaps, each with its reason. It also carries every gap that the searches and path traces it cites reported, under B2 to B27. The searches it cites are the ones that returned its confirmed and rejected leads: it carries the gaps search reports about those leads, such as a `page-metadata-missing` or `evidence-text-unavailable` gap on a lead's page, recomputed at its pinned snapshot. The path traces it cites are the ones that produced the paths its likely impacts cite. The gaps that every trace carries, such as `no-relationship-master-list` (B9), are carried even when it has no confirmed lead.

#### B31 — There is no approval state

**Priority:** P1

**Given** any assessment, **when** someone tries to add an approval or sign-off field, through the API or a crafted record, **then** the attempt is refused. The workspace offers no approve or sign-off control.

#### B32 — A saved assessment is pinned

**Priority:** P1

**Given** a saved assessment, **when** it is opened or exported, **then** it shows:

- the PublishedKnowledgeSnapshot it used (catalog commit and source commits);
- the master list identity (B10);
- the C4 registry commit and digests, where C4 mappings were used.

#### B33 — An assessment is refused against any other snapshot

**Priority:** P1

**Given** a saved assessment and an active snapshot that differs from its own, **when** a manager tries to edit it, **then** the edit is refused with a `snapshot-mismatch` error. **Given** an assessment record and a command-line render against a different catalog commit, master list or registry, **when** the command runs, **then** it fails with the matching mismatch error, as it does today.

#### B34 — A saved assessment stays readable after a new snapshot

**Priority:** P1

**Given** a saved assessment, and a new snapshot activated since it was saved, **when** a reader opens or exports it, **then** they see its full saved content, including its leads, path steps and evidence excerpts, exactly as saved. Its evidence excerpts are every cited line range shown when its paths were traced: page lines (B25), code lines (B28) and ordinary-document lines (B38), each frozen when it is saved. Its exported Markdown prints page lines only, as B28 and B38 say. It is marked as pinned to an earlier snapshot.

#### B35 — A new assessment can start from an earlier one

**Priority:** P2

**Given** a saved assessment pinned to an earlier snapshot, **when** a manager starts a new assessment from it, **then** the new draft holds the same request id and description on the active snapshot, and nothing else. It links back to the earlier assessment.

#### B36 — A draft does not survive a snapshot change

**Priority:** P1

**Given** an unsaved draft in the browser, and a new snapshot activated since the draft began, **when** the reader takes their next action, **then** the action is refused, with a message that the snapshot changed and the draft can no longer be saved. The draft is discarded. A Markdown export of the draft taken before the change is unaffected.

## Quality attributes

| Id | Statement | Measure | Serves |
| --- | --- | --- | --- |
| Q1 | Search and path tracing respond quickly in the workspace. | On a fixture snapshot of 10,000 graph nodes and 50,000 assertions, the 95th percentile of 100 runs is at most 2 seconds for a search page and at most 5 seconds for paths of depth 4 from one start, measured at the API on the reference runtime container. | 4 |
| Q2 | Every assessment action needs sign-in, and every write needs the manage role. | Automated tests show that each assessment endpoint refuses unauthenticated calls, and each save or edit endpoint refuses readers without `ManageKnowledgeGraph`. | 4, 5 |
| Q3 | Results are deterministic. | Two runs with identical inputs give byte-identical search, paths and assessment JSON and Markdown, on the command line and in the workspace (B23). | 4, 6 |
| Q4 | Exported Markdown is safe. | Descriptions, reasons, labels and cited page text containing Markdown, HTML or line breaks render as inert text. A fixture with injected `<script>`, `|` and heading markup shows no structural change in the rendered export. | 4, 5 |
| Q5 | Saved assessments are durable. | After a workspace restart and after a snapshot activation, every saved assessment opens with content byte-identical to its saved form (B34). A save interrupted mid-write leaves no partial record. | 4, 6 |
| Q6 | The workspace assessment pages are accessible. | Automated axe checks on the search, paths, draft and saved-assessment pages report no serious or critical violations against WCAG 2.2 AA, and B15 to B18 can be completed by keyboard alone. | 4 |
| Q7 | Page text is not published. | A check over the built `dist/` and the unauthenticated API responses for the Confluence fixture finds none of its cited lines (B29). | 5 |

## Uncertainties

| # | Question | Decision or deferral | By |
| --- | --- | --- | --- |
| U1 | Who may save an assessment? | Readers search, trace, draft and export. Saving and editing need `ManageKnowledgeGraph`, as every other workspace write does (B17 to B19). Analysts who must save are given the manage role. | User, 26 Nov 2026 |
| U2 | How do paths behave before the master list (#205) exists? | Every assertion is followed from subject to object, as today, with one `no-relationship-master-list` gap (B9). Once a list exists, outcome 2 applies in full (B6 to B8). Outcome 2 therefore does not block the other outcomes. | User, 26 Nov 2026 |
| U3 | How does impact cross `exact` and `specializes` links? | `exact` in both directions. `specializes` only from the general term to its specialisations. Climbing is reported as a `specialization-boundary` gap (B11, B12). | User, 26 Nov 2026 |
| U4 | When is a lead supported? | When a C4 mapping backs it, or when at least one evidence record of any role cites it and verifies at the pinned commit (B1). On the C4 fixtures, "the same results as today" is read as the same leads, order, mapped-start paths, views and assessments, with the differences listed in B4 intended. **The product owner should confirm this reading.** B4 was later retired in favour of B37 (U16). | User, 26 Nov 2026 |
| U5 | What happens to assessments when a new snapshot is activated? | Saved assessments keep a frozen copy of everything they cite, and stay readable and exportable, but cannot be edited (B33, B34). A new assessment can start from one (B35). An unsaved draft is refused on its next action and discarded (B36). | User, 26 Nov 2026 |
| U6 | Must the workspace and command-line Markdown be identical? | Yes, byte-identical for the same snapshot, master list, registry and record (B23). That is testable and keeps one renderer contract. | Analyst's call |
| U7 | Is a relationship listed as carrying no impact reported as a gap? | No. The stewards decided its meaning. It is counted, not reported as a gap (B7). | Analyst's call |
| U8 | Does a reviewed-link crossing count towards the depth limit? | Yes, as one step, like a C4 binding today (B11). The depth limit stays 1 to 6, defaulting to 4. | Analyst's call |
| U9 | How is an unlinked boundary detected without comparing labels? | Any node on a path whose denoted term has no reviewed link reports `unlinked-term` (B13). Labels are never compared. | Analyst's call |
| U10 | Where do a page's title and Confluence address come from? | From the exported page file at the pinned commit. If either is missing, a `page-metadata-missing` gap is reported (B25, B26). How it is read is the architect's choice. | Analyst's call |
| U11 | Do saved assessments get deleted or expire? | Deferred, out of scope. Saved assessments are kept until a later feature defines deletion and retention. | Analyst's call |
| U12 | How large can a search result be? | Workspace search is cursor-paged at 100 leads (B15), and the list of saved assessments at 50 (B21). The command line keeps returning every lead. | Analyst's call |
| U13 | Can readers who are not managers see each other's drafts? | No. Drafts live only in the author's browser until a manager saves them (B17, B19). | Analyst's call |
| U14 | How do the workspace's evidence text and the bundle relate? | Architecture question, passed to the architect. Behaviour fixed by B25, B27 and B29. | Deferred to architect |
| U15 | Does an assessment need its own access control, beyond workspace roles? | Deferred, out of scope. Every reader of the workspace can open every saved assessment. | Analyst's call |
| U16 | B4 conflicted with B30, B5 and B3. What may differ on the C4 fixtures? | B4 is retired. B37 lists every allowed difference, including B30's gap sections for every assessment (schema 1 included), B3's reason text, `no-c4-mapping` and `unlinked-term`. Any other difference fails. | User, 27 Nov 2026 (pushback) |
| U17 | What makes a cited file an exported page? | A Markdown file cited by `selected-document` evidence that opens with a front-matter metadata block. Other cited files are ordinary documents, shown like code evidence, with no metadata gap (B38). | User, 27 Nov 2026 (pushback) |
| U18 | What does it mean for evidence to cite a node (B1)? | The node carries the evidence itself, or it is the subject or object of an assertion that carries it. Without the second case, nodes in a Confluence-shaped graph could never be supported, because only design records carry node evidence. | Analyst's call (pushback) |
| U19 | How often is `unlinked-term` reported? | Once for each source and denoted term in a result, naming the nodes it was met at. A node that denotes no term reports nothing (B13). | Analyst's call (pushback) |
| U20 | Does `no-c4-mapping` replace `unsupported-focus`? | Only for a focus with no mapping at all. A focus mapped at the wrong level keeps `unsupported-focus` (B5). | Analyst's call (pushback) |
| U21 | Can a reviewed-link crossing support a likely impact? It carries two reviewed links and a catalog commit, but no evidence IRI. | Yes. When the cited path's step arriving at the impacted node is a crossing, its links and catalog commit are the support, and no evidence IRI is needed (B39). The C4 fixtures have no reviewed links, so B37 is unaffected. | User, 28 Nov 2026 (pushback) |
| U22 | Which excerpts does a saved assessment keep? | Every cited excerpt shown during the trace, of pages, code and ordinary documents, is frozen and shown on the saved assessment in the signed-in workspace. The export still prints page lines only (B34, B28, B38, B29). | User, 28 Nov 2026 (pushback) |
| U23 | A record cites paths, never a search. Which search gaps does an assessment carry (B30)? | The gaps search reports about its confirmed and rejected leads, recomputed at its pinned snapshot. No search query is recorded, and the record is unchanged. Gaps every trace carries are kept even with no confirmed lead (B30). | User, 28 Nov 2026 (pushback) |
| U24 | How large can a trace be? On the Q1 fixture one trace finds 780 paths (2 MB) at depth 4, 3,905 (12 MB) at depth 5 and 19,530 (74 MB) at depth 6, against a 6 MiB response limit on AWS. | Every trace, on the command line and in the workspace, returns at most its first 500 paths, and reports `paths-truncated` when more exist (B40). Depths 1 to 6 stay. Serving larger payloads through signed S3 URLs, which could lift the cap, is parked as #234. The C4 fixtures find far fewer than 500 paths, so B37 is unaffected. | User, 28 Nov 2026 (pushback) |

## Terms

The following shared terms are proposed for `core/ontology.ttl` and division-steward review. Each is wider than any one source repository. The vision and these behaviours are their evidence. No existing IRI is changed, and no equivalence link is proposed.

- **ImpactLead** — a graph node or catalog concept returned by candidate search for a change request. It is identified by source and IRI and records how it matched. It is supported when a reviewed C4 mapping or verified evidence backs it. A lead is a starting point to confirm, never a conclusion about impact.
- **ImpactPath** — an ordered chain of steps from a supported ImpactLead chosen as a start. Each step follows a source assertion in the impact direction its relationship carries, a reviewed C4 binding, or a ReviewedLinkCrossing. Each step is shown with its evidence, and a path never stands as a direct assertion between its endpoints.
- **ReviewedLinkCrossing** — an ImpactPath step from a node in one source to a node in another. It is made through the terms they denote and the steward-reviewed links of those terms to a shared term. It is permitted for `exact` links in either direction, and for `specializes` links only from general to specific.
- **AssessmentGap** — a recorded limit on what a search, an ImpactPath trace or an ImpactAssessment could establish, with a code and a reason. Examples are an unlisted relationship, an unlinked term, a missing mapping, unavailable evidence text or a snapshot coverage gap. A gap never states that something is unaffected.

**Correction to an existing term.** `ImpactAssessment` is currently defined as *architect-authored*. Under outcome 4, analysts write assessments too. Its definition is corrected to *assessor-authored* (an architect or analyst), and it records the snapshot and the relationship master list it is pinned to. Its IRI and label are unchanged, and its meaning is otherwise the same.

**Correction to ImpactPath (amendment, 27 November 2026).** ImpactPath was defined as starting from a *confirmed* impact lead. B3 and B16 start paths from any supported lead that the assessor chooses, and confirming a lead is a separate step of the assessment (B17). The definition is corrected to *a supported impact lead chosen as a start*. Its IRI and label are unchanged.

`PublishedKnowledgeSnapshot`, `C4Diagram` and the Ontology Kit terms (`SourceKnowledgeGraph`, `GraphAssertion`, `GraphEvidence`, `GraphCoverage`) keep their meaning. The **relationship master list** and its impact directions belong to #205, which will define those terms.

## Retired behaviours

| Id | Was | Reason | Replaced by |
| --- | --- | --- | --- |
| B4 | C4 fixtures behave as today, apart from three listed differences. | Its list of differences conflicted with B30, B5 and B3's new reason text. Widening the list changes what the behaviour guarantees (U16). | B37 |
