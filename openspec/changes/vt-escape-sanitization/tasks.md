## 1. Sanitizer core

- [x] 1.1 Add internal `TextSanitizer` (in `src/Dcli/Internal/`) with an internal `SanitizeMode { Strip, Replace }` enum and the two-class transform from design Decision 3: Class A (C0 `0x00-0x08`/`0x0E-0x1F` incl. `ESC`, `DEL` `0x7F`, C1 `0x80-0x9F`) follows the mode; Class B whitespace controls (`\t \n \v \f \r`) always → single space `U+0020`.
- [x] 1.2 Implement `replace`-mode glyph mapping: C0 → Control Picture `U+2400`+offset, `DEL` → `U+2421`, C1 → `U+FFFD` (all width 1).
- [x] 1.3 Implement the allocation-free fast path (Decision 7): scan first; if no Class-A/B byte present, return the same string instance. Ensure idempotence (sanitized text re-sanitizes to itself via the fast path).
- [x] 1.4 Add `static readonly DefaultMode` initialized once from `DCLI_SANITIZE_MODE` (case-insensitive `strip`/`replace`; unset/empty/unknown → `strip`), plus `Apply(string)` (uses `DefaultMode`) and an internal `Apply(string, SanitizeMode)` overload for deterministic testing (Decision 5).
- [x] 1.5 Unit tests for `TextSanitizer`: each byte class in both modes, whitespace→space (incl. `"a\tb"`→`"a b"`, `"line1\nline2"`→`"line1 line2"`), printable/wide/emoji passthrough unchanged, fast-path returns same instance, idempotence, env-var parsing incl. unrecognized→strip.

## 2. Segment safe-by-default construction + Raw seam

- [x] 2.1 Convert `Segment` from a positional record to an explicit `record` preserving the source-compatible public ctor `Segment(string Text, Style Style = default)` as the sanitizing primary ctor (`Text` runs through `TextSanitizer.Apply`); keep `Style` defaulting to `default`.
- [x] 2.2 Make `Text` and `Style` **get-only** (not `init`) to close the `with`-expression bypass (Decision 6); add `Deconstruct(out string Text, out Style Style)` if any existing call site relies on positional deconstruction.
- [x] 2.3 Add `internal bool IsRaw` (default `false`) and a `private Segment(string text, Style style, bool raw)` ctor; expose `public static Segment Raw(string text, Style style = default)` that bypasses sanitization (null-checks `text`) and sets `IsRaw = true`. Confirm `IsRaw` participates in record value equality (Decision 2).
- [x] 2.4 Update `Segment` XML docs: replace the "stored and returned verbatim" wording with the safe-by-default contract; document `Raw` as the audited, dangerous opt-out.
- [x] 2.5 Unit tests: control bytes neutralized via `new Segment(...)` and `Line.FromText(...)`; `Segment.Raw` stores verbatim; `Segment.Raw("hi") != new Segment("hi")`; `with { Text = ... }` does not compile (verify via doc/comment + a compile-guard test that uses the copy ctor path); width measured on stored text.

## 3. Wire construction paths + LineBuilder.Raw

- [x] 3.1 Add `LineBuilder.Raw(string text, Style style = default)` that appends a raw segment; confirm all other `LineBuilder` append paths build segments through the sanitizing `Segment` ctor.
- [x] 3.2 Verify `Line.FromText`, the string-accepting `*Request` overloads (`DialogRequests.cs`), and the scrollback/status surfaces all funnel through the sanitizing `Segment` ctor (no path constructs segment text around it). Adjust any that bypass it.
- [x] 3.3 Unit tests confirming a string prompt/option/append carrying escape bytes yields neutralized segment text through each public surface.

## 4. End-to-end rendering safety tests

- [ ] 4.1 Via `Dcli.Testing.HeadlessTerminal` / `FrameSnapshot` / `InMemoryOutputSink`: assert consumer text containing `ESC`/CSI/OSC/newline never reaches the output sink except via `Segment.Raw`.
- [ ] 4.2 Sync-fence test: a frame whose content was built from text containing `"[?2026l"` emits exactly one sync mode-reset (the renderer's own fence close) and not the consumer's.
- [ ] 4.3 Raw passthrough test: `Segment.Raw("[31mred[0m")` reaches the sink byte-for-byte; width/wrapping consistency test across strip and replace modes (using the internal explicit-mode entry).

## 5. Docs, sample audit, release notes

- [ ] 5.1 Audit in-repo call sites (`samples/`, `Dcli.Demo`, `Dcli.Demo.DmonWizard`) for intentional raw-VT passthrough; convert any to `Segment.Raw` / `LineBuilder.Raw` (expected: none).
- [ ] 5.2 Add a release note: default construction now neutralizes control bytes; use `Segment.Raw` for verbatim; `DCLI_SANITIZE_MODE=replace` visualizes stripped bytes when debugging.
