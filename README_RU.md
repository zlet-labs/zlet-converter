# Zlet Converter

<p align="center">
  <img src="docs/assets/zlet-batch-converter-hero.svg" alt="Zlet Converter — локальный конвертер файлов для Windows от Zlet Labs" width="100%">
</p>

<p align="center">
  <a href="README.md">English</a> · <strong>Русский</strong>
</p>

<p align="center">
  <img alt="Версия v0.1.0" src="https://img.shields.io/badge/version-v0.1.0-2563eb">
  <img alt="PRE-ALPHA" src="https://img.shields.io/badge/status-PRE--ALPHA-f59e0b">
  <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows%20x64-0078D4">
  <img alt="Локальная обработка" src="https://img.shields.io/badge/processing-local%20only-16a34a">
  <img alt="MIT License" src="https://img.shields.io/badge/license-MIT-22c55e">
</p>

Zlet Converter — privacy-first/local-first приложение для Windows, которое преобразует документы в качественный Markdown, модернизирует поддерживаемые legacy Office-файлы и пакетно обрабатывает папки/подпапки без обязательного аккаунта и облачной конвертации.

Один пакет приложения поддерживает русский и английский интерфейс. При первом запуске язык нужно подтвердить явно; позже его можно сразу сменить через **Настройки → Язык**, не перезапуская приложение и не теряя текущий Preview/результаты. Сохраняется только настройка языка в `%LOCALAPPDATA%\Zlet Labs\Zlet Converter\settings.json`; аккаунт, облако и backend не требуются. Для bootstrap доступен запуск `ZletConverter.exe --language=ru-RU` или `--language=en-US`.

> **v0.1.0 имеет зрелость PRE-ALPHA, но публикуется как обычный GitHub Release, а не GitHub Pre-release.** PRE-ALPHA означает зрелость продукта. Установщик пока не подписан Authenticode, поэтому Windows может показать Unknown publisher или предупреждение SmartScreen. Microsoft Office в комплект не входит.

> **О переименовании:** v0.0.2 был опубликован под прежним публичным названием `Zlet Batch Converter`. Его исторические название релиза и имена assets остаются без изменений. v0.0.3 и более новые версии используют актуальное название `Zlet Converter` / `ZletConverter`.

## Скачать v0.1.0

