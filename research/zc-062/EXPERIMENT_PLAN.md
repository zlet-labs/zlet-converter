# ZC-062 — Quality Core checker benchmark

Issue: #123
Parent research: #89

## Research question

Can deterministic source-vs-Markdown and output-health checks detect known conversion defects with useful precision while explicitly representing dimensions that were not evaluated?

This experiment validates the checker, not a converter winner.

## Frozen baseline

- Repository baseline: `a8a98db40aad8cb8833c3e60976dab3df62f4c6e`
- Branch: `research/zc-062-quality-checker-benchmark`
- Corpus: existing public/synthetic Zlet Converter fixtures first
- Private enterprise/X-IDBox inputs: excluded from committed evidence
- Cloud/LLM/VLM processing: excluded

Every actual run MUST record exact runtime/dependency versions, OS, input hashes, converter engine/version/config, Markdown output hashes, checker tooling commit and evidence paths.

## Quality dimensions

Results are recorded independently for:

- Content
- Structure
- Order
- Integrity
- Evaluation Coverage

A dimension that cannot be established from independent evidence MUST be `NOT_EVALUATED` / `UNKNOWN`; absence of a check is never success.

## Initial check matrix

| Check ID | Class | Dimension | Positive defect | Negative/control |
| --- | --- | --- | --- | --- |
| QC-EMPTY-001 | deterministic output-only | Content | empty / whitespace-only Markdown | non-empty valid Markdown |
| QC-SHORT-001 | source-vs-output | Content | material source text missing | source/output with expected retained text |
| QC-DUP-001 | deterministic output-only + source evidence where available | Integrity | repeated block not supported by source | intentional source repetition |
| QC-HEAD-001 | source-vs-output | Structure | heading level/text loss or hierarchy degradation | preserved heading hierarchy |
| QC-LIST-001 | source-vs-output | Structure | list items flattened/lost | preserved list structure |
| QC-TABLE-001 | source-vs-output | Structure / Content | missing rows/cells or malformed table | preserved table facts |
| QC-MARKUP-001 | deterministic output-only | Integrity | malformed Markdown / leaked HTML fallback artifact | valid intended Markdown/HTML |
| QC-STABLE-001 | deterministic repeatability | Integrity | same version/config/input produces different normalized output | identical repeated result |
| QC-COVER-001 | deterministic applicability | Evaluation Coverage | unsupported/uninspectable source semantics | independently inspectable source facts |

The first run may narrow the matrix if existing fixtures cannot establish a valid positive/negative pair. Such a check becomes `BLOCKED` or `UNSUPPORTED`, not guessed.

## Fixture requirements

Each investigated check needs:

1. positive fixture with a known defect or controlled mutation;
2. negative/control fixture that should not trigger;
3. immutable source hash;
4. expected facts independent from the converter under test;
5. expected finding;
6. actual finding;
7. false-positive and false-negative observation.

Prefer existing synthetic fixtures. Add minimal synthetic mutations only for missing pairs.

## Result semantics

Document-level status:

- `OK`: no material problem detected by checks actually performed;
- `NEEDS_REVIEW`: explainable risk/fidelity finding requires inspection;
- `FAILED`: conversion unusable or a hard validation rule proves it cannot be accepted;
- unevaluated dimensions remain explicit in Evaluation Coverage.

Per-check evidence review status:

- `PASS`
- `PARTIAL`
- `FAIL`
- `UNSUPPORTED`
- `BLOCKED`

No aggregate score may replace dimension evidence or hard gates.

## Machine-readable finding contract

Each finding should minimally preserve:

```json
{
  "check_id": "QC-TABLE-001",
  "check_version": 1,
  "classification": "deterministic_source_vs_output",
  "dimension": "Structure",
  "status": "NEEDS_REVIEW",
  "applicability": "EVALUATED",
  "summary": "Expected table facts were not preserved",
  "evidence": [],
  "source_provenance": {},
  "output_provenance": {},
  "recommended_action": "Inspect the affected table",
  "retry": {
    "allowed": false,
    "route": null,
    "reason": null
  }
}
```

## Run manifest

Each result identity must capture:

```json
{
  "schema_version": 1,
  "issue": 123,
  "repository_commit": null,
  "checker_commit": null,
  "corpus_id": null,
  "corpus_hash": null,
  "environment": {
    "os": null,
    "runtime_versions": {}
  },
  "inputs": [],
  "converter": {
    "engine": null,
    "version": null,
    "config": {}
  },
  "outputs": [],
  "checks": [],
  "evidence_paths": []
}
```

Do not overwrite an old result after checker/evaluation logic changes. Create a new result identity.

## Execution order

1. Inventory existing public/synthetic fixtures and their known expectations.
2. Freeze hashes and select the smallest valid positive/negative pairs.
3. Produce/freeze Markdown outputs with exact engine versions/configs.
4. Derive independent SourceFacts for inspectable formats.
5. Run the initial checker matrix.
6. Record findings and Evaluation Coverage.
7. Review false positives/false negatives per check.
8. Produce evidence review.
9. Report `GO`, `ITERATE`, or `STOP` back to #123 and parent #89.

## Stop conditions

Stop a check rather than invent evidence when source semantics cannot be independently established, provenance/hashes are missing, the format is not inspectable at the required dimension, or validation would require cloud/LLM processing.

## Acceptance for this research run

The experiment is reproducible only if another run using the same source hashes, versions, configs and checker commit can regenerate the same normalized findings and evidence references.
