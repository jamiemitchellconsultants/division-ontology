# I7. Review amendments

Tasks 36 to 42 answer the PR #220 review. The behaviours were amended in PR #228 (B39; B17, B30 and B34 refined) and the architecture in PR #230 (ADRs 0035 and 0036). The task list, with each task's tests and commit, is in [the plan](../../superpowers/plans/generalise-impact-assessment/plan.md#i7-review-amendments). The rules each task builds are stated in full in `02-behaviours.md`, `03-architecture.md` and the two ADRs, so this file adds only what those leave to the build.

## Interfaces

- **`traces` (ADR 0035).** In a schema 2 record: `"traces": [{"starts": [{"sourceId", "nodeIri"}], "maxDepth": 1..6}]`, at most 5 entries (ADR 0035, amended in PR #231), each with 1 to 20 starts. Absent means `[{"starts": confirmedLeads, "maxDepth": 4}]`, and nothing is traced when there are no confirmed leads. Anything else is refused with `invalid traces`, which the API reports as `invalidAssessment`.
- **`Assessments.CitablePaths(ImpactModel, JsonObject record)`** returns the union of the paths of every trace, in trace order, each path once by id. The API's freeze step and the command line's `assessment-render` and `feedback-render` all use it.
- **B39 support.** A likely impact with an empty `evidenceIris` is valid when one of its cited paths has a `reviewedLinkCrossing` step whose `to` is the impact's key. The Markdown line for such an impact adds `; reviewed links through <shared term> at catalog <commit>`.
- **Excerpts (B34).** Every frozen evidence record gets `lines` when its text can be read, whatever its role, and an `evidence-text-unavailable` gap when it cannot (B27). Markdown still prints `lines` only under a page.
- **Lead gaps (B30).** For each confirmed and rejected lead, the page-evidence gaps search reports for it are added to the frozen evidence gaps, without duplicates.
- **Model repair (ADR 0036).** `ImpactModels.StageAsync` inserts only when no row exists. Staging calls it even when the snapshot's projection already exists. Activation reads the bundle and calls it when the snapshot's model row is missing.
