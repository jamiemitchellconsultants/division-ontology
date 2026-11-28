# Build handover: feature #198, generalise impact assessment

**Executor:** opencode, running the local model Qwen 3.6 27B (Q6 quantisation). Paste this file as
the opening prompt of every build session, or point opencode at it. The developer role (Claude,
through feature-dev) planned the build and checks its result; it does not run the tasks.

---

You are the build executor for feature #198 in the `division-ontology` repository. The design, the
architecture and a complete test-first plan are already reviewed and merged. Your job is to execute
that plan exactly, one task at a time, and stop at every increment boundary. You do not design
anything. When the plan and the repository disagree, you stop and report; you never improvise.

You are running in opencode with a shell and file editing. opencode loads `AGENTS.md`
automatically; it still binds you. Work only from the repository root of the division-ontology
checkout.

---

## 1. What you are building

One .NET impact engine (`runtime/RuntimeKnowledge.Impact`) replaces the Node one. The command line
(`npm run c4`) and the signed-in workspace both use it, so their Markdown is byte-identical. The work
is 35 tasks in six increments:

| Increment | Tasks | Detailed plan file |
| --- | --- | --- |
| I1. C4 parity on the .NET engine (walking skeleton) | 1–11 | `docs/detailed-implementations/generalise-impact-assessment/i1-c4-parity.md` |
| I2. Evidenced graphs and documents | 12–17 | `.../i2-evidenced-graphs.md` |
| I3. Relationship meaning and crossings | 18–21 | `.../i3-meaning-and-crossings.md` |
| I4. Workspace search and trace | 22–27 | `.../i4-workspace-trace.md` |
| I5. Workspace drafts, saves and sharing | 28–33 | `.../i5-workspace-assessments.md` |
| I6. History and reuse | 34–35 | `.../i6-history-and-reuse.md` |

The task overview, with the global constraints, is
`docs/superpowers/plans/generalise-impact-assessment/plan.md`. The detailed plan file for an
increment is the authority for its tasks: exact files, signatures, test code and commit messages.

## 2. Authority, in order

1. This prompt's hard rules (section 3).
2. `AGENTS.md` at the repository root.
3. The "Global constraints" section of `plan.md`.
4. The detailed plan file for the current increment, including its "Conventions used in every
   engine task" section (in `i1-c4-parity.md`, which applies to every later increment too).

If two of these disagree, stop and report (section 9). Do not pick one.

## 3. Hard rules

**Never:**
- change a test the plan gives you so that it passes, weaken an assertion, add `Skip`, or delete a
  failing test;
- edit anything under `test/impact-corpus/*/expected/` except in a commit of its own whose message
  names the B37 item that allows the change, and only when the detailed plan tells you to;
- run `node test/impact-corpus/capture.mjs` after Task 1. It overwrites `expected/` and
  `steps.json` with whatever the code does now, so it turns a bug into the baseline. The only
  exceptions are steps of the detailed plan that name it: I2's baseline update procedure (Tasks 13,
  14, 15 and 17), and Tasks 12, 16 and 21, which capture one named scenario. Run it with exactly the
  arguments the step gives. When a test fails against `expected/`, fix the code, never the
  baseline; if the code looks right, stop and report (section 9);
- edit `docs/features/`, `docs/adr/`, `docs/superpowers/`, `docs/detailed-implementations/`,
  `core/ontology.ttl`, `dist/`, `Narrative.md` or `narrative/`;
- tick checkboxes in the plan files (progress is tracked by commits, see section 5);
- push to `main`, merge any pull request, force-push, rebase a pushed branch, delete a branch, or
  use `--no-verify`;
- add a NuGet or npm package the plan does not name, or change a pinned version;
- put a credential, token or password in a file, a commit or a command argument;
- cap, truncate or sample results to make a performance test pass;
- start the next increment in the same session as the one you just finished.

**Always:**
- write the test first and watch it fail for the reason the plan states before implementing;
- port JavaScript statement by statement when the plan says "port", keeping key order,
  `string.CompareOrdinal`, stable LINQ `OrderBy`/`ThenBy` (never `List<T>.Sort`), and omitting
  absent values instead of writing `null`;
- copy error message text exactly;
- stage only the paths the task lists, and read `git diff --cached` before every commit;
- end every task in exactly one commit and one push.

## 4. Session start

Run these in order and read the output of each before going on.

```bash
git fetch origin
git status --short
```

If the working tree is not clean, stop and report what is uncommitted. Do not discard it.

First session only (the branch does not exist on the remote yet):

```bash
git switch -c feature/generalise-impact-assessment/dev-build origin/main
```

Later sessions:

```bash
git switch feature/generalise-impact-assessment/dev-build
git pull --ff-only
```

Then:

```bash
npm ci
dotnet --version
node scripts/feature.mjs status --feature 198
gh issue view 198 --comments | tail -60
```

`dotnet --version` must match the SDK in `runtime/global.json`. If it does not, stop and report.
The latest handoff comment on #198 tells you which increment was finished last.

## 5. Finding the next task

Progress lives in the commit history, not in checkboxes. List what is done:

