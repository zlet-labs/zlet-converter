# Manual packaged Windows verification for v0.1.0

This checklist is evidence for the actual packaged **Zlet Converter v0.1.0 PRE-ALPHA** application. It does not replace automated tests, and automated tests do not replace the packaged checks below.

Use only public/reproducible fixtures for committed/shared evidence. Private enterprise/X-IDBox documents must not be uploaded or attached.

## Acceptance identity

Record before testing:

- exact `main` commit under test;
- release/tag and whether the release is Draft or published;
- Windows version/build;
- display resolution/scaling where UI is checked;
- installed Word/Excel/PowerPoint versions, or explicit absence;
- Rust toolchain version;
- anydoc version and pinned revision;
- portable ZIP filename, byte size and SHA-256;
- installer filename, byte size and SHA-256 where tested;
- unpacked directory size;
- tester/agent identity and UTC timestamp;
- evidence directory path.

Stop with `BLOCKED` instead of guessing if the exact commit/package identity cannot be established.

## Package integrity and launch

- Compare portable ZIP SHA-256 with `SHA256SUMS.txt`.
- Fully extract the ZIP to a new directory.
- Confirm at minimum:
  - `ZletConverter.exe`;
  - `zlet-anydoc-worker.exe`;
  - `Zlet.FolderConverter.OfficeWorker.exe`;
  - `THIRD_PARTY_NOTICES.md`;
  - packaged Rust notice/license material.
- Confirm the package contains no source/test directories, Python runtime, Java runtime or private fixtures.
- Start `ZletConverter.exe` from the extracted folder.
- Confirm title/version identify **Zlet Converter v0.1.0**.
- Confirm no missing-runtime/dependency error.
- Confirm first-launch RU/EN selection works on a clean profile where practical.

## Documents → Markdown

Use repository public fixtures from `evaluation/fixtures` and record input SHA-256 plus output paths/hashes.

### DOCX

Fixture: `F08_structured.docx`

- Convert to Markdown from the packaged application.
- Verify headings/hierarchy, paragraphs, list structure, links/tables where applicable.
- Confirm output is non-empty and readable.
- Record lost/duplicated/corrupted content observations.

### XLSX

Fixtures:

- `F10_sheets.xlsx`
- `F17_xlsx_formatting.xlsx`

Verify:

- sheet boundaries/names are preserved where exposed by the route;
- displayed/cached values are represented;
- multiline cells are deterministic;
- Markdown tables are well formed where applicable;
- formula syntax is not invented when not exposed upstream.

### PPTX

Fixture: `F09_slides.pptx`

Verify:

- slide order/boundaries;
- titles/text/lists/tables exposed by the parser;
- links/notes where available;
- no duplicated slide text.

### Searchable PDF

Fixture: `F01_simple_text.pdf`

Verify:

- Markdown is produced locally;
- expected text is present in sensible reading order;
- no obvious duplicate/lost/corrupted content;
- no cloud/API requirement is triggered.

### Image-only scanned PDF

Fixture: `F06_scanned.pdf`

This fixture represents the no-extractable-text case. It does **not** prove reliable classification of every partially searchable or mixed OCR PDF.

- Confirm the core package does not report this image-only fixture as a successful OCR conversion.
- Confirm explicit specialist/OCR diagnostic (`pdf_specialist_required` or its localized user-facing mapping).
- Record result as `UNSUPPORTED`/explicit specialist-required for the core package, not as successful OCR.
- Record separately that partially searchable/mixed OCR PDFs remain provisional in v0.1.0: incidental extractable text may produce partial Markdown and requires manual quality review.

### TXT

Create a small UTF-8 text fixture inside the evidence directory containing ASCII, Cyrillic and multiple paragraphs.

- Convert TXT → Markdown.
- Confirm text/Unicode/order are preserved deterministically.

### HTML

- Confirm HTML → Markdown remains explicitly unavailable in v0.1.0.
- Confirm there is no hidden Python, Docling or cloud fallback.

## Provisional direct legacy Markdown routes

Direct legacy `.doc`, `.xls`, and `.ppt` parsing exists in the native route, but v0.1.0 does not treat those capabilities as fully packaged-verified without public reproducible legacy fixtures.

- If a public/reproducible legacy fixture is available, record its provenance/hash and run direct legacy file → Markdown.
- For DOC/XLS, verify non-empty usable Markdown and inspect structure/value preservation.
- For PPT, additionally inspect the known table-semantics limitation: upstream parsing may expose a binary table only as sequential text.
- If no suitable public fixture is available, record the corresponding direct legacy Markdown item as `BLOCKED`.
- Do not substitute DOC → DOCX / XLS → XLSX / PPT → PPTX modernization evidence for direct legacy → Markdown evidence.

## Companion assets and complex structures

Companion asset export is implemented where the parser exposes assets, but **v0.1.0 packaged asset preservation is provisional until acceptance has a public reproducible asset-bearing fixture**.

If such a fixture is available:

- record fixture provenance and SHA-256;
- confirm assets are copied to an app-owned companion directory;
- confirm Markdown references are relative, not machine-specific absolute paths;
- confirm asset filenames are deterministic and Windows-safe;
- confirm Folder and ZIP output retain working relative references;
- confirm a completed asset-bearing Markdown result keeps its assets after Stop.

