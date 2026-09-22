# ZC-062 — first Quality Core smoke observation

## Run

A first live Conversion Lab run was executed on 2026-09-21:

```text
zcl run --corpus controlled-smoke-v1 --engines zlet --quality-core
```

Run id: `20260921T142309Z-24770a90`

Conversion Lab revision: `c6b2460dc22684d88217eec6c83fb42aecdbf5d0`

Environment recorded by the run:
- Linux 6.8.0-110-generic, x86_64
- Python 3.12.3
- Quality Core runtime: Debian 12, .NET 8.0.31

Zlet runtime evidence:
- engine version: `0.1.0`
- native Linux executable SHA-256: `9ba96c2d50c937c90eecdad5b5a8df6a78af2778c5c7c3d2bacb5e29df5d73e4`
- runtime artifact size: 72,568 bytes

Quality Core container evidence:
- image: `zlet-lab/quality-core:0.1`
- image id/digest: `sha256:66de7c8d49f4063b1749cd0f25c4dd4fa506b2eb5bf00b3cb34f6c9d67d64846`
- image size: 84,046,014 bytes

## Results

The corpus contained two fixtures:
- `txt-smoke`: conversion PASS, Quality Core EVALUATED, verification PASS;
- `html-structure`: conversion UNSUPPORTED by the selected Zlet route, Quality Core explicitly recorded `NOT_EVALUATED_CONVERSION_NOT_PASS`.

For `txt-smoke`:
- source SHA-256 = Markdown SHA-256 = `009d3f060fec0a5e81ed6646f1287432f6f1159098f23f0d28be22957b769da4`;
- text recall = 1.0;
- text precision = 1.0;
- content token length ratio = 1.0;
- heading/list/table/link metrics correctly remained `NOT_APPLICABLE`;
- pairwise order remained `NOT_EVALUATED_INSUFFICIENT_ANCHORS`;
- evidence fixture verification passed.

Quality evidence identity:
- run schema: `zlet-quality-run/0.2`;
- report schema: `zlet-quality-report/0.2`;
- Quality Core: `0.1.0-dev`;
- Source Inspector: `zlet-txt-native/0.1.0`;
- Markdown Inspector: `zlet-markdown-markdig/0.1.0`;
- comparator: `zlet-exact-quality-comparator/0.2.0`;
- matcher: `exact-normalized-text/0.1`;
- metrics: `conversion-fidelity/0.2`;
- evidence key: `e3182b7254e5d8edf6fa8fc105a15486ac93ab401d790669612bc677ae5c67e0`.

## Important finding

The exact comparator emitted:

```text
EXTRA_OR_DUPLICATED_CONTENT
Markdown contains 1 content fact(s) beyond exact source multiplicity.
```

even though the TXT source and Markdown output are byte-identical and both text recall and precision are 1.0.

This is a concrete false-positive candidate in the current Quality Core representation/comparator path. It must be investigated before duplicate-content findings can qualify for an MVP checker.

This is precisely why ZC-062 requires positive and negative fixtures instead of treating a successful pipeline execution as proof of checker correctness.

## Preliminary evidence-review status

| Area | Status | Observation |
| --- | --- | --- |
| evidence identity / hashes | PASS | run records source/output/pipeline identities and verifies frozen artifacts |
| explicit unsupported state | PASS | unsupported HTML was not silently counted as successful quality evaluation |
| applicability semantics | PASS | inapplicable structural metrics remained explicit |
| text preservation on TXT control | PASS | recall/precision 1.0 with byte-identical source/output |
| duplicate-content check | FAIL / investigate | false-positive candidate on byte-identical TXT control |
| order evaluation | PARTIAL | insufficient anchors correctly reported, but not evaluated |
| ZC-062 full checker benchmark | BLOCKED pending fixtures | no controlled defect mutations were run yet |

## Next action

Before adding more architecture, isolate the TXT false-positive cause, add it as a negative regression fixture, then run controlled positive mutations for EMPTY, SHORT and DUP. Only after those pass should the experiment expand to DOCX/XLSX SourceFacts checks.