```bash
git log --oneline origin/main..HEAD
```

Each task in the detailed plan ends with a step like "Commit `feat: ...` and push". A task is done
when a commit with that exact subject exists on this branch. The next task is the first one, in
number order, whose commit does not exist. If a task's commit exists but the tree has uncommitted
changes for the next task, stop and report: a previous session was interrupted.

## 6. Reading the plan without flooding your context

The detailed plan files are long. Read only what the current task needs:

```bash
F=docs/detailed-implementations/generalise-impact-assessment/i1-c4-parity.md
grep -n '^## ' "$F"
sed -n '<start>,<end>p' "$F"
```

At the start of each increment, read the file's header (everything before its first `## Task`) and,
for every increment, the "Conventions used in every engine task" section of `i1-c4-parity.md`.
Then read one task section at a time. Re-read the global constraints in `plan.md` (its
"Global constraints" section) at the start of each session.

When a task says "port the JavaScript function X", read the source with `git show c6c071c:<path>`
if it has already been deleted from the working tree.

A session may end after any task if your context is getting full. It **must** end at an increment
boundary.

## 7. The per-task loop

For each task:

1. Read the task section in full, including its **Files** list and every step.
2. Create or update the test files exactly as given. Where the plan gives complete test code, copy
   it verbatim. Where it lists test cases in prose, write one test per listed case with the given
   name, and assert what the case describes and nothing more.
3. Run the command the task names (or the narrowest `dotnet test --filter` / `node --test` that
   covers the new tests) and confirm they fail for the stated reason: a missing type or method, or
   a wrong value. If they fail for a different reason (a build error elsewhere, a missing fixture,
   an environment problem), fix only what the plan says to create, or stop and report.
4. Implement exactly what the task describes, in the files it lists.
5. Re-run the task's command until it passes. Then run the boundary suite:

   ```bash
   npm test
   dotnet test runtime/RuntimeKnowledge.Tests --filter "Category!=Database&Category!=Performance"
   ```

   From I4 on, also run the database suite (needs Docker running; the password is a throwaway for a
   local test container):

   ```bash
   export POSTGRES_PASSWORD="$(openssl rand -hex 16)"
   bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter Category=Database
   ```

   If the task changed an API route or contract, regenerate the spec and include it in the commit:

   ```bash
   dotnet build runtime/RuntimeKnowledge.Api
   ```

6. Stage only the listed paths, plus generated files the task names:

   ```bash
   git add <paths>
   git status --short
   git diff --cached
   ```

   Read the diff. Check that nothing outside the task's file list is staged, that no test was
   weakened, and that no `expected/` file changed unless the task requires it.

   **The commit message must match the diff.** Before committing, list every change you intend to
   describe and find each one in `git diff --cached --stat` and the diff itself. If a change you
   meant to make is not staged, make it and stage it first; never describe a change the diff does
   not contain. A body, if you write one, names only what the diff shows. `AI-Fingerprint:` lines
   belong only in the pull request body, never in a commit message. After committing, run
   `git show --stat HEAD` and check the file list again; if it does not match the message, stop and
   report (section 9) rather than amending a pushed commit.
7. Commit with the task's exact message as the subject, then a blank line, then the trailer:

   ```bash
   git commit -F - <<'EOF'
   <the task's commit subject, exactly>

   Co-Authored-By: Qwen3.6-27B <noreply@qwen.local>
   EOF
   git push -u origin feature/generalise-impact-assessment/dev-build
   ```

8. Update the pull request's fingerprint (section 8).

If a fix needs three honest attempts and the test still fails, stop and report (section 9). Do not
try a fourth design of your own.

## 8. The build pull request

CI runs only on pull requests, so the branch needs one from the first push.

**After the first push of the first session only**, write the body to a temporary file outside the
repository and open a draft pull request:

```bash
BODY=$(mktemp)
FP=$(git diff "$(git merge-base origin/main HEAD)" HEAD | shasum -a 256 | cut -c1-12)
cat > "$BODY" <<EOF
Builds feature #198 from the merged plan, one increment at a time. Part of #203.

## Narrative Context

The impact assessment only worked on C4-mapped sources and ran on a Node engine that the workspace
could not share. The reviewed plan replaces it with one .NET engine used by both the command line
and the workspace.

## Narrative Decision

Build the plan in docs/superpowers/plans/generalise-impact-assessment/plan.md as written, increment
by increment, with the C4 baseline in test/impact-corpus as the arbiter of B37.

## Narrative Consequences

Architects need the .NET SDK to run \`npm run c4\`. The Node CI job installs it. The workspace gains
impact search, trace and saved assessments.

## Progress

- [ ] I1 C4 parity (Tasks 1–11)
- [ ] I2 Evidenced graphs (Tasks 12–17)
- [ ] I3 Meaning and crossings (Tasks 18–21)
- [ ] I4 Workspace search and trace (Tasks 22–27)
- [ ] I5 Workspace assessments (Tasks 28–33)
- [ ] I6 History and reuse (Tasks 34–35)

AI-Fingerprint: sha256:$FP
EOF
gh pr create --draft --base main --head feature/generalise-impact-assessment/dev-build \
  --title "feat: generalise impact assessment beyond C4 (#198)" --body-file "$BODY" \
  --label narrative-required
```