| Установщик Windows | Portable ZIP |
|---|---|
| **[⬇ Скачать установщик](https://github.com/zlet-labs/zlet-converter/releases/download/v0.1.0/ZletConverter-v0.1.0-Setup-win-x64.exe)** | **[📦 Скачать portable](https://github.com/zlet-labs/zlet-converter/releases/download/v0.1.0/ZletConverter-v0.1.0-win-x64.zip)** |
| `ZletConverter-v0.1.0-Setup-win-x64.exe` | `ZletConverter-v0.1.0-win-x64.zip` |

[Описание релиза](https://github.com/zlet-labs/zlet-converter/releases/tag/v0.1.0) · [SHA-256](https://github.com/zlet-labs/zlet-converter/releases/download/v0.1.0/SHA256SUMS.txt)

### Зачем использовать

| 🔒 Локально | ⚡ Пакетно | 🛡 Осторожно с файлами |
|---|---|---|
| Без аккаунтов и загрузки документов в облако | Папки и подпапки за один запуск | Существующие результаты не перезаписываются молча |
| Содержимое документов остаётся на ПК | Много документов → Markdown за один batch | Явная диагностика вместо тихой потери качества |

## Documents → Markdown

v0.1.0 содержит первую серьёзную реализацию основного маршрута Zlet Converter: **Documents → high-quality Markdown**.

В пакет входит локальный native worker `zlet-anydoc-worker.exe` на базе закреплённого `anydoc 0.2.4`. Routing, диагностика и контракт Markdown принадлежат приложению Zlet, а не конкретному движку.

Простые структуры выводятся как обычный GFM Markdown. Если сложную таблицу нельзя сохранить в GFM без потери merged-cell или nested structure, Zlet может использовать HTML внутри Markdown вместо красивого, но неверного упрощения.

Companion image/asset export реализован там, где parser предоставляет assets, и использует относительные локальные ссылки. End-to-end packaged preservation в v0.1.0 остаётся **provisional**, пока acceptance не получит публичную воспроизводимую asset-bearing fixture; наличие unit coverage само по себе не считается PASS.

## Поддерживаемые форматы в v0.1.0

### Markdown

| Исходник | Результат | Требование / статус |
|---|---|---|
| `.docx` | `.md` + companion assets при наличии | bundled local native worker |
| legacy `.doc` | `.md` | provisional direct local path; packaged acceptance ждёт публичную legacy fixture |
| `.xlsx` | `.md` | bundled local native worker; displayed/cached values |
| legacy `.xls` | `.md` | provisional direct local path; packaged acceptance ждёт публичную legacy fixture |
| `.pptx` | `.md` + companion assets при наличии | bundled local native worker |
| legacy `.ppt` | `.md` | provisional; известное ограничение table semantics |
| обычный searchable `.pdf` | `.md` | bundled local native worker |
| image-only/no-text scanned `.pdf` | specialist-required diagnostic | явный `pdf_specialist_required`; OCR в core нет |
| partially searchable / mixed OCR `.pdf` | provisional | случайно доступный текст может дать частичный Markdown; нужна ручная проверка |
| `.txt` | `.md` | прямой локальный route |
| `.html`, `.htm` | Markdown не включён в v0.1.0 | explicit unsupported capability; без скрытого cloud/Python fallback |
| `.json` | `.md` или `.txt` | существующий локальный route |

### Legacy Office modernization и существующие utilities

| Исходник | Результат | Требование |
|---|---|---|
| `.doc` | `.docx` | установлен Microsoft Word |
| `.xls` | `.xlsx` | установлен Microsoft Excel |
| `.ppt` | `.pptx` | установлен Microsoft PowerPoint |
| `.xls`, `.xlsx` | отдельный UTF-8 `.csv` для каждого листа | установлен Microsoft Excel |
| `.xls`, `.xlsx` | отдельный UTF-8 `.tsv` для каждого листа | установлен Microsoft Excel |
| поддерживаемые уже совместимые файлы | безопасная копия без изменений | Office не нужен там, где конвертация не требуется |

При экспорте Excel каждый лист становится отдельной операцией Preview. Скрытые и very-hidden листы видны, но не выбраны по умолчанию; полностью пустые листы явно пропускаются. Имена результата детерминированы и безопасны для Windows, например `sales__Summary.csv`.

Zlet Converter создаёт человекочитаемый `ZletConverter-report.txt` с относительными путями, итоговыми счётчиками, статистикой листов, статусами и безопасной диагностикой. Существующий отчёт не перезаписывается: используются суффиксы `-2`, `-3` и далее.

Итоговая панель сохраняет общую статистику листов и сводку по каждой книге, включая пропущенные скрытые и пустые листы. Книга считается одним исходным файлом независимо от числа результатов. Отчёт находится в папке результата или в корне ZIP, в том числе после остановки и частичных ошибок. Ошибка записи отчёта остаётся видимой. Элементы и подписи отчёта используют существующую настройку RU/EN; смена языка сохраняет Preview и результаты.

Preview можно фильтровать нажатием строк форматов в Rules без изменения checkbox selection и фактического набора файлов для обработки. Видимое действие **Показать все** очищает активный фильтр. Колонки исходного файла, действия, статуса, результата, размера и времени сортируются по возрастанию/убыванию, а видимые строки нумеруются с 1 по текущему порядку после filter + sort.

Тесты с настоящим Excel запускаются только явно через `ZLET_OFFICE_INTEGRATION=1` и локальные non-sensitive fixtures. Автоматические тесты не заменяют полную clean-machine проверку и реальные Microsoft Office integration tests.

Word, Excel и PowerPoint определяются независимо. Если одно приложение отсутствует, недоступна только связанная с ним Office-dependent операция. Bundled современные маршруты Document → Markdown не требуют Microsoft Office.

> **Безопасность PowerPoint:** legacy PPT modernization не запускается, пока у пользователя уже открыт PowerPoint. Markdown через native document worker является отдельным маршрутом.

## Быстрый старт

1. Скачайте установщик или portable ZIP выше.
2. Запустите `ZletConverter.exe`.
3. Выберите исходную папку и выполните сканирование.
4. Проверьте Preview и отметьте нужные операции.
5. Выберите Markdown или другой доступный target, затем Folder/ZIP output.
6. Запустите batch и проверьте результаты и диагностику по файлам.

Готовые сборки self-contained для .NET 8, поэтому отдельно устанавливать .NET Runtime не требуется.

## Что получает пользователь

- Локальную конвертацию Document → Markdown без обязательного cloud service и LLM API.
- Routing по format/capability за единым app-owned conversion contract.
- Adaptive Markdown rendering для списков, таблиц, ссылок и assets, доступных parser.
- Явную диагностику, если capability безопасно недоступна.
- Preview до начала обработки с фильтрацией, сортировкой и нумерацией строк.
- Выбор отдельных операций перед запуском.
- Сохранение относительной структуры подпапок в результате.
- Статус, stage-based progress, размер исходника и время выполнения по каждому файлу.
- Безопасную кнопку Stop, которая не запускает следующие операции из очереди и сохраняет уже завершённые результаты.
- Раздельные итоговые счётчики converted, copied, failed, conflict, unavailable, skipped и unselected.
- Постоянный человекочитаемый `ZletConverter-report.txt` для folder/ZIP output.

## Ограничения

Zlet Converter всё ещё находится в статусе **PRE-ALPHA** и не обещает универсальную lossless-конвертацию.

- Direct legacy `.doc` и `.xls` → Markdown реализованы, но packaged acceptance v0.1.0 считает их provisional до появления публичных воспроизводимых legacy fixtures.
- Direct legacy `.ppt` → Markdown может потерять семантику таблицы, если upstream parser уже представил binary PowerPoint table только как последовательный текст. Zlet не выдумывает потерянную структуру.
- Complex multi-column/layout-heavy PDF остаётся provisional capability; v0.1.0 не заявляет, что лёгкий route полностью решает такие документы.
- Image-only/no-extractable-text scanned PDF получает specialist-required diagnostic. Partially searchable/mixed OCR PDF в v0.1.0 надёжно не классифицируется и может вернуть неполный extracted text, поэтому результат требует ручной проверки.
- Companion asset export реализован, но packaged Folder/ZIP/Stop preservation остаётся provisional до появления публичной asset-bearing fixture в clean-machine acceptance.
- HTML → Markdown намеренно отключён в v0.1.0 до квалификации отдельного лёгкого локального route.
- Password-protected, encrypted, corrupted и неподдерживаемые документы могут завершиться явной ошибкой.

Исходные файлы не должны намеренно изменяться, но для важных данных при тестировании PRE-ALPHA ПО рекомендуется иметь резервную копию.

<details>
<summary><strong>Подробно о безопасности и приватности</strong></summary>

Конвертер специально сделан local-first и осторожным по отношению к пользовательским файлам.

- Файлы обрабатываются локально и не загружаются в облако для конвертации.
- Обязательный аккаунт, backend, cloud conversion service и LLM API не нужны.
- UI-процесс не выполняет Office COM automation напрямую.
- Office modernization идёт через изолированный STA worker-процесс.
- Native Markdown worker поставляется локально и использует закреплённый dependency graph.
- Legacy-файлы и книги Excel для экспорта листов открываются read-only там, где это требуется.
- Исходник должен оставаться внутри выбранной исходной папки.
- Reparse-point файлы и папки пропускаются.
- SHA-256 исходника проверяется до и после обработки там, где этого требует существующий safe-operation contract.
- Результат сначала создаётся во временном/staging расположении и проверяется перед финальным перемещением.
- Существующие файлы и каталоги результата не перезаписываются.
- Office-процессы никогда не завершаются только по имени процесса.
- TXT-отчёты используют относительные пути и не должны содержать содержимое документов, пароли, secrets или tokens.

Техническая диагностика может содержать коды ошибок и служебные данные процесса. Она не должна содержать содержимое документов, секреты или полные локальные пути документов.

</details>

<details>
<summary><strong>Сборка из исходников и локальная упаковка</strong></summary>

Требования:

- Windows x64
- .NET 8 SDK
- Rust 1.88.0 (зафиксирован в `rust-toolchain.toml`)

Сначала проверьте и соберите нативный Markdown worker, затем решение .NET:

```powershell
cargo test --manifest-path src/Zlet.FolderConverter.AnydocWorker/Cargo.toml --locked
cargo build --manifest-path src/Zlet.FolderConverter.AnydocWorker/Cargo.toml --release --locked
dotnet restore FolderConverter.sln
dotnet build FolderConverter.sln -c Release
dotnet test FolderConverter.sln -c Release
```

Если репозиторий был клонирован до переименования в `zlet-converter`, обновите существующий `origin`:

```powershell
git remote set-url origin https://github.com/zlet-labs/zlet-converter.git
```

Сборка portable-пакета:

```powershell
.\scripts\publish-portable.ps1
```

Ожидаемый локальный ZIP для текущей версии исходников:

```text
artifacts/portable/win-x64/ZletConverter-v0.1.0-win-x64.zip
```

Сборка Windows installer через Inno Setup 6:

```powershell
.\scripts\build-installer.ps1
```

Скрипт установщика собирает portable payload, затем создаёт Windows x64 installer и выводит его SHA-256 и статус Authenticode.

</details>

<details>
<summary><strong>Реальные Microsoft Office integration tests</strong></summary>

Реальные интеграционные тесты Office запускаются только явно, потому что требуют установленный Microsoft Office и настоящие legacy-файлы:

```powershell
$env:ZLET_OFFICE_INTEGRATION = "1"
$env:ZLET_OFFICE_WORD_FIXTURE = "C:\fixtures\sample.doc"
$env:ZLET_OFFICE_WORD_BATCH_FIXTURE_DIR = "C:\fixtures\word-batch"
$env:ZLET_OFFICE_EXCEL_FIXTURE = "C:\fixtures\sample.xls"
$env:ZLET_OFFICE_POWERPOINT_FIXTURE = "C:\fixtures\sample.ppt"
dotnet test FolderConverter.sln -c Release --filter Category=OfficeIntegration
```

Если нужное приложение Office или фикстура отсутствует, соответствующий integration test пропускается, а не считается успешно пройденным.

</details>

## Проект

Zlet Converter — проект **Zlet Labs**: небольшие, практичные, self-serve инструменты без лишней SaaS-машины.

[Zlet Labs](https://zlet.app/) · [GitHub Issues](https://github.com/zlet-labs/zlet-converter/issues) · [Все релизы](https://github.com/zlet-labs/zlet-converter/releases) · [MIT License](LICENSE)

Описание релиза: [docs/RELEASE_NOTES_v0.1.0.md](docs/RELEASE_NOTES_v0.1.0.md) · Чек-лист ручной проверки: [docs/manual-clean-machine-verification-v0.1.0.md](docs/manual-clean-machine-verification-v0.1.0.md)
