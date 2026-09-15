# Zlet Converter

<p align="center">
  <img src="docs/assets/zlet-batch-converter-hero.svg" alt="Zlet Converter — local Windows file converter from Zlet Labs" width="100%">
</p>

<p align="center">
  <strong>English</strong> · <a href="README_RU.md">Русский</a>
</p>

<p align="center">
  <img alt="Version v0.1.0" src="https://img.shields.io/badge/version-v0.1.0-2563eb">
  <img alt="PRE-ALPHA" src="https://img.shields.io/badge/status-PRE--ALPHA-f59e0b">
  <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows%20x64-0078D4">
  <img alt="Local processing" src="https://img.shields.io/badge/processing-local%20only-16a34a">
  <img alt="MIT License" src="https://img.shields.io/badge/license-MIT-22c55e">
</p>

Zlet Converter is a privacy-first/local-first Windows desktop utility for converting documents into high-quality Markdown, modernizing supported legacy Office files, and processing batches in folders/subfolders without mandatory accounts or cloud conversion.

The application UI supports Russian and English in the same package. On first launch, choose a language explicitly; change it later under **Settings → Language** without restarting or losing the current preview/results. Only the language setting is stored in `%LOCALAPPDATA%\Zlet Labs\Zlet Converter\settings.json`; no account, cloud service, or backend is required. Packagers can set the initial choice with `ZletConverter.exe --language=ru-RU` or `--language=en-US`.

> **v0.1.0 is PRE-ALPHA software, but it is published as a normal GitHub Release, not as a GitHub Pre-release.** PRE-ALPHA describes product maturity. The installer is currently unsigned, so Windows may show an Unknown publisher or SmartScreen warning. Microsoft Office is not included.

> **Rename note:** v0.0.2 was published under the previous public name `Zlet Batch Converter`. Its historical release title and asset names remain unchanged. v0.0.3 and later use the current `Zlet Converter` / `ZletConverter` naming.

## Download v0.1.0

