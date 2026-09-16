# Zlet Converter v0.1.0

[English](#english) · [Русский](#русский)

> **PRE-ALPHA · Windows x64 · Local processing only**
>
> GitHub classification: **standard Release** (`prerelease=false`). PRE-ALPHA describes product maturity, not GitHub Pre-release status.

## English

Zlet Converter v0.1.0 is the first release centered on the product's main promise:

**Documents → high-quality Markdown**

This is a functional milestone rather than a patch release. The Markdown route now uses a bundled local native worker with format/capability routing and an app-owned conversion contract. No account, cloud conversion service or LLM API is required.

### Highlights

- **DOCX → Markdown.** Local `anydoc 0.2.4` structured extraction plus the Zlet adaptive renderer. A direct legacy DOC path is implemented, but its packaged acceptance remains provisional in v0.1.0 until a public reproducible legacy fixture is added.
- **XLSX → Markdown.** Local `anydoc` / Calamine path with deterministic sheet-oriented output and displayed/cached values. Formula syntax is not invented when it is not exposed by the parser. A direct legacy XLS path is implemented but remains packaged-acceptance provisional in v0.1.0.
- **PPTX → Markdown.** Local `anydoc` route with slide order/boundaries and supported structural content preserved where exposed upstream. Direct legacy PPT remains provisional and has a known table-semantics limitation noted below.
- **Searchable PDF → Markdown.** Straightforward digital text PDFs use the lightweight local route.
- **Image-only scanned PDF is explicit.** For scanned PDFs with no extractable text, including the public `F06_scanned.pdf` fixture, the core package returns `pdf_specialist_required`. v0.1.0 does not reliably classify partially searchable/mixed OCR PDFs; incidental extractable text can still produce partial Markdown and therefore requires manual review.
- **TXT → Markdown.** Direct local conversion.
- **Adaptive Markdown rendering.** Simple lossless structures render as normal GFM. Complex tables may use HTML inside Markdown when GFM cannot preserve merged-cell or nested structure without loss.
- **Companion asset export is implemented, but provisional for packaged acceptance.** Images/assets use deterministic relative references where the parser exposes them. Full packaged preservation remains `BLOCKED` until a public reproducible asset-bearing fixture is included in acceptance evidence.
- **Batch / Folder / ZIP integration.** Markdown results participate in the existing batch, conflict-protection, stop and ZIP workflows.
- **Stable diagnostics.** Worker failures are mapped into app-owned error codes with RU/EN user-facing messages.
- **Bundled native worker.** `zlet-anydoc-worker.exe` ships inside the Windows package. The worker protocol is `1.0`.
- **Pinned dependencies.** `anydoc 0.2.4` is pinned to revision `42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c`; Rust toolchain is pinned by the repository.
- **Rust third-party notices.** Packaged license/notices are generated from the locked dependency graph without build-machine absolute paths.
- **Resource safety.** The native worker enforces an explicit input-size limit before allocating the complete input document.

### Existing routes retained

Legacy Office modernization remains separate from Markdown conversion:

| Source | Result | Requirement |
|---|---|---|
| `.doc` | `.docx` | Microsoft Word installed |
| `.xls` | `.xlsx` | Microsoft Excel installed |
| `.ppt` | `.pptx` | Microsoft PowerPoint installed |

Existing Excel worksheet CSV/TSV export, safe-copy operations, batch reporting, Folder/ZIP output, conflict protection, RU/EN UI and local settings remain available.

### Markdown capability summary

| Source | Markdown in v0.1.0 | Notes |
|---|---|---|
| `.docx` | Yes | bundled local native worker |
| legacy `.doc` | Provisional | direct local path implemented; packaged acceptance pending public legacy fixture |
| `.xlsx` | Yes | sheet-oriented local route |
| legacy `.xls` | Provisional | direct local path implemented; packaged acceptance pending public legacy fixture |
| `.pptx` | Yes | bundled local native worker |
| legacy `.ppt` | Provisional | known table-semantics limitation; packaged acceptance requires explicit evidence |
| searchable `.pdf` | Yes | lightweight local route for straightforward digital PDFs |
| image-only/no-text scanned `.pdf` | No core OCR | explicit `pdf_specialist_required` diagnostic |
| partially searchable / mixed OCR `.pdf` | Provisional | may yield partial extracted text; manual review required |
| `.txt` | Yes | direct local route |
| `.html`, `.htm` | Not enabled for Markdown | no hidden Python/Docling/cloud fallback |

### Important limitations

- Zlet Converter v0.1.0 is **PRE-ALPHA** and does not claim universal lossless conversion.
- Direct legacy `.doc` and `.xls` → Markdown are implemented paths but are not treated as fully packaged-verified release capabilities until public reproducible legacy fixtures are added to acceptance.
- Direct legacy `.ppt` → Markdown may lose table semantics when the upstream parser exposes a binary PowerPoint table only as sequential text. Zlet does not invent structure already lost upstream.
- Complex multi-column or layout-heavy PDFs remain provisional and are not advertised as fully solved by the lightweight route.
- Image-only/no-extractable-text scanned PDFs are explicitly routed to specialist-required diagnostics. Partially searchable/mixed OCR PDFs are not reliably classified in v0.1.0 and may expose incomplete extracted text.
- Companion asset export is implemented, but packaged preservation is provisional until an asset-bearing public fixture can verify folder, ZIP and stopped-batch behavior end to end.
- OCR/scanned-PDF specialist processing is not bundled in the core package.
- HTML → Markdown is intentionally not enabled in v0.1.0 pending a dedicated lightweight local capability.
- Password-protected, encrypted, corrupted or otherwise unsupported documents may fail explicitly.
- Microsoft Office is not included. It is required only for the separate legacy Office modernization / Excel export routes that already depend on it.
- The installer remains unsigned, so Windows may show an Unknown publisher or SmartScreen warning.

### Privacy and product boundary

- Conversion is local-first and does not require document upload.
- No mandatory account, backend, cloud service or LLM API is introduced.
- v0.1.0 does **not** add chunking, metadata enrichment, embeddings, vector databases, retrieval, RAG pipelines or AI evaluation.

### Verification status

The release workflow is required to run locked Rust tests/build, .NET restore/build/tests, win-x64 packaging, package validation and SHA-256 generation before creating the release assets.

Full packaged Windows acceptance remains a separate evidence step and is tracked in GitHub Issue #90. `BLOCKED` provisional items stay visible in the evidence; release publication alone is not proof that every packaged/manual capability has passed.

---

## Русский

Zlet Converter v0.1.0 — первый релиз, в центре которого находится основное обещание продукта:

**Documents → high-quality Markdown**

Это функциональная веха, а не очередной patch-релиз. Маршрут Markdown теперь использует bundled локальный native worker, routing по format/capability и собственный контракт результата Zlet. Аккаунт, облачная конвертация и LLM API не требуются.

### Главное

- **DOCX → Markdown.** Локальный `anydoc 0.2.4` + Zlet adaptive renderer. Прямой legacy DOC route реализован, но packaged acceptance в v0.1.0 остаётся provisional до появления публичной воспроизводимой legacy-фикстуры.
- **XLSX → Markdown.** Локальный `anydoc` / Calamine route с детерминированным sheet-oriented результатом и displayed/cached values. Zlet не выдумывает формулы, которых parser не предоставил. Прямой legacy XLS route реализован, но packaged acceptance остаётся provisional.
- **PPTX → Markdown.** Локальный `anydoc` route с сохранением порядка слайдов и доступной upstream структуры. Direct legacy PPT остаётся provisional и имеет известное ограничение семантики таблиц.
- **Searchable PDF → Markdown.** Обычные цифровые PDF с текстовым слоем идут через лёгкий локальный route.
- **Image-only scanned PDF обрабатывается явно.** Для scanned PDF без извлекаемого текста, включая публичную `F06_scanned.pdf`, core package возвращает `pdf_specialist_required`. v0.1.0 не умеет надёжно классифицировать partially searchable/mixed OCR PDF: случайно доступный текст может дать частичный Markdown, который требует ручной проверки.
- **TXT → Markdown.** Прямой локальный route.
- **Adaptive Markdown rendering.** Простые lossless-структуры выводятся как GFM. Сложные таблицы могут использовать HTML внутри Markdown, если GFM не способен сохранить merged cells или nested structure без потерь.
- **Companion asset export реализован, но packaged acceptance остаётся provisional.** Изображения/assets используют детерминированные относительные ссылки там, где parser их предоставляет. Полная packaged-проверка остаётся `BLOCKED` до появления публичной воспроизводимой asset-bearing fixture.
- **Batch / Folder / ZIP.** Markdown встроен в существующие пакетные сценарии, защиту от конфликтов, Stop и ZIP output.
- **Стабильная диагностика.** Ошибки native worker преобразуются в app-owned коды и локализованные RU/EN сообщения.
- **Bundled native worker.** `zlet-anydoc-worker.exe` входит в Windows package. Версия worker protocol: `1.0`.
- **Pinned dependencies.** `anydoc 0.2.4` закреплён на revision `42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c`; Rust toolchain закреплён в репозитории.
- **Rust third-party notices.** Лицензии собираются из locked dependency graph без абсолютных путей машины сборки.
- **Resource safety.** Native worker проверяет лимит входного файла до чтения всего документа в память.

### Существующие маршруты сохранены

Legacy Office modernization остаётся отдельным маршрутом от Markdown:

| Исходник | Результат | Требование |
|---|---|---|
| `.doc` | `.docx` | установлен Microsoft Word |
| `.xls` | `.xlsx` | установлен Microsoft Excel |
| `.ppt` | `.pptx` | установлен Microsoft PowerPoint |

Существующие Excel CSV/TSV export, safe-copy операции, batch report, Folder/ZIP output, conflict protection, RU/EN интерфейс и локальные настройки сохраняются.

### Поддержка Markdown в v0.1.0

| Исходник | Markdown | Примечание |
|---|---|---|
| `.docx` | Да | bundled local native worker |
| legacy `.doc` | Provisional | direct local route реализован; packaged acceptance ждёт публичную legacy fixture |
| `.xlsx` | Да | локальный sheet-oriented route |
| legacy `.xls` | Provisional | direct local route реализован; packaged acceptance ждёт публичную legacy fixture |
| `.pptx` | Да | bundled local native worker |
| legacy `.ppt` | Provisional | известное ограничение table semantics; нужен отдельный packaged evidence |
| searchable `.pdf` | Да | лёгкий локальный route для обычных digital PDF |
| image-only/no-text scanned `.pdf` | Нет OCR в core | явная диагностика `pdf_specialist_required` |
| partially searchable / mixed OCR `.pdf` | Provisional | возможен частичный extracted text; нужна ручная проверка |
| `.txt` | Да | прямой локальный route |
| `.html`, `.htm` | Markdown не включён | без скрытого Python/Docling/cloud fallback |

### Важные ограничения

- Zlet Converter v0.1.0 всё ещё имеет зрелость **PRE-ALPHA** и не обещает универсальную lossless-конвертацию.
- Direct legacy `.doc` и `.xls` → Markdown реализованы, но не считаются полностью packaged-verified capability, пока acceptance не получит публичные воспроизводимые legacy fixtures.
- Direct legacy `.ppt` → Markdown может потерять семантику таблиц, если upstream parser уже превратил binary PowerPoint table в последовательный текст. Zlet не выдумывает утраченную структуру.
- Сложные multi-column/layout-heavy PDF остаются provisional capability и не заявляются как полностью решённый сценарий лёгкого route.
- Image-only/no-extractable-text scanned PDF явно получает specialist-required diagnostic. Partially searchable/mixed OCR PDF в v0.1.0 надёжно не классифицируется и может вернуть неполный extracted text.
- Companion asset export реализован, но end-to-end packaged preservation остаётся provisional до появления публичной asset-bearing fixture для Folder/ZIP/Stop проверок.
- OCR/scanned-PDF specialist не входит в core package.
- HTML → Markdown намеренно не включён в v0.1.0 до отдельной лёгкой локальной capability.
- Password-protected, encrypted, corrupted и неподдерживаемые документы могут завершиться явной ошибкой.
- Microsoft Office не входит в комплект и нужен только отдельным legacy modernization / Excel export сценариям, которым он уже требовался.
- Установщик остаётся неподписанным, поэтому Windows может показать Unknown publisher или SmartScreen.

### Приватность и граница продукта

- Конвертация local-first и не требует загрузки документов в облако.
- Обязательный аккаунт, backend, cloud service и LLM API не добавляются.
- v0.1.0 **не** добавляет chunking, metadata enrichment, embeddings, vector DB, retrieval, RAG pipeline или AI evaluation.

### Статус проверки

Release workflow обязан выполнить locked Rust tests/build, .NET restore/build/tests, win-x64 packaging, package validation и SHA-256 перед созданием release assets.

Полный packaged Windows acceptance является отдельным evidence-этапом и отслеживается в GitHub Issue #90. Provisional пункты со статусом `BLOCKED` остаются видимыми в evidence; сам факт публикации релиза не означает, что все clean-machine/manual acceptance проверки автоматически пройдены.
