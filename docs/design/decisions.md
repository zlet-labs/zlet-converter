# Design Decisions

## Workspace

Converter uses a workbench model.

The main screen is not a wizard.

The document queue is the primary workspace.

## Conversion naming

UI uses:

Conversion

Examples:

PDF → MD

DOC → DOCX → MD

XLS → XLSX → MD

## Quality

Do not use numeric quality scores.

Use states:

- Checked
- Review required
- Failed
- Not checked

## Navigation

Main:

- Convert
- History
- Settings

## Settings structure

- General
- Conversion
- Diagnostics
- About

## Theme

Light theme is primary.
Dark theme can use the same design tokens.