If no public reproducible asset-bearing fixture is available, record companion-asset Folder/ZIP/Stop preservation as `BLOCKED`. Unit tests or implementation inspection alone do not turn this item into `PASS`.

For complex structures independently of companion assets:

- use a reproducible fixture that exercises a complex table when available;
- confirm structure-preserving HTML fallback is used when required rather than flattening merged/nested relationships merely to stay pure GFM;
- if no suitable public fixture exists, record that specific complex-table packaged item as `BLOCKED`.

## Folder output

- Choose a fresh result folder.
- Run a mixed Markdown batch.
- Confirm nested relative paths are preserved.
- Confirm one failed/unsupported source does not stop later selected items.
- Confirm existing target files/directories are not silently overwritten.
- Run the same batch again and confirm conflicts are reported.
- Confirm temporary/staging directories are cleaned.
- Confirm `ZletConverter-report.txt` uses relative paths and privacy-safe diagnostics.

## ZIP output

- Choose a new ZIP destination.
- Confirm successful Markdown outputs are included.
- Confirm `ZletConverter-report.txt` is at ZIP root.
- Confirm existing ZIP is not silently overwritten.
- Verify same-stem source collision handling is deterministic and does not overwrite one result with another.
- Companion-assets-in-ZIP is a separate acceptance item under **Companion assets and complex structures** and remains `BLOCKED` without a public asset-bearing fixture.

## Stop and completed results

- Start a mixed batch and Stop while later work is active.
- Confirm already completed outputs remain available.
- Confirm queued work stops starting.
- Confirm partial report/results remain readable.
- Completed asset-bearing result preservation is a separate acceptance item and remains `BLOCKED` without a public asset-bearing fixture.

## RU/EN diagnostics

Verify representative native-worker failures/limitations in both UI languages, including where reproducible:

- unsupported format/capability;
- source not found or equivalent safe failure path;
- conversion failure;
- image-only scanned PDF specialist-required diagnostic.

Confirm user-facing text does not leak full private source paths or document content.

## Network/privacy observation

During representative Document → Markdown conversions:

- confirm no account/login is required;
- confirm no document upload/cloud conversion request is required;
- confirm no mandatory LLM API call exists;
- record the observation method used.

Manual update checking, if explicitly initiated by the tester, is outside the conversion-network observation and must be recorded separately.

## Excel worksheet CSV/TSV export

This retained route requires real Microsoft Excel and non-sensitive workbooks. Use XLS and/or XLSX workbooks with at least two non-empty worksheets, plus hidden/very-hidden and empty sheets where practical.

- Confirm CSV mode creates one UTF-8 CSV per eligible worksheet.
- Confirm TSV mode creates one UTF-8 TSV per eligible worksheet.
- Confirm each worksheet appears as its own Preview operation.
- Confirm hidden/very-hidden worksheet state is represented and not silently treated as a normal selected sheet.
- Confirm empty worksheets are skipped explicitly.
- Confirm generated worksheet filenames are deterministic and Windows-safe.
- Confirm Unicode cell content is preserved in the exported text encoding.
- Record separately which real Excel conversions/exports were actually run.
- If Microsoft Excel or suitable non-sensitive fixtures are unavailable, record this route as `BLOCKED`, not `PASS`.

## Legacy Office modernization

This is a separate route from Markdown conversion.

Where the corresponding Microsoft Office application and non-sensitive legacy fixture are available, verify separately:

- DOC → DOCX;
- XLS → XLSX;
- PPT → PPTX;
- original source unchanged;
- generated modern Office file opens normally;
- no unrelated already-open user Office process is killed.

If real Office or fixtures are unavailable, mark these items `BLOCKED`, not `PASS`.

## Installer checks

Where practical:

- compare installer SHA-256 with `SHA256SUMS.txt`;
- install for current user on a clean/throwaway environment;
- confirm product name/version and shortcuts;
- launch installed app;
- uninstall and confirm app-owned installed files/shortcuts are removed.

The installer is unsigned in v0.1.0; record SmartScreen/Unknown Publisher behavior without treating the expected warning as a functional failure.

## Evidence layout

Recommended immutable directory outside the production repository:

```text
C:\Zlet\ZC-090-acceptance\<UTC_TIMESTAMP>\
  environment.txt
  git.txt
  commands.txt
  package\
  fixtures\hashes.txt
  logs\
  outputs\
  assets\
  zip\
  diagnostics\
  verdict.md
```

Record exact commands, raw output, Markdown outputs, diagnostics, package/fixture hashes and evidence paths.

## Verdict vocabulary

Use per-item verdicts:

- `PASS`
- `PARTIAL`
- `FAIL`
- `UNSUPPORTED`
- `BLOCKED`

Do not collapse conversion quality into one opaque score. Do not use `NOT APPLICABLE` as an escape hatch for an advertised or implemented capability; if required reproducible evidence is unavailable, use `BLOCKED` and say why.

## Completion rule

Full v0.1.0 packaged acceptance is complete only when the evidence is reviewed under the project's evidence-review rules and GitHub Issue #90 contains the final evidence-backed verdict. Provisional `BLOCKED` capability items must remain visible rather than being silently converted into passes. Release publication alone is not packaged acceptance.
