# Headless batch conversion

Zlet Converter exposes a supported headless batch entrypoint for reproducible operator and QA runs. It reuses the same scanner, planner, adapter resolver, workers, source-integrity protections and conversion processor as the desktop application; it is not a separate conversion engine.

## Command

```powershell
ZletConverter.exe batch `
  --source "C:\input" `
  --destination "C:\output" `
  --target markdown `
  --report-json "C:\evidence\conversion-report.json"
```

`--recursive true|false` is optional and defaults to `true`.

The first contract supports only `--target markdown`. Unknown options, duplicate options and unsupported targets fail closed.

## Linux test/CI runtime

For internal tests, CI and Conversion Lab runs, the same headless contract is available through the cross-platform CLI host:

```bash
./zlet-converter batch \
  --source /data/input \
  --destination /data/output \
  --target markdown \
  --recursive true \
  --report-json /data/evidence/conversion-report.json
```

The Linux runtime is an internal automation surface, not a public Linux desktop edition. It reuses the same app-owned core and headless runner as the Windows application. Windows-only capabilities such as Microsoft Office COM automation are not a Linux fallback and must remain explicit when unavailable.

Linux CI/regression evidence answers a different question from packaged Windows acceptance and does not replace the Windows release gate.

## Evidence behavior

The JSON report uses schema `zlet-converter-headless-report/v1` and records:

- product/version identity;
- requested target and recursive mode;
- per-source relative identity and detected format;
- terminal conversion status and stable diagnostic code where available;
- source SHA-256 before and after processing;
- explicit source-integrity result;
- final derived Markdown and companion asset paths, sizes and SHA-256 values;
- scan errors and aggregate counts;
- final batch state.

Paths in per-item evidence are relative. The report records only source/destination root names rather than absolute local paths.

An existing report file is never overwritten.

Unsupported capability remains explicit. For example, until the dedicated local HTML-to-Markdown route is implemented, HTML in a Markdown batch is reported as `Unsupported`; it is not silently copied or treated as a successful Markdown conversion.

## Exit codes

| Code | Meaning |
|---:|---|
| `0` | Batch completed without reported conversion/integrity issues |
| `2` | Batch completed with one or more unsupported, unavailable, failed, conflicting, not-processed, scan, or integrity issues |
| `64` | Invalid invocation/configuration |
| `70` | Fatal unexpected runtime failure |
| `130` | Cancelled |

Exit code `2` is not a QUALITY verdict. The machine report is execution/provenance evidence; downstream Zlet AI Quality gates evaluate conversion quality separately.

## Privacy and product boundary

Headless mode performs local conversion only. It does not add sanitization, chunking, embeddings, retrieval, RAG evaluation, cloud processing or LLM/API dependencies.

Do not call internal worker executables directly for canonical operator evidence. Use this supported app-owned entrypoint so routing, diagnostics, process ownership, source safety and transactional output semantics remain aligned with the desktop product.
