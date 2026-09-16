# Zlet Converter documentation

This directory contains both current product/release documentation and historical evidence. Historical files are intentionally retained so past decisions and release evidence remain reproducible; they are not declarations of the current production architecture.

## Current

- [`PRODUCT_DESCRIPTION.md`](PRODUCT_DESCRIPTION.md) — current product description and scope.
- [`RELEASE_NOTES_v0.1.0.md`](RELEASE_NOTES_v0.1.0.md) — release notes for the current v0.1.0 PRE-ALPHA release candidate.
- [`manual-clean-machine-verification-v0.1.0.md`](manual-clean-machine-verification-v0.1.0.md) — canonical packaged Windows acceptance checklist for v0.1.0.
- GitHub Issue [#90](https://github.com/zlet-labs/zlet-converter/issues/90) — live packaged Windows acceptance state for v0.1.0.

## Current architecture / roadmap boundary

The primary product route is **Documents → high-quality Markdown**. Conversion engines are replaceable implementation details behind Zlet-owned routing and diagnostics.

The v0.1.0 package still contains the legacy Microsoft Office/COM worker for legacy Office modernization and Excel worksheet CSV/TSV exports. This is a transitional implementation, not a product contract. GitHub Issue [#84](https://github.com/zlet-labs/zlet-converter/issues/84) tracks replacement of the COM modernization path with a local non-COM worker and removal of installed Microsoft Office as a modernization requirement.

Do not delete `Zlet.FolderConverter.OfficeWorker` independently of #84 while the current resolver still routes production operations through it.

## Historical release documentation

These files describe earlier shipped releases and are intentionally immutable historical records:

- [`RELEASE_NOTES_v0.0.2.md`](RELEASE_NOTES_v0.0.2.md)
- [`RELEASE_NOTES_v0.0.3.md`](RELEASE_NOTES_v0.0.3.md)
- [`manual-clean-machine-verification.md`](manual-clean-machine-verification.md) — historical v0.0.3-era packaged verification checklist.
- [`ZL-059-verification.md`](ZL-059-verification.md) — historical verification evidence from the earlier settings/update-check delivery.

Do not use historical checklists as current acceptance instructions.

## Research / evaluation history

- [`DOCLING_EVALUATION.md`](DOCLING_EVALUATION.md) — historical Docling evaluation. Docling is not the current v0.1.0 production Markdown engine.
- [`../CONVERSION_RESEARCH.md`](../CONVERSION_RESEARCH.md) — earlier conversion-engine research notes.
- [`../evaluation/`](../evaluation/) — public/reproducible fixtures and retained research outputs used to support engine decisions.

Completed research evidence should be preserved rather than rewritten after routing decisions change. New experiments should create new evidence sets.

## Sources of truth

- **GitHub code / Issues / PRs / CI / Releases** — executable and delivery truth.
- **Notion Zlet Converter product canon** — persistent product and architecture decisions.
- **Project canon/current-state files** — compact working canon and mutable operational snapshot.

When documents disagree, refresh the live GitHub state before treating old operational data as current.