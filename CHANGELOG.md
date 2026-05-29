# Changelog

All notable changes to dcli are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Security

- **VT-escape injection gap closed.** Consumer text passed to `Segment`, `Line.FromText`, or
  `LineBuilder` is now sanitized at construction. A raw escape sequence embedded in user-supplied
  text could previously defeat the synchronized-output fence, reposition the cursor, or trigger
  an OSC handler — silently breaking dcli's rendering invariants.

### Changed

- **BREAKING (behavioural) — `Segment` text is sanitized at construction.** Control and escape
  bytes in the `text` argument are neutralized before storage:
  - Whitespace controls (`\t \n \v \f \r`) are replaced with a single space.
  - Other C0 bytes (`U+0000–U+0008`, `U+000E–U+001F`), DEL (`U+007F`), and C1 bytes
    (`U+0080–U+009F`) are stripped.
  - Ordinary printable text — multi-byte UTF-8, CJK, emoji, combining marks — is unaffected.

  Code that previously relied on control bytes passing through unchanged (e.g. embedding raw
  ANSI in a `Segment` string) must switch to `Segment.Raw` / `LineBuilder.Raw`.

### Added

- **`Segment.Raw(text, style)`** — constructs a `Segment` that skips sanitization entirely.
  Use only when you are emitting pre-rendered ANSI and own responsibility for terminal integrity
  (unclosed SGR/OSC sequences will corrupt output).

- **`LineBuilder.Raw(text, style?)`** — appends a raw (unsanitized) segment to a line under
  construction, for mixing sanitized and verbatim runs in a single `Line`.

- **`DCLI_SANITIZE_MODE` environment variable** — controls what happens to neutralized bytes:
  - `strip` (default) — stripped bytes are silently removed.
  - `replace` — stripped bytes are substituted with their Unicode Control-Picture glyph
    (e.g. `ESC` → `␛`, `NUL` → `␀`). Set this when debugging "where did my text go" problems.

---

## [0.2.0-rc.2] — previous release

> Release notes for earlier versions not yet migrated to this file.
