# Agents Start Here Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `docs/agents-start-here.md` — the ordered, prompting-first instruction document an agent follows to create a division from this template — hooked from `AGENTS.md` and the README, owned in a copy by its stewards, and pinned by tests.

**Architecture:** One new document, two pointer-only hooks, one CODEOWNERS line, and assertions added to the two existing test files that already pin onboarding documents and scratch-copy initialisation. `scripts/init-division.mjs`, the catalogue builder, and every canonical rule document are unchanged; the new document links to them. Frontmatter for `feature.md` and ADR 0027 was already added on this plan branch (commit `9f75051`), so `node scripts/feature.mjs check-frontmatter --feature 189` passes before every build commit.

**Tech Stack:** Markdown; Node.js 20 built-in test runner (`node --test`); existing `scripts/init-division.mjs`.

**Spec:** `docs/features/agents-start-here/feature.md` (vision, behaviours B1–B16, uncertainties U1–U7, architect traceability) and `docs/adr/0027-agents-start-here-in-own-document.md`.

## Global Constraints

- `AGENTS.md` is canonical. Pointers in `AGENTS.md` and `README.md` name the new document and copy no rules from it or into it.
- Never edit `dist/`; do not change `scripts/init-division.mjs`, `src/`, `core/ontology.ttl`, `sources.json`, `links/`, or any workflow.
- The five optional setups and their declined defaults are exactly B6’s: workspace (catalogue-only), embedding provider when the workspace is chosen (no semantic search), C4 pilot (not set up), granted sources via the source-read App (steward-registered only), catalogue auto-merge (steward review).
- Answer states are exactly: accepted, declined, defaulted — not answered. Required choices (target owner/name, namespace, stewards, public-publication confirmation) are never defaulted.
- Similarity is described only as discovery for human judgement; the document must contain no instruction to create a link or equivalence from a match.
- No credentials in files, command arguments, or the setup record.
- Every task: run its focused test, run `node scripts/feature.mjs check-frontmatter --feature 189`, inspect `git diff --cached`, then commit with the task’s message and push.
- Tests run from the repo root with `npm test` (full suite) or `node --test test/<file>` (focused).

## Review Focus

- A missing required choice let through to creation — pinned by Task 1’s required-prompt assertions and Task 2’s stop wording.
- An optional prompt without its declined default, or an unanswered prompt recorded as chosen — pinned by Task 1’s default and state assertions.
- An administrator step reported complete instead of pending — pinned by Task 1’s pending-hand-off assertions and Task 2’s handover table.
- A secret value in the setup record or a written file — pinned by Task 2’s explicit “choices and pending steps only” record rule (review-checked; no automated secret scan exists).
- Pointer text drifting into copied rules — pinned by Task 1 asserting the pointers are single references, and by review of Task 3’s diff.

## Increments

This feature is one increment: five tasks, all documentation and tests, no deployable boundary inside it.

| Increment | Behaviour ids | Quality attributes to respect | Tasks |
| --- | --- | --- | --- |
| 1 | B1–B16 | none (the behaviours define no quality attribute ids) | Tasks 1–5 |

---

### Task 1: Pin the document contract in the onboarding-docs tests

**Files:**
- Modify: `test/onboarding-docs.test.mjs` (append new tests after the existing five)

**Interfaces:**
- Consumes: behaviour wording B5–B9, B13–B16 from the spec.
- Produces: failing tests that Tasks 2–3 turn green; test names listed in Task 4.

- [ ] **Step 1: Write the failing tests.** Append to `test/onboarding-docs.test.mjs`:

```js
const startHere = () => text('docs/agents-start-here.md');
test('agents start here asks for the required choices before creation', () => {
  const doc = startHere();
  for (const pattern of [/target owner\/name/i, /division namespace/i, /steward logins/i, /public repository/i]) assert.match(doc, pattern);
  assert.match(doc, /defaulted — not answered/);
});
test('agents start here prompts for each optional setup with its declined default', () => {
  const doc = startHere();
  for (const pattern of [/runtime knowledge workspace/i, /catalogue-only/, /embedding/i, /no semantic search/i, /C4/, /not set up/, /source-read App/, /steward-registered sources only/, /auto-merge/, /steward review/]) assert.match(doc, pattern);
});
test('agents start here records states, stops, and pending hand-offs', () => {
  const doc = startHere();
  for (const pattern of [/accepted/, /declined/, /pending/i, /who must/i]) assert.match(doc, pattern);
  assert.match(doc, /never.*(grant itself|credentials)/i);
});
test('agents start here points to the canonical homes and keeps similarity as discovery', () => {
  const doc = startHere();
  for (const pattern of [/AGENTS\.md/, /GOVERNANCE\.md/, /automation-setup\.md/, /05-runbook\.md/]) assert.match(doc, pattern);
  assert.match(doc, /discovery/i);
  assert.doesNotMatch(doc, /create a link from a (similarity )?match/i);
});
test('AGENTS and README point at agents start here', () => {
  assert.match(text('AGENTS.md'), /agents-start-here\.md/);
  assert.match(text('AGENTS.md'), /Create a division from this template/);
  assert.match(text('README.md'), /agents-start-here\.md/);
});
```

- [ ] **Step 2: Run to verify it fails.** `node --test test/onboarding-docs.test.mjs` — expected FAIL: `docs/agents-start-here.md` does not exist.
- [ ] **Step 3: Commit.** `git add test/onboarding-docs.test.mjs && git commit -m "test: pin the agents-start-here document contract"` and push.

