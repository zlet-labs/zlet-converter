# ZC-062 — corpus inventory

## Evidence source

Inventory was taken from the supplied `ZC_v0.1.0_Converter_Test_Set` package and its own `manifest.json`.

The package contains 20 manifest-tracked files: public/synthetic-style smoke/parity fixtures, negative inputs, package documentation, and a separate `04_Real_World_Documents` area explicitly described as **not a public corpus**.

For committed ZC-062 evidence, the real-world/private area is excluded.

## Eligible frozen inputs

### Controlled handbook family

| Path | SHA-256 | Role |
| --- | --- | --- |
| `03_Controlled_Parity/paired-handbook.doc` | `bd1c651e10017bb165d1f304dced1fc66871ffef67180020c34627cce579023f` | controlled_parity |
| `03_Controlled_Parity/paired-handbook.docx` | `43717212e82a9d4b02238147dd96df19fba5aa364a97d5ee5bf163dc3cb18d31` | controlled_parity |
| `03_Controlled_Parity/paired-handbook.pdf` | `a6d8d133c975130e7a9cf2df310bdfb7ced7548f1c1719e60b33a1a2a8e4b225` | controlled_parity |
| `03_Controlled_Parity/paired-handbook.pptx` | `5e8347638ace43147e43dacc36c2726918c6d26eb459d4f917c70a1f84a26628` | controlled_parity |

The package README states that this handbook family carries the same narrative fixture across routes. This makes it useful for parity observations, but cross-format equivalence alone is not an independent semantic oracle.

### Controlled spreadsheet fixture

| Path | SHA-256 | Role |
| --- | --- | --- |
| `03_Controlled_Parity/paired-table.xlsx` | `f6537a2751e89798f0962d0f851b032139b8a9b597dc5031073bfa18ca92c2e5` | controlled_parity |

### Negative / unsupported

| Path | SHA-256 | Intended role |
| --- | --- | --- |
| `02_Unsupported_and_Negative/broken-invalid.docx` | `cba58667e4ec55d129dadfca53ba99612555a7cc0f6b900d2c76df5d0f1fccb7` | deliberately invalid DOCX |
| `02_Unsupported_and_Negative/paired-handbook.html` | `f962da5d6bc4157c18c11ae6cd1852d2e9abe394a068728b66ae782f122ac8d3` | expected unsupported in v0.1.0 test plan |

## Duplicate smoke copies

The smoke folder contains byte-identical copies of several controlled fixtures. Their hashes match the controlled-parity versions. They do not create independent evidence and should not be counted twice in checker precision/coverage summaries.

## Excluded from committed/public evidence

The package README says `04_Real_World_Documents` contains selected complex/large inputs and explicitly states they are **not a public corpus**.

Therefore ZC-062 must not commit, publish, or use their content as reproducible public evidence:

- `x_id_box_schema.xlsx`
- `Параметры логирования событий безопасности.pdf`
- `ТЗ_ЛК_1.2.1_Этап1_v1.18.docx`
- `ФТ_ЛК_ЛК_v.1.18.1.docx`

They may only be supplementary local checks under the project privacy rules.

## Coverage against the initial checker matrix

| Check | Existing corpus sufficient? | Next action |
| --- | --- | --- |
| QC-EMPTY-001 | No explicit empty-output fixture | create synthetic Markdown mutation + valid control |
| QC-SHORT-001 | Source fixtures available, but no known-loss output pair yet | derive independent SourceFacts and create controlled lossy output mutation |
| QC-DUP-001 | No explicit unintended-duplication pair | create duplicated-block mutation + intentional-repeat control if needed |
| QC-HEAD-001 | DOCX/PPTX source available | inspect source structure and create controlled heading-loss mutation |
| QC-LIST-001 | DOCX/PPTX may contain list facts; must verify | inspect source facts before declaring applicability |
| QC-TABLE-001 | XLSX controlled table available | derive sheet/cell/table facts and create controlled row/cell-loss mutation |
| QC-MARKUP-001 | No explicit malformed Markdown output | create malformed-output mutation + valid control |
| QC-STABLE-001 | Inputs available | requires two real conversions with same pinned version/config |
| QC-COVER-001 | Yes | use unsupported/uninspectable dimensions to prove explicit NOT_EVALUATED behavior |

## First-run selection

The smallest defensible first run should use:

1. `paired-handbook.docx` for independently inspectable headings/paragraph/list candidates;
2. `paired-table.xlsx` for spreadsheet content/structure facts;
3. `broken-invalid.docx` for conversion/error diagnostics;
4. synthetic Markdown mutations derived from a frozen valid output for EMPTY, SHORT, DUP, HEAD/LIST/TABLE loss and MARKUP checks;
5. repeated conversion of the same pinned input only when the exact converter binary/version/config is available.

PDF order/semantic fidelity is intentionally deferred unless an independent PDF evidence provider can establish the tested fact. Otherwise it remains `NOT_EVALUATED`.

## Important limitation

This inventory freezes what inputs exist. It does **not** yet prove that a checker detects defects. The next result identity requires actual SourceFacts, frozen Markdown outputs, controlled mutations, and observed findings.
