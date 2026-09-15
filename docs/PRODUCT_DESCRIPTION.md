# Zlet Converter — Product Description

[English](#english) · [Русский](#русский)

## English

### Short description

**Zlet Converter is a local-first Windows utility for converting documents into high-quality Markdown, modernizing supported legacy Microsoft Office files, exporting Excel worksheets, and safely processing batches without cloud uploads.**

### Repository / catalog description

Zlet Converter helps process many files in folders and subfolders while keeping the work local on the user's PC. v0.1.0 introduces the primary product route **Documents → high-quality Markdown** for supported modern documents through a bundled local native worker, while retaining legacy Office modernization, Excel worksheet CSV/TSV export, safe-copy operations, relative folder structure, Folder/ZIP output, conflict protection and human-readable reporting.

The app is designed for simple self-serve use: choose a folder, review planned actions, filter or sort Preview, select what to process, choose Folder or ZIP output, run the batch, and inspect the result summary and persistent TXT report. No account, backend, cloud conversion service or LLM API is required.

### Markdown in v0.1.0

Primary packaged Markdown targets are DOCX, XLSX, PPTX, straightforward searchable PDF and TXT. Simple structures render as GFM; complex tables may use sanitized HTML inside Markdown when pure GFM would lose structure.

Image-only/no-extractable-text scanned PDF produces an explicit specialist-required diagnostic rather than claiming OCR. Partially searchable/mixed OCR PDFs remain provisional and may require manual review.

Direct legacy DOC/XLS/PPT → Markdown paths are implemented but remain packaged-acceptance provisional until public reproducible legacy fixtures are available. Companion asset export is implemented where the parser exposes assets, while end-to-end packaged Folder/ZIP/Stop asset preservation remains provisional until a public reproducible asset-bearing fixture is included in acceptance.

HTML → Markdown is intentionally not enabled in v0.1.0; there is no hidden cloud/Python/Docling fallback.

### Existing routes retained

- DOC → DOCX through installed Microsoft Word.
- XLS → XLSX through installed Microsoft Excel.
- PPT → PPTX through installed Microsoft PowerPoint.
- XLS/XLSX → one UTF-8 CSV or TSV per eligible worksheet through installed Microsoft Excel.
- Safe unchanged copy for supported already-compatible documents/media.
- JSON → TXT or Markdown conversion.

Microsoft Office is required only for the separate Office-dependent routes. Bundled modern Document → Markdown routes do not require Microsoft Office.

### Key points in v0.1.0

- Windows x64 desktop utility.
- Local processing only; documents are not uploaded for conversion.
- Russian and English UI in the same package, with in-app language switching that preserves current Preview/results.
- Documents → high-quality Markdown as the primary route.
- Bundled local `zlet-anydoc-worker.exe` with pinned anydoc dependency/revision.
- DOCX/XLSX/PPTX/searchable PDF/TXT Markdown routes.
- Explicit image-only scanned-PDF specialist-required diagnostics; no bundled OCR claim.
- Adaptive Markdown rendering with structure-preserving HTML fallback for complex tables where required.
- Companion asset export implemented but packaged preservation remains provisional pending a public asset-bearing fixture.
- Direct legacy DOC/XLS/PPT → Markdown remains provisional pending reproducible packaged evidence.
- Legacy DOC/XLS/PPT modernization through installed Microsoft Office remains available separately.
- XLS/XLSX per-worksheet UTF-8 CSV/TSV export remains available through installed Excel.
- Safe unchanged copy for supported compatible files.
- Folder and subfolder scanning with relative structure preservation.
- Preview filtering by format, visible clear-filter action, sortable columns and 1-based visible row numbering.
- Selection remains independent from filtering/sorting and controls the real execution set.
- Folder or ZIP output.
- Persistent `ZletConverter-report.txt` with relative paths, counters, worksheet accounting, statuses and safe diagnostics.
- Conflict protection: existing result files/directories are not silently overwritten.
- Per-file status, progress, source size and elapsed time.
- Safe batch cancellation that does not terminate unrelated user Office processes.
- PRE-ALPHA product maturity; complex, corrupted, password-protected or unsupported documents may fail explicitly.

### Current release classification

`v0.1.0` is prepared as a normal GitHub Release (`prerelease=false`). **PRE-ALPHA** describes product maturity only. Release publication and packaged Windows acceptance are separate gates; publication alone is not acceptance evidence.

### One-line GitHub About text

`Local-first Windows document converter: high-quality Markdown, legacy Office modernization and batch processing. No cloud uploads.`

## Русский

### Короткое описание

**Zlet Converter — local-first Windows-утилита для преобразования документов в качественный Markdown, модернизации поддерживаемых legacy Microsoft Office-файлов, экспорта листов Excel и безопасной пакетной обработки без загрузки документов в облако.**

### Описание продукта

Zlet Converter помогает обрабатывать сразу много файлов в папках и подпапках, оставляя работу на компьютере пользователя. v0.1.0 вводит основной продуктовый маршрут **Documents → high-quality Markdown** для поддерживаемых современных документов через bundled local native worker и сохраняет legacy Office modernization, Excel CSV/TSV export, safe-copy операции, относительную структуру каталогов, Folder/ZIP output, conflict protection и человекочитаемый отчёт.

Основной сценарий простой: выбрать папку, посмотреть план операций, отфильтровать или отсортировать Preview, отметить нужные файлы, выбрать Folder или ZIP output, запустить batch и проверить итоговую сводку и постоянный TXT-отчёт. Аккаунт, backend, cloud conversion service и LLM API не требуются.

### Markdown в v0.1.0

Основные packaged Markdown targets: DOCX, XLSX, PPTX, обычный searchable PDF и TXT. Простые структуры выводятся как GFM; для сложных таблиц допускается sanitized HTML внутри Markdown, когда чистый GFM потерял бы структуру.

Image-only/no-extractable-text scanned PDF получает явную specialist-required диагностику без ложного заявления OCR. Partially searchable/mixed OCR PDF остаются provisional и могут требовать ручной проверки.

Direct legacy DOC/XLS/PPT → Markdown routes реализованы, но packaged acceptance остаётся provisional до появления публичных воспроизводимых legacy fixtures. Companion asset export реализован там, где parser предоставляет assets, а end-to-end packaged preservation для Folder/ZIP/Stop остаётся provisional до появления публичной asset-bearing fixture в acceptance.

HTML → Markdown намеренно не включён в v0.1.0; скрытого cloud/Python/Docling fallback нет.

### Существующие маршруты сохранены

- DOC → DOCX через установленный Microsoft Word.
- XLS → XLSX через установленный Microsoft Excel.
- PPT → PPTX через установленный Microsoft PowerPoint.
- XLS/XLSX → отдельный UTF-8 CSV или TSV для каждого eligible worksheet через установленный Microsoft Excel.
- Безопасное копирование поддерживаемых уже совместимых документов/медиа без изменений.
- JSON → TXT или Markdown.

Microsoft Office требуется только отдельным Office-dependent routes. Bundled современные Document → Markdown routes не требуют Microsoft Office.

### Основные возможности v0.1.0

- Windows x64 desktop-утилита.
- Полностью локальная обработка; документы не загружаются в облако для конвертации.
- Русский и английский интерфейс в одном пакете с переключением языка без потери текущего Preview/результатов.
- Documents → high-quality Markdown как основной маршрут.
- Bundled local `zlet-anydoc-worker.exe` с pinned anydoc dependency/revision.
- DOCX/XLSX/PPTX/searchable PDF/TXT → Markdown.
- Явная specialist-required диагностика image-only scanned PDF; bundled OCR не заявляется.
- Adaptive Markdown rendering с structure-preserving HTML fallback для сложных таблиц при необходимости.
- Companion asset export реализован, но packaged preservation остаётся provisional до публичной asset-bearing fixture.
- Direct legacy DOC/XLS/PPT → Markdown остаётся provisional до воспроизводимого packaged evidence.
- Legacy DOC/XLS/PPT modernization через установленный Microsoft Office сохраняется отдельным маршрутом.
- XLS/XLSX → per-worksheet UTF-8 CSV/TSV через установленный Excel сохраняется.
- Безопасное копирование поддерживаемых совместимых файлов без изменений.
- Сканирование папок и подпапок с сохранением относительной структуры.
- Фильтрация Preview по форматам, видимое действие очистки фильтра, сортировка колонок и нумерация видимых строк с 1.
- Фильтр/сортировка не меняют checkbox selection и реальный execution set.
- Folder или ZIP output.
- Постоянный `ZletConverter-report.txt` с относительными путями, счётчиками, статистикой листов, статусами и безопасной диагностикой.
- Защита от конфликтов: существующие файлы/каталоги результата не перезаписываются молча.
- Статус, прогресс, размер исходника и время выполнения по каждому файлу.
- Безопасная остановка batch без завершения посторонних пользовательских процессов Office.
- Зрелость PRE-ALPHA: сложные, повреждённые, password-protected или неподдерживаемые документы могут завершиться явной ошибкой/ограничением.

### Классификация текущего релиза

`v0.1.0` готовится как обычный GitHub Release (`prerelease=false`). **PRE-ALPHA** обозначает только зрелость продукта. Публикация релиза и packaged Windows acceptance — разные gates; сама публикация не является acceptance evidence.

### Короткое описание для GitHub About

`Local-first Windows document converter: качественный Markdown, legacy Office modernization и batch processing. Без облачной загрузки документов.`