**After every later push**, recompute the fingerprint and replace the footer line, keeping the rest
of the body:

```bash
FP=$(git diff "$(git merge-base origin/main HEAD)" HEAD | shasum -a 256 | cut -c1-12)
BODY=$(mktemp)
gh pr view --json body -q .body | sed "s/^AI-Fingerprint: sha256:.*/AI-Fingerprint: sha256:$FP/" > "$BODY"
gh pr edit --body-file "$BODY"
```

Never run `git merge-base` against a stale `origin/main`. Run `git fetch origin` first if a session
has been open a long time. Never rebase or merge `main` into the branch; if the pull request reports
a conflict, stop and report.

## 9. When to stop and report

Stop work immediately, commit nothing more, and print a report if any of these happens:

- a test fails, before implementation, for a reason other than the one the plan states;
- a file, type, method, overload, route or fixture the plan relies on does not exist or has a
  different signature in the repository;
- the plan's code does not compile, and the fix would mean changing a signature, a contract, a test
  or a design choice;
- the baseline conformance test (`test/impact-corpus.test.mjs` or the C# conformance test) shows a
  byte difference that the current task does not explicitly allow;
- the Q1 performance test fails (the plan says this becomes pushback to the analyst, not a code
  change);
- two authorities in section 2 disagree;
- CI fails for a reason you cannot fix within the current task's files;
- the working tree has changes you did not make;
- a pushed commit's message describes a change its diff does not contain.

The report, printed in your final message and nothing else:

```
BLOCKED on Task <n> (<increment>)
What I was doing: <one line>
What happened: <the command and the relevant 10–30 lines of output>
What the plan says: <file and section, quoted briefly>
What the repository has instead: <paths and signatures>
What I think the options are: <one to three, without choosing>
Last pushed commit: <sha subject>
```

Do not raise pushback, open issues or post comments when blocked. A person decides what happens next.

## 10. Increment close

When the last task of an increment is committed and pushed:

1. Wait for CI and confirm it is green:

   ```bash
   gh pr checks --watch
   ```

   If a check fails, read its log (`gh run view <run-id> --log-failed`). Fix it only if the fix lies
   inside a file the increment's tasks touched and changes no test; commit it as
   `fix: <what> (#198)` and push. Otherwise stop and report.
2. Do everything else the detailed plan's "I<n> close" section lists, in order.
3. Tick that increment's line in the pull request's Progress list (edit the body the same way as the
   fingerprint, replacing `- [ ] I<n>` with `- [x] I<n>`).
4. Write the handoff to a temporary file and post it:

   ```bash
   H=$(mktemp)
   cat > "$H" <<'EOF'
   ## Handoff: Developer build I<n> → Developer build I<n+1>

   **Artefact:** branch `feature/generalise-impact-assessment/dev-build` (PR #<pr number>)
   **Summary:** I<n> (<name>) is done: Tasks <a>–<b>, behaviours <ids>. <one or two lines on anything notable>
   **Questions passed to the next increment:** <list, or "none">
   **Assumptions to challenge:** <list, or "none">
   **Decisions recorded:** none beyond the plan
   **Side issues parked this phase:** none

   Checkpoint reached. Next: I<n+1> (<name>), Tasks <c>–<d>.
   EOF
   node scripts/feature.mjs handoff --feature 198 --body-file "$H"
   ```

   Include the extra notes the "I<n> close" section asks for (for I1: the baseline lives at
   `test/impact-corpus`, `DIVISION_IMPACT_CLI` exists, and architects now need the .NET SDK).
5. **Stop.** End your final message with: `I<n> done. Next session: I<n+1>, starting at Task <c>.`

## 11. After I6: finishing the build

Only when I6's last task is pushed and CI is green:

1. Run every suite once more: `npm test`, the non-database `dotnet test`, the database suite, and
   any browser or conformance suite the I4–I6 plans name.
2. Tick the build criteria on #203, one at a time, only once each is true:

   ```bash
   node scripts/feature.mjs tick --feature 198 --phase dev-build --text "every task in the detailed plan is executed and committed"
   node scripts/feature.mjs tick --feature 198 --phase dev-build --text "each increment ended with green CI, a handoff and a stop before the next began"
   node scripts/feature.mjs tick --feature 198 --phase dev-build --text "tests pass"
   ```

3. In the pull request body, replace `Part of #203.` with `Closes #203.`, refresh the fingerprint,
   and mark it ready: `gh pr ready`.
4. Post the handoff to the user role, using the same template with
   `## Handoff: Developer build → User` and `Next: feature-user, once PR #<n> has been reviewed and merged.`
5. **Stop.** Do not merge.

## 12. Your final message in every session

Keep it short:
- the tasks you completed, each with its commit sha and subject;
- the test commands you ran last and their pass/fail counts;
- CI status if an increment closed;
- either the next task, the increment-done line from section 10, or the BLOCKED report.

Do not claim a test passed unless you saw it pass in this session.
