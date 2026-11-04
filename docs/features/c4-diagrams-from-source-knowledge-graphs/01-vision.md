# C4 diagrams from source knowledge graphs — product vision

**Feature:** [#7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7)

**Phase:** [#16 — Product owner vision](https://github.com/jamiemitchellconsultants/division-ontology/issues/16)

**Status:** Accepted by the product owner on 4 November 2026; pending review and merge of the product-owner phase pull request.

## Problem

Architects reviewing change requests must assess impact across a complex ecosystem of systems, dependencies and code owned by different repositories. Today the division catalogue preserves source-owned ontology concepts and provenance, but it does not turn architectural facts into C4 views that help an architect trace a proposed change from system context down to relevant code. Reconstructing that picture by hand makes it easy to miss an affected system or mistake missing knowledge for no impact. A source may publish useful facts without enough evidence for every C4 level; that partial contribution should remain usable without concealing the gaps. An unjoined repository should not appear as a participating source.

## Users

- **Architect (initial user):** reviews a change request and uses the views to identify affected parts of the participating ecosystem, check dependencies, and produce an evidence-based impact assessment.
- **Developer:** uses narrower views and cited source evidence to verify the implementation and dependencies named in an assessment.
- **Source maintainer:** reviews whether the source-owned graph and its declared gaps accurately describe the repository.
- **Division ontology reader:** uses diagrams to explore the published, cross-repository architecture at a known set of source revisions. This includes AI agents consuming a view, provided they can inspect its evidence and uncertainty.

## Outcomes and measures

1. **Architects make credible impact assessments.** For representative change requests, an architect can identify likely affected systems, containers, components and code in participating sources, explain the paths by which the change may affect them, and state where evidence is insufficient. Measure: collect structured feedback from architects and reviewers on the assessment's perceived accuracy, completeness, useful discoveries, suspected missed impacts, false leads and confidence. Record examples and unresolved questions. Initial success is their judgement that the views improve the assessment; a reference set and numeric accuracy threshold can be considered after the pilot.
2. **The four C4 levels help an architect investigate impact.** Given reviewed facts for each level, a user can move from system context through containers and components to important code, following relevant relationships and boundaries. Measure: separate walkthroughs at all four levels let an architect explain the suspected impact path for a change request without inventing an element or edge. One repository need not cover all four levels.
3. **Assessments are evidence-based and reproducible.** Every source-derived element and relationship can be traced to its participating repository, recorded commit and underlying assertion or inspected source. Direct observations and tentative inferences remain distinguishable. Measure: an independent reviewer can follow each claim in a pilot assessment to its evidence, and repeated requests at the same recorded revisions yield the same supported claims. Matching names alone never establish equivalence.
4. **Missing knowledge does not become a false assurance of no impact.** A user can see which levels and areas a participating source supports, which are incomplete or unavailable, and why. Measure: pilot cases with partial or unknown evidence make the assessment explicitly unresolved in those areas and identify the follow-up needed; available facts remain usable. An unjoined repository has no participating-source coverage status, and missing C4 coverage does not block its admission.

## Scope

- Support architects reviewing change requests and producing impact assessments across repositories already participating in the division catalogue, using C4 system context, container, component and code views from available, versioned, source-owned architecture facts.
- Use the source knowledge graph's recorded assertions, evidence, coverage and revision. Reading source code at that recorded commit is acceptable where a reliable code-level graph projection is impractical.
- Preserve source ownership and provenance. Make missing levels and unsupported relationships visible rather than filling them in by assumption.
- Support sources with partial C4 coverage. Keep C4 coverage independent of source onboarding in [feature #6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6).
- Allow evidenced cross-repository context from participating sources without requiring every repository to describe the same depth.
- Evaluate representative C#, TypeScript, Java and Python sources. For the JointBooking pilot, only Terraform counts as deployment evidence.

## Out of scope

- Requiring all four levels before a repository can join, or displaying an unjoined repository as a participating source.
- Automatically approving or deciding the impact of a change request. The architect remains responsible for the assessment and for investigating gaps.
- Requiring an independently adjudicated impact reference or a zero-miss threshold in the first pilot. The first evaluation includes subjective architect and reviewer feedback.
- Authoring or validating a source repository's knowledge graph in this repository; the [Ontology Kit source graph feature](https://github.com/jamiemitchellconsultants/InternalOntology/issues/35) owns that upstream work.
- Automatically asserting equivalence, ownership, boundaries or relationships from similar names or plausible code patterns without reviewable evidence.
- Adopting Graphify, its Claude skill or a hosted graph service as a product dependency. The JointBooking Graphify spike was research, not an accepted source contract.
- Implementing the separate runtime contribution workspace in [feature #25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25), or the C4 model's additional landscape, dynamic and deployment diagram types.
- Choosing a diagram renderer, interaction design, graph schema or precise source contract in the product-owner phase.

## Research

- The [official C4 model](https://c4model.com/) defines four hierarchical abstractions and four corresponding diagram levels. Its [system context](https://c4model.com/diagrams/system-context), [container](https://c4model.com/diagrams/container), [component](https://c4model.com/diagrams/component) and [code](https://c4model.com/diagrams/code) guidance distinguishes each view's scope and audience. It treats code diagrams as optional and best generated on demand; this feature nevertheless supports that level when a user needs it and evidence exists.
- The [division catalogue](https://github.com/jamiemitchellconsultants/division-ontology/blob/main/README.md) currently publishes source concepts and source commits in generated JSON and N-Quads. Its [governance](https://github.com/jamiemitchellconsultants/division-ontology/blob/main/GOVERNANCE.md) keeps source definitions authoritative and forbids inferring equivalence from labels. The diagram feature should keep that provenance and review discipline.
- The accepted [InternalOntology source graph vision](https://github.com/jamiemitchellconsultants/InternalOntology/blob/main/docs/features/source-knowledge-graph/01-vision.md) says AI agent grounding is the upstream graph's primary outcome and C4 is a downstream view. Its [user guide](https://github.com/jamiemitchellconsultants/InternalOntology/blob/main/docs/features/source-knowledge-graph/05-user-guide.md) and [acceptance record](https://github.com/jamiemitchellconsultants/InternalOntology/blob/main/docs/features/source-knowledge-graph/05-acceptance.md) show a pinned Turtle graph with evidence, direct or inferred basis, and covered, unknown and excluded areas; representative C#, TypeScript, Java and Python trials passed. This establishes a source to consume, not proof that a real participating repository already provides every C4 fact.
- Earlier research for the upstream feature found useful file and line evidence in a JointBooking Graphify spike, alongside unreliable paths through test helpers and missing Terraform extraction. The upstream [vision](https://github.com/jamiemitchellconsultants/InternalOntology/blob/main/docs/features/source-knowledge-graph/01-vision.md) records the decision to keep Graphify out of the product dependency set and to use Terraform alone for JointBooking deployment evidence.
- Product-owner steering on 4 November 2026 named architects as the initial users and accurate impact assessment of change requests across a complex ecosystem as the success outcome. The owner then asked for a looser initial evaluation that permits subjective feedback. The [runtime knowledge workspace vision](https://github.com/jamiemitchellconsultants/division-ontology/blob/main/docs/features/runtime-knowledge-workspace/01-vision.md) independently names architects as its initial discovery audience and leaves C4 generation to this feature.
