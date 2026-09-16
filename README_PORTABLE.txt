Zlet Converter v0.1.0
=====================

Архив: ZletConverter-v0.1.0-win-x64.zip
Статус: PRE-ALPHA

1. Полностью распакуйте ZIP в обычную локальную папку.
2. Запустите ZletConverter.exe.
3. Не отделяйте ZletConverter.exe и zlet-anydoc-worker.exe от остальных файлов
   распакованной папки.

При первом запуске выберите Русский или English. Позже язык можно сменить без
перезапуска через Настройки -> Язык. Выбор хранится локально в
%LOCALAPPDATA%\Zlet Labs\Zlet Converter\settings.json; аккаунт и облако не нужны.

Приложение работает локально и не отправляет документы в облачный сервис.
.NET 8 входит в self-contained пакет. Native Markdown worker также входит в ZIP.

Основной маршрут v0.1.0 — Documents -> Markdown:
- DOCX -> Markdown: локальный anydoc worker + Zlet renderer;
- legacy DOC -> Markdown: direct local path реализован, но packaged acceptance
  v0.1.0 остаётся provisional до публичной воспроизводимой legacy fixture;
- XLSX -> Markdown: локальный anydoc/Calamine route;
- legacy XLS -> Markdown: direct local path реализован, но packaged acceptance
  v0.1.0 остаётся provisional;
- PPTX -> Markdown: локальный anydoc route;
- legacy PPT -> Markdown: provisional route с известным ограничением table
  semantics;
- обычный searchable PDF -> Markdown: локальный anydoc route;
- image-only/no-extractable-text scanned PDF -> явная диагностика
  pdf_specialist_required;
- partially searchable/mixed OCR PDF в v0.1.0 надёжно не классифицируется:
  доступный текст может дать частичный Markdown, который нужно проверять вручную;
- TXT -> Markdown: прямой локальный route.

Для сложных таблиц Markdown renderer может использовать локальный HTML fallback
внутри Markdown, если чистый GFM не может сохранить структуру без потерь.
Companion image/asset export реализован с относительными ссылками там, где parser
предоставляет assets, но полная packaged-проверка Folder/ZIP/Stop остаётся
provisional до появления публичной asset-bearing fixture.

HTML -> Markdown в v0.1.0 намеренно не включён: приложение не подменяет
отсутствующую локальную capability скрытым Python/Docling/cloud fallback.
Advanced PDF/OCR specialist component также не входит в core package v0.1.0.

Legacy Office modernization остаётся отдельным маршрутом:
- DOC -> DOCX: Microsoft Word;
- XLS -> XLSX: Microsoft Excel;
- PPT -> PPTX: Microsoft PowerPoint.

Для экспорта листов Excel также нужен Microsoft Excel:
- XLS/XLSX -> отдельный UTF-8 CSV для каждого листа;
- XLS/XLSX -> отдельный UTF-8 TSV для каждого листа.

Каждое Office-приложение необязательно и влияет только на операции, которым оно
действительно требуется. Современные Document -> Markdown маршруты через bundled
anydoc worker не требуют Microsoft Office.

Без Office также сохраняются существующие безопасные локальные операции копирования
поддерживаемых файлов. JSON можно преобразовать в TXT или Markdown.

После обработки Zlet Converter создаёт человекочитаемый ZletConverter-report.txt
с относительными путями, счётчиками и безопасной диагностикой. Доступен вывод
в папку или ZIP; существующие результаты не перезаписываются молча.

Microsoft Office не входит в комплект. Python, Java, обязательный аккаунт,
облачный API и LLM API для основной конвертации не используются.

Установщик Zlet Converter пока не подписан. Windows может показать Unknown
publisher или предупреждение SmartScreen.

PRE-ALPHA означает, что результат нужно проверять на важных документах. Не
заявляется универсальная lossless-конвертация. Сложные, повреждённые,
зашифрованные или защищённые паролем документы могут завершиться явной ошибкой
или ограничением capability.

Известное ограничение: direct legacy PPT -> Markdown может потерять табличную
семантику, если upstream parser уже представил таблицу как последовательный
текст. Zlet не пытается выдумывать потерянную структуру.

Лицензионные сведения находятся в THIRD_PARTY_NOTICES.md и сопутствующих файлах
licenses в packaged artifact.
