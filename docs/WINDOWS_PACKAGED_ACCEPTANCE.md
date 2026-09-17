# Windows packaged acceptance

This workflow verifies the actual packaged Zlet Converter runtime on Windows 11 x64. It is separate from unit/CI verification and is intended to produce reproducible release/acceptance evidence.

## What it checks

The workflow exercises the supported packaged `ZletConverter.exe batch` entrypoint against versioned public fixtures and checks these routes:

- DOCX -> Markdown
- PDF -> Markdown
- PPTX -> Markdown
- XLSX -> Markdown
- TXT -> Markdown

For every route it requires a non-empty UTF-8 `.md` output. PDF/OOXML outputs are rejected if they retain the source binary signature. Binary-source routes are also rejected when the output SHA-256 is byte-identical to the source, which catches the regression where routing reports Markdown but the runtime performs Copy.

This is an execution/routing acceptance gate, not a claim that the Markdown has sufficient semantic quality. Markdown quality remains covered by the quality benchmark and later quality work.

## Prerequisites

- Windows 11 x64.
- A checked-out copy of the exact repository revision whose public fixtures are being used.
- An extracted portable Zlet Converter package containing `ZletConverter.exe`.
- PowerShell 5.1 or newer.

No account, network service, cloud processing, LLM API, telemetry, Microsoft Office, or private document is required for the five Document -> Markdown checks.

## One-command run

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test-packaged-windows.ps1 `
  -PackagePath "C:\path\to\extracted\ZletConverter" `
  -CommitSha "<packaged-git-commit>"
```

The script creates a timestamped `zlet-acceptance-evidence` directory by default. Use `-EvidencePath` to place evidence elsewhere.

## Evidence

Each run retains:

- `acceptance-report.json`, schema `zlet-converter-packaged-acceptance/v1`;
- the app file version and packaged EXE SHA-256;
- the tested git commit supplied by the operator/release workflow;
- Windows version/build, architecture and PowerShell version;
- fixture identities and SHA-256 hashes;
- per-route PASS/FAIL, output paths and output SHA-256 hashes;
- `conversion-report.json` emitted by the packaged app-owned headless entrypoint;
- the exact temporary source/output files used for the run.

A successful run exits `0` and prints `Packaged acceptance: PASS 5/5`. Any failed route, non-zero converter exit code, or missing converter report makes the acceptance command exit `1`.

## Reproducibility and limitations

Run the same package, repository revision and script to reproduce the acceptance result. The repository fixtures are public and versioned. The TXT fixture is generated deterministically by the script and its hash is recorded.

This gate deliberately does not silently infer conversion quality from file existence. It proves basic packaged routing and text-output integrity. Structural fidelity, tables, headings, reading order, PDF complexity and OCR require their dedicated benchmark/evidence gates.

Legacy DOC/XLS/PPT modernization is outside the mandatory five-route gate because it depends on the configured local Legacy Office capability. When legacy packaged acceptance is added, dependency absence must be reported explicitly rather than treated as success.