### Task 2: Write the “Agents start here” document

**Files:**
- Create: `docs/agents-start-here.md`

**Interfaces:**
- Consumes: Task 1’s assertions; the section outline and exact prompt tables in `docs/detailed-implementations/agents-start-here/plan.md`.
- Produces: the document Tasks 3–4 reference by path; the setup-record table form.

- [ ] **Step 1: Write the document** with the detailed plan’s outline: title “Agents start here”; when to use it; 1 Prompt first (required table, optional table with declined defaults, answer states, setup-record table); 2 Create and clone (`gh repo create <owner>/<name> --template jamiemitchellconsultants/division-ontology --public --clone`, verified against `gh repo create --help` on gh 2.104, or GitHub’s **Use this template**); 3 Initialise once (`npm ci`, `npm run init-division -- --namespace <recorded> --steward <recorded>…`) and verify `division.json`, core, CODEOWNERS, empty Narrative, empty `sources.json`, rebuilt catalog; 4 Automation setup — link `docs/automation-setup.md`, accepted optional setups link their own canonical documents, administrator steps recorded pending with who must act; 5 First sync verification; 6 Handover — report the setup record, then point to `AGENTS.md`, `GOVERNANCE.md`, and the workspace runbook when chosen. State the stop conditions of B10–B12 and the discovery-only similarity wording of B14. Copy no rules from the linked documents.
- [ ] **Step 2: Run Task 1’s document tests.** `node --test test/onboarding-docs.test.mjs` — the four `agents start here …` document tests pass; the pointer test still fails (Task 3).
- [ ] **Step 3: Commit.** `git add docs/agents-start-here.md && git commit -m "docs: add Agents start here instructions"` and push.

### Task 3: Add the canonical pointers and CODEOWNERS entry

**Files:**
- Modify: `AGENTS.md` (one pointer paragraph at the top of “Short agent requests”, line 35)
- Modify: `README.md` (one bullet in “Minimal instructions for an agent”, line 22)
- Modify: `.github/CODEOWNERS` (one line after `/README.md`)

**Interfaces:**
- Consumes: `docs/agents-start-here.md` from Task 2.
- Produces: discoverability asserted by Task 1’s pointer test; the CODEOWNERS line Task 4 asserts on.

- [ ] **Step 1: Add the pointers.** In `AGENTS.md`, before the “Add `owner/repo`…” sentence, add: a person can say **“Create a division from this template”** — the agent follows [`docs/agents-start-here.md`](docs/agents-start-here.md), prompting for the required and optional setup choices there before creating anything. In `README.md`, add the bullet: `- **Create a division from this template** — the agent follows [Agents start here](docs/agents-start-here.md), prompting for setup choices before creating the repository.` Add `/docs/agents-start-here.md @jamiemitchellconsultants @james-mitchell-ba` to `.github/CODEOWNERS`.
- [ ] **Step 2: Run the full onboarding-docs file.** `node --test test/onboarding-docs.test.mjs` — all tests pass, including the pointer test.
- [ ] **Step 3: Commit.** `git add AGENTS.md README.md .github/CODEOWNERS && git commit -m "docs: point AGENTS and README at Agents start here"` and push.

### Task 4: Pin survival and steward ownership in the init-division tests

**Files:**
- Modify: `test/init-division.test.mjs` (inside the existing “a fresh copy gets its own namespace…” test, after the `05-runbook.md` assertion)

**Interfaces:**
- Consumes: `docs/agents-start-here.md` (Task 2) and its CODEOWNERS line (Task 3); the scratch-copy helper `templateCopy()` already copies `docs/` and `.github/CODEOWNERS`.
- Produces: regression proof for B15–B16.

- [ ] **Step 1: Write the assertions:**

```js
    assert.ok(existsSync(join(root, 'docs/agents-start-here.md')), 'agents start here survives initialisation');
    const agentsLine = readFileSync(join(root, '.github/CODEOWNERS'), 'utf8').split('\n').find((l) => l.startsWith('/docs/agents-start-here.md'));
    assert.match(agentsLine ?? '', / @sales-steward @sales-deputy$/);
```

- [ ] **Step 2: Run it.** `node --test test/init-division.test.mjs` — passes. To prove the assertions bite, temporarily rename `docs/agents-start-here.md`, re-run and see the first assertion fail, then restore the file before committing.
- [ ] **Step 3: Commit.** `git add test/init-division.test.mjs && git commit -m "test: pin agents-start-here survival in a division copy"` and push.

### Task 5: Verify the whole feature

**Files:** none (verification only; fix and re-commit under the owning task if anything fails)

- [ ] **Step 1:** `npm test` — all tests pass.
- [ ] **Step 2:** `node scripts/feature.mjs check-frontmatter --feature 189` — “frontmatter: ok”.
- [ ] **Step 3:** Follow `docs/agents-start-here.md` against a scratch copy under `$TMPDIR` (create from the working tree, run its initialisation and verification steps with invented namespace/stewards), confirming B3’s checks can all be performed from the document alone; record any gap as a Task 2 fix, not as acceptance evidence. Delete the scratch copy.
- [ ] **Step 4:** Confirm `git status` is clean and every task’s commit is pushed. No commit for this task unless a fix was needed.
