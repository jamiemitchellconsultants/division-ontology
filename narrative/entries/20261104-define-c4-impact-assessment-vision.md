---
date: 2026-11-04
slug: define-c4-impact-assessment-vision
title: "Define C4 impact assessment vision"
summary: "Adopt a vision in which architects use source-backed C4 views to make and explain impact assessments."
kind: product
status: accepted
sequence: 2026-11-04T12:26:32.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/39; merge commit bd32eacc400a0be4f62fe0895423f9cad6243b7f"
---

## Context

Architects need to assess change impact across systems and repositories. The division catalogue records source concepts and commits but does not yet turn architecture facts into C4 views. InternalOntology now publishes a versioned source knowledge graph with evidence and coverage; no participating repository must provide every C4 level. The product owner asked for architects as the first users and for a pilot that permits subjective feedback.

## Decision

Adopt a vision in which architects use source-backed C4 views to make and explain impact assessments. Support system context, container, component and code views when evidence exists, preserve provenance and uncertainty, and accept partial coverage without blocking source admission. Evaluate the first pilot through structured architect and reviewer feedback on perceived accuracy, completeness, suspected misses, false leads and confidence.

## Consequences

The analyst can define the user-visible impact-assessment behaviours and coverage states after this PR merges. The first pilot has no independently adjudicated reference set or zero-miss threshold. Graph authoring remains in source repositories; renderer, interaction design and detailed source contract remain open for later phases.
