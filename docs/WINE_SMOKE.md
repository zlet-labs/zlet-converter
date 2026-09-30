# Wine compatibility smoke

`./scripts/run-wine-smoke.sh` is a Linux/Miami compatibility layer for the Windows x64 headless conversion core.

It cross-publishes the self-contained `win-x64` CLI from an exact git commit, launches that Windows executable under Wine, performs deterministic conversion (TXT by default; PDF/DOCX/PPTX/XLSX when a corpus is supplied), and records hashes, environment, logs, outputs, conversion report, and a runner report under the evidence root.

This layer is intentionally **not** packaged Windows acceptance. It does not validate WPF UI, installer behavior, COM/Office integration, or other Windows-specific behavior. Those remain a real-Windows acceptance/release gate.

Default run:

```bash
./scripts/run-wine-smoke.sh --git-ref HEAD
```

Evidence defaults to `/srv/zlet-converter/evidence/wine`. A private/local corpus can be supplied with `--corpus`; the runner does not upload inputs or outputs. The Windows anydoc worker is cross-built with the pinned Rust 1.88.0 GNU Windows target and runs under Wine. Supported smoke inputs are TXT, PDF, DOCX, PPTX and XLSX.