| Windows installer | Portable ZIP |
|---|---|
| **[⬇ Download installer](https://github.com/zlet-labs/zlet-converter/releases/download/v0.1.0/ZletConverter-v0.1.0-Setup-win-x64.exe)** | **[📦 Download portable](https://github.com/zlet-labs/zlet-converter/releases/download/v0.1.0/ZletConverter-v0.1.0-win-x64.zip)** |
| `ZletConverter-v0.1.0-Setup-win-x64.exe` | `ZletConverter-v0.1.0-win-x64.zip` |

[Release notes](https://github.com/zlet-labs/zlet-converter/releases/tag/v0.1.0) · [SHA-256 checksums](https://github.com/zlet-labs/zlet-converter/releases/download/v0.1.0/SHA256SUMS.txt)

### Why use it?

| 🔒 Local | ⚡ Batch | 🛡 Defensive |
|---|---|---|
| No accounts, no document uploads | Scan folders and subfolders in one run | Existing outputs are not silently overwritten |
| Document contents stay on your PC | Convert many documents to Markdown | Explicit diagnostics instead of silent quality degradation |

## Documents → Markdown

v0.1.0 introduces the first substantial implementation of Zlet Converter's main product route: **Documents → high-quality Markdown**.

The default package includes a local native `zlet-anydoc-worker.exe` based on pinned `anydoc 0.2.4`. Zlet owns the routing, diagnostics and Markdown rendering contract rather than exposing an engine-specific product API.

Simple structures render as ordinary GFM Markdown. When a complex table cannot be represented without losing merged-cell or nested structure, Zlet may emit sanitized HTML inside Markdown instead of flattening the source into a prettier lie.

Companion image/asset export is implemented where the parser exposes assets and uses relative local references. End-to-end packaged preservation is still **provisional in v0.1.0** until acceptance includes a public reproducible asset-bearing fixture; it must not be recorded as passed merely because the renderer has unit coverage.

## Supported formats in v0.1.0

### Markdown

| Source | Result | Requirement / status |
|---|---|---|
| `.docx` | `.md` + companion assets where applicable | bundled local native worker |
| legacy `.doc` | `.md` | provisional direct local path; packaged acceptance pending public legacy fixture |
| `.xlsx` | `.md` | bundled local native worker; displayed/cached values |
| legacy `.xls` | `.md` | provisional direct local path; packaged acceptance pending public legacy fixture |
| `.pptx` | `.md` + companion assets where applicable | bundled local native worker |
| legacy `.ppt` | `.md` | provisional; known table-semantics limitation |
| straightforward searchable `.pdf` | `.md` | bundled local native worker |
| image-only/no-text scanned `.pdf` | specialist-required diagnostic | explicit `pdf_specialist_required`; no core OCR |
| partially searchable / mixed OCR `.pdf` | provisional | incidental extractable text can yield partial Markdown; manual review required |
| `.txt` | `.md` | direct local route |
| `.html`, `.htm` | Markdown not enabled in v0.1.0 | explicit unsupported capability; no hidden cloud/Python fallback |
| `.json` | `.md` or `.txt` | existing local route |

### Legacy Office modernization and existing utilities

| Source | Result | Requirement |
|---|---|---|
| `.doc` | `.docx` | Microsoft Word installed |
| `.xls` | `.xlsx` | Microsoft Excel installed |
| `.ppt` | `.pptx` | Microsoft PowerPoint installed |
| `.xls`, `.xlsx` | one UTF-8 `.csv` per worksheet | Microsoft Excel installed |
| `.xls`, `.xlsx` | one UTF-8 `.tsv` per worksheet | Microsoft Excel installed |
| supported already-compatible files | unchanged safe copy | Office not required where conversion is unnecessary |

For Excel sheet exports, each worksheet is a separate Preview operation. Hidden and very-hidden worksheets remain visible but are not selected by default; empty worksheets are skipped explicitly. Output names are deterministic and Windows-safe, such as `sales__Summary.csv`.

Zlet Converter generates a human-readable `ZletConverter-report.txt` with relative paths, batch counters, worksheet accounting, statuses and safe diagnostics. Existing report names are not silently overwritten; deterministic `-2`, `-3`, ... suffixes are used.

The final panel retains aggregate and per-workbook sheet summaries, including hidden and empty sheets skipped. A workbook counts as one source file even when it produces several worksheet files. Reports are saved in the result folder or at the ZIP root, including after Stop or partial failure. Report failures remain visible. Controls and report labels follow the RU/EN language setting; switching language preserves the preview and results.

Preview can be filtered by the Rules format rows without changing checkbox selection or the conversion execution set. A visible **Show all** action clears the active format filter. Source file, action, status, result, size and time columns can be sorted ascending/descending, and visible rows are numbered from 1 according to the current filter + sort order.

Real Excel sheet tests are opt-in: set `ZLET_OFFICE_INTEGRATION=1`, `ZLET_OFFICE_XLSX_SHEETS_FIXTURE` and/or `ZLET_OFFICE_XLS_SHEETS_FIXTURE` to local multi-sheet workbooks with at least two nonempty two-column worksheets, then run the `OfficeIntegration` test category. Automated tests do not substitute for full clean-machine and real Microsoft Office verification.

Word, Excel, and PowerPoint are detected independently. If one Office application is missing, only the corresponding Office-dependent conversion becomes unavailable. The bundled modern Document → Markdown routes do not require Microsoft Office.

> **PowerPoint safety:** legacy PPT modernization is refused while user PowerPoint is already running. This avoids interfering with an open presentation. Markdown conversion through the native document worker is a separate route.

## Quick start

1. Download the installer or portable ZIP above.
2. Run `ZletConverter.exe`.
3. Choose a source folder and scan it.
4. Review Preview and select the operations you want.
5. Choose Markdown or another available target, then choose Folder/ZIP output.
6. Run the batch and review per-file results and diagnostics.

Packaged builds are self-contained for .NET 8, so the .NET runtime does not need to be installed separately.

## What you get

- Local Document → Markdown conversion without a mandatory cloud service or LLM API.
- Format/capability routing behind one app-owned conversion contract.
- Adaptive Markdown rendering for lists, tables, links and assets exposed by the parser.
- Explicit diagnostics when a capability is not safely available.
- Preview before processing with format filtering, sorting and visible row numbering.
- Selection of individual operations before execution.
- Relative subfolder structure preserved in output.
- Per-file status, stage progress, source size, and execution time.
- Safe Stop that prevents new queued operations from starting while preserving completed results.
- Separate final counters for converted, copied, failed, conflict, unavailable, skipped, and unselected items.
- Persistent human-readable `ZletConverter-report.txt` in folder or ZIP output.

## Limitations

Zlet Converter is still **PRE-ALPHA** and does not claim universal lossless conversion.

- Direct legacy `.doc` and `.xls` → Markdown are implemented paths, but v0.1.0 packaged acceptance treats them as provisional until public reproducible legacy fixtures are available.
- Direct legacy `.ppt` → Markdown can lose table semantics when the upstream parser has already exposed a binary PowerPoint table only as sequential text. Zlet does not invent lost structure.
- Complex multi-column/layout-heavy PDF is provisional; v0.1.0 does not claim that the lightweight route fully solves those documents.
- Image-only/no-extractable-text scanned PDFs are reported as specialist-required. Partially searchable/mixed OCR PDFs are not reliably classified in v0.1.0 and can expose incomplete extracted text, so those outputs require manual review.
- Companion asset export is implemented, but packaged Folder/ZIP/Stop preservation remains provisional until a public asset-bearing fixture is added to clean-machine acceptance.
- HTML → Markdown is intentionally disabled in v0.1.0 until a dedicated lightweight local route is qualified.
- Password-protected, encrypted, corrupted or unsupported documents can fail explicitly.

Original files are not intentionally modified, but keep backups of important data when testing PRE-ALPHA software.

<details>
<summary><strong>Safety and privacy details</strong></summary>

The converter is intentionally local-first and defensive around user files.

- Files are processed locally and are not uploaded for conversion.
- No mandatory account, backend, cloud conversion service or LLM API is required.
- The UI process does not perform Office COM automation directly.
- Office modernization runs through an isolated STA worker process.
- The native Markdown worker is bundled locally and uses a pinned dependency graph.
- Legacy files and Excel workbooks used for worksheet export are opened read-only where required.
- Source files must remain inside the selected source folder.
- Reparse-point files and folders are skipped.
- Source SHA-256 is checked before and after processing where the existing safe operation contract requires it.
- Outputs are produced in temporary/staging locations and validated before the final move.
- Existing output files/directories are not overwritten.
- Office processes are never terminated by process name alone.
- TXT reports use relative paths and must not contain document contents, passwords, secrets or tokens.

Technical diagnostics may contain error codes and process metadata. They must not contain document contents, secrets, or full local document paths.

</details>

<details>
<summary><strong>Build from source and package locally</strong></summary>

Requirements:

- Windows x64
- .NET 8 SDK
- Rust 1.88.0 (pinned in `rust-toolchain.toml`)

Build the native Markdown worker first, then the .NET solution:

```powershell
cargo test --manifest-path src/Zlet.FolderConverter.AnydocWorker/Cargo.toml --locked
cargo build --manifest-path src/Zlet.FolderConverter.AnydocWorker/Cargo.toml --release --locked
dotnet restore FolderConverter.sln
dotnet build FolderConverter.sln -c Release
dotnet test FolderConverter.sln -c Release
```

If you cloned the repository before it was renamed to `zlet-converter`, update the existing remote instead of recloning:

```powershell
git remote set-url origin https://github.com/zlet-labs/zlet-converter.git
```

Build the portable package:

```powershell
.\scripts\publish-portable.ps1
```

Expected local ZIP at the current source version:

```text
artifacts/portable/win-x64/ZletConverter-v0.1.0-win-x64.zip
```

Build the Windows installer with Inno Setup 6:

```powershell
.\scripts\build-installer.ps1
```

The installer script builds the portable payload, then creates the Windows x64 installer and prints its SHA-256 and Authenticode status.

</details>

<details>
<summary><strong>Real Microsoft Office integration tests</strong></summary>

Real Office integration tests are opt-in because they require installed Microsoft Office and real legacy fixtures:

```powershell
$env:ZLET_OFFICE_INTEGRATION = "1"
$env:ZLET_OFFICE_WORD_FIXTURE = "C:\fixtures\sample.doc"
$env:ZLET_OFFICE_WORD_BATCH_FIXTURE_DIR = "C:\fixtures\word-batch"
$env:ZLET_OFFICE_EXCEL_FIXTURE = "C:\fixtures\sample.xls"
$env:ZLET_OFFICE_POWERPOINT_FIXTURE = "C:\fixtures\sample.ppt"
dotnet test FolderConverter.sln -c Release --filter Category=OfficeIntegration
```

If an Office application or required fixture is missing, the corresponding integration test is skipped rather than counted as passed.

</details>

## Project

Zlet Converter is a **Zlet Labs** project: small, practical, self-serve tools without unnecessary SaaS machinery.

[Zlet Labs](https://zlet.app/) · [GitHub Issues](https://github.com/zlet-labs/zlet-converter/issues) · [All releases](https://github.com/zlet-labs/zlet-converter/releases) · [MIT License](LICENSE)

Release notes: [docs/RELEASE_NOTES_v0.1.0.md](docs/RELEASE_NOTES_v0.1.0.md) · Manual verification checklist: [docs/manual-clean-machine-verification-v0.1.0.md](docs/manual-clean-machine-verification-v0.1.0.md)
