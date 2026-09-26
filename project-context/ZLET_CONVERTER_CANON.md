# Zlet Converter Product Canon

Status: AUTHORITATIVE product contract.

## Product promise

Zlet Converter is a privacy-first, local-first document conversion product.

Primary flow:

`Document → Markdown → Quality Check`

Conversion remains useful on its own. Quality verification is layered on the same app-owned conversion/verification core and must not create a second conversion engine.

## Product boundary

Zlet Converter owns:

- document ingestion and format/capability routing;
- conversion adapters/workers and normalized Markdown output;
- conversion diagnostics and product UX;
- integration of a qualified, versioned quality-evaluation contract from Zlet Conversion Lab;
- product packaging and entitlement boundaries for Free, Paid and Paid API capabilities.

Zlet Converter does **not** own the evaluation methodology itself. Metric definitions, source facts, comparison semantics, controlled-defect validation, corpus methodology and evaluation evidence are owned and validated by Zlet Conversion Lab.

## Commercial capability boundary

### Free

Free includes:

- full local-first Document → Markdown conversion without intentional quality degradation;
- basic automatic quality checking;
- clear indication of detected risks/anomalies and unsupported or unevaluated dimensions;
- safety and conversion diagnostics.

Free must not claim absolute correctness when a dimension was not evaluated. `UNKNOWN` / `NOT_EVALUATED` remains explicit.

### Paid

Paid adds advanced quality analysis, including capabilities such as:

- deeper source ↔ target comparison;
- detailed findings and affected dimensions;
- evidence/location where supported;
- explanations and recommendations;
- evidence-backed retry/re-check workflows where supported;
- batch triage and reporting;
- independent verification of externally produced Markdown/structured conversion results.

Exact packaging, tier name and price may evolve. The architectural boundary is basic quality check vs advanced quality analysis.

### Paid API

Paid API exposes advanced quality/verification and automation/integration capabilities through the same app-owned core.

The API must not become a separate conversion or quality engine and must not fork routing, metrics or verdict semantics.

## Quality methodology authority

Zlet Conversion Lab is the authoritative owner of the evaluation methodology.

Expected integration direction:

`Zlet Conversion Lab methodology → qualified/versioned Quality Core contract → Zlet Converter product integration`

Converter must consume a pinned/versioned qualified contract or artifact. It must not independently redefine metric formulas or evaluation semantics.

A change to quality semantics requires validation in Conversion Lab before Converter treats the new version as qualified.

## Evaluation invariants

Preserve:

- source → conversion output comparison;
- reproducible evaluation;
- immutable evidence;
- versioned methodology/contracts;
- stage-level failure attribution;
- explicit coverage and unavailable/unknown states;
- dimension-level results rather than an opaque global score.

A single composite score must not become the primary product truth.

## Product separation

Zlet Converter ends at conversion plus conversion-quality verification.

Normalization/chunking/retrieval/RAG/answer-quality pipelines outside conversion fidelity belong to other Zlet products, not Converter.

Production data-preparation transformations are not silently folded into quality evaluation.

## Privacy

Document conversion and local quality checking are local-first. Source/output bytes, extracted text and document structure must not be silently sent to cloud services.

Private enterprise/X-IDBox material must never enter the public repository, public CI artifacts or public evidence.

## Authority and conflicts

This file is the authoritative long-lived product boundary for Zlet Converter.

GitHub Issues, PRs and implementation docs must conform to it. Older statements that Zlet Converter is permanently Free-only, has no Paid quality-analysis boundary, or assigns evaluation-methodology ownership to Converter are superseded by this canon.

Fast-changing implementation state, branches, active Issues/PRs, engine versions and release status should be recorded separately and must not silently redefine this product contract.
