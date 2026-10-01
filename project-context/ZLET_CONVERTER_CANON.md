# Zlet Converter Product Canon

Status: AUTHORITATIVE product contract.

## Product promise

Zlet Converter is a privacy-first, local-first document conversion product.

Primary flow:

`Document → Markdown → Quality Check`

Conversion remains useful on its own. Quality verification is provided by Zlet Quality Core, developed and versioned by Zlet Conversion Lab.

## Product boundary

Zlet Converter owns document ingestion, conversion, normalized Markdown output, product UX, packaging and Free/Paid/Paid API entitlement boundaries.

Zlet Converter **uses** Zlet Quality Core as a versioned dependency. It does not develop or fork Quality Core, metric formulas, comparators, SourceFacts semantics, coverage semantics or verdict semantics.

## Commercial capability boundary

### Free

Free includes full local-first Document → Markdown conversion plus a basic Quality Core-backed quality check. Unsupported or unevaluated dimensions remain explicit and must not be presented as proven correct.

### Paid

Paid adds advanced Quality Core-backed analysis, including deeper source ↔ target comparison, detailed findings/evidence where supported, explanations/recommendations, re-check workflows and batch reporting.

### Paid API

Paid API exposes advanced Quality Core-backed verification and automation/integration capabilities. It must use the same versioned Quality Core dependency and must not create a separate quality engine.

## Quality Core ownership

**Zlet Conversion Lab develops, validates, qualifies and versions Zlet Quality Core.**

**Zlet Converter consumes Zlet Quality Core as a dependency.**

Canonical dependency direction:

`Zlet Conversion Lab → Zlet Quality Core → Zlet Converter`

Quality Core includes the portable converter-agnostic evaluation implementation and its contracts: inspectors/SourceFacts, MarkdownFacts, comparators, metric/verdict semantics, coverage semantics and evidence contracts.

A semantic or implementation change to Quality Core is made and qualified in ZCL first. ZC adopts an explicit qualified version. ZC must not independently redefine or patch Quality Core semantics as product-local behavior.

Product packaging and entitlement decisions remain owned by ZC and do not change Quality Core semantics.

## Evaluation invariants

Preserve reproducible evaluation, immutable evidence, versioned methodology/contracts, stage-level failure attribution, explicit coverage/unknown states and dimension-level results. A single opaque composite score must not become the primary product truth.

## Product separation

Zlet Converter ends at conversion plus conversion-quality verification. Normalization/chunking/retrieval/RAG/answer-quality pipelines outside conversion fidelity belong to other Zlet products.

## Privacy

Document conversion and local quality checking are local-first. Source/output bytes, extracted text and document structure must not be silently sent to cloud services.

Private enterprise/X-IDBox material must never enter the public repository, public CI artifacts or public evidence.

## Authority and conflicts

This file is the authoritative long-lived product boundary for Zlet Converter.

GitHub Issues, PRs and implementation docs must conform to it. Older statements that Zlet Converter is permanently Free-only, has no Paid quality-analysis boundary, owns Quality Core development, or owns evaluation-methodology implementation are superseded by this canon.
