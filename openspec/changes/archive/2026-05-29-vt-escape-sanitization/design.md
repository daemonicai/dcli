## Context

`Segment.Text` is emitted to the terminal verbatim at exactly one site —
`VtFrameRenderer.EmitLine` (`src/Dcli/Internal/RenderLoop/VtFrameRenderer.cs:274`,
`_buf.Append(segment.Text)`). The `Style` side is fully sealed (`Style` = `Format` flags + `Color`;
`SgrTranslator` only ever emits known SGR codes), so `Segment.Text` is the **only** path by which
untrusted bytes can reach the output stream.

`DisplayWidth.Measure` counts C0/C1/DEL as width 0 (`DisplayWidth.cs:42-49`) and `LineWrapper` simply
sums per-rune widths. Injected control bytes are therefore *invisible to the layout math* but *active at
the terminal*: a consumer string can carry `ESC[?2026l` (close the per-frame sync fence early), a raw
`\n`/`\r`/`ESC[A` (permanently desync the painter's `_lastRestingRow` cross-frame cursor accounting),
`ESC[2J`/`ESC[3J` (wipe screen/scrollback), or OSC sequences (OSC 52 clipboard write, OSC 8 hyperlink,
OSC 0 title spoof). The current behaviour is *correct per the committed `Segment` "stored verbatim"
contract* — which is precisely why closing it is a contract change and needs its own OpenSpec change.

The decided shape (agreed with the user before this design): **safe by default + an audited opt-out**.
Neutralize at construction; expose `Segment.Raw(...)` as the single trusted seam; make the transform
env-var-configurable with strip as the default.

## Goals / Non-Goals

**Goals:**
- A `Segment` produced through any ordinary construction path holds **terminal-safe** text — no control
  byte reaches the writer through it.
- Exactly one audited seam (`Segment.Raw`) can carry verbatim bytes to `VtFrameRenderer:274`.
- Stored text == measured text == emitted text, so `DisplayWidth`, `LineWrapper`, and `Line`/`Segment`
  equality remain automatically consistent with no changes to those types.
- The neutralization transform is configurable (env var), defaulting to silent strip; an opt-in
  "replace with a visible glyph" mode aids debugging.
- The common case (text with no control bytes) allocates nothing extra.

**Non-Goals:**
- Input-side VT handling (`VtInputParser`) — separate capability, untouched.
- The scrollback oversized-reprint ordering gap — tracked separately.
- A markup parser or any interpretation of bracketed/escape syntax (the no-markup stance stands).
- Column-accurate tab expansion at wrap time (see Decision 4).
- Making `VtFrameRenderer` itself sanitize — sanitization is a construction-time *styled-text* property,
  not a render-loop one; the renderer keeps appending the (now-safe) stored text verbatim.

## Decisions

### Decision 1 — Sanitize at construction, in one chokepoint
All public construction funnels through the `Segment` primary constructor. `Line.FromText`,
`LineBuilder`, the `*Request` string-prompt overloads, and the scrollback/status surfaces already build
their `Segment`s through that ctor, so sanitizing there covers every path without per-call-site work.

*Alternative rejected — sanitize on emit (in `VtFrameRenderer`):* keeps `Segment` "verbatim" but makes
*stored text ≠ emitted text*, so `DisplayWidth`/wrapping would measure bytes that never render. It also
re-runs the transform every frame on the hot path. Construction-time is measured-once and keeps every
downstream type honest.

### Decision 2 — `Segment.Raw` is the only trusted seam, and it participates in value equality
Add `public static Segment Raw(string text, Style style = default)` that bypasses the sanitizer and
stores text verbatim. Add a matching `LineBuilder.Raw(string text, Style style = default)` for
multi-segment lines. `Segment` carries an `internal bool IsRaw` set only by `Raw`.

`IsRaw` **participates in record value equality** (record-synthesized equality compares all instance
fields, including the internal backing field — so this is automatic). Rationale: a raw segment is
semantically distinct — it may carry bytes a sanitized segment never could — and must not compare equal
to a sanitized one in equality-based logic or golden-frame assertions.

`Segment` is converted from a positional record to an explicit `record` that **preserves the
source-compatible public constructor** `new Segment(string Text, Style Style = default)` (the primary,
sanitizing ctor) plus a `private Segment(string, Style, bool raw)` used by `Raw`. A `Deconstruct(out
string Text, out Style Style)` is added if any existing call site relies on positional deconstruction.

*Alternative rejected — no escape hatch:* a rendering library will legitimately need to forward
pre-rendered ANSI (e.g. a consumer piping colored tool output). Without `Raw` they would have no
recourse; with it, the dangerous path is named, greppable, and documented.

### Decision 3 — Two byte classes, env-var-controlled transform for the dangerous class
Neutralization splits the offending bytes into two classes:

- **Class A — control/escape (security-relevant):** C0 except the whitespace controls
  (`0x00–0x08`, `0x0E–0x1F`, which includes `ESC 0x1B`), `DEL 0x7F`, and C1 (`0x80–0x9F`). These follow
  the configurable **mode**:
  - `strip` (**default**) — removed entirely.
  - `replace` — mapped to a visible, width-1 glyph: C0 → its Unicode Control Picture
    (`U+2400`–`U+241F`), `DEL` → `U+2421`, C1 → `U+FFFD`.
- **Class B — whitespace controls (layout-relevant, benign):** TAB `0x09`, LF `0x0A`, VT `0x0B`,
  FF `0x0C`, CR `0x0D`. These are **always replaced by a single space `U+0020`**, regardless of mode.
  Rationale: they only threaten *layout* (cursor movement / extra rows breaking the one-`Line`-per-row
  painter invariant), not security; a single space preserves token separation (`"a\tb"` → `"a b"`, not
  `"ab"`) and gives a deterministic width 1.

This two-class split is the core principle: *dangerous escapes obey the security knob; positional
whitespace is normalized to a space.*

*Alternative rejected — one rule for all controls:* simpler, but silently deleting tabs/newlines
word-joins content (`"a\tb"` → `"ab"`), and putting newlines under the mode would let `replace` emit a
glyph where a space reads far better.

### Decision 4 — Tab is normalized to a space, not column-expanded
At construction the column is unknown (segments are concatenated and wrapped later), so column-accurate
tab expansion is impossible there. Tab joins Class B → single space. Consequence: `DisplayWidth`'s
existing `TabWidth=8` expansion becomes reachable only via `Segment.Raw` content, where width is the
consumer's responsibility. A future change could add column-aware expansion at wrap time if a real need
appears; out of scope here.

### Decision 5 — Env var: `DCLI_SANITIZE_MODE`, read once and cached
- Variable: `DCLI_SANITIZE_MODE`. Values (case-insensitive): `strip` (default) and `replace`. Unset,
  empty, or unrecognized → `strip`.
- Read **once** and cached in a `static readonly` field on the internal sanitizer
  (`TextSanitizer.DefaultMode`), initialized from `Environment.GetEnvironmentVariable` at type
  initialization. Reading per-`Segment` is wasteful and per-process config is the right granularity.
- **Testability:** the sanitizer exposes an internal `Apply(string text, SanitizeMode mode)` overload
  that takes the mode explicitly; tests exercise both modes through it deterministically without
  touching process env. The public `Segment` path calls `Apply(text)` which uses the cached
  `DefaultMode`.

### Decision 6 — Properties are get-only to close the `with`-expression bypass
`Segment.Text` and `Style` are **get-only** (not `init`). A `with` expression copies properties through
the synthesized copy constructor, *bypassing the sanitizing primary ctor* — so an `init`-settable
`Text` would let `segment with { Text = untrusted }` smuggle raw bytes back in. Get-only members make
`segment with { Text = ... }` a **compile error**, which is the intended outcome; the copy constructor
still produces faithful copies (preserving `IsRaw`). To build a modified segment, callers construct a
new `Segment` (which re-sanitizes) or use `Segment.Raw`.

### Decision 7 — Allocation-free fast path
`TextSanitizer.Apply` first scans for any byte needing neutralization; if none, it returns the **same
string instance** (no allocation, no copy). Only strings actually containing control bytes are rebuilt.
Sanitization is idempotent: a previously-sanitized string contains no Class-A/B bytes, so it hits the
fast path on any re-entry.

## Risks / Trade-offs

- **[Contract break: `Segment` no longer round-trips arbitrary text]** → Documented as BREAKING in the
  proposal and in `Segment` XML docs. Behaviourally invisible to callers passing ordinary printable
  text; only control bytes change. The `Raw` seam covers deliberate passthrough.
- **[Silent data loss in `strip` mode]** → By design (user-chosen default). The `replace` mode exists
  precisely so a developer debugging "where did my text go" can switch on visible glyphs via one env
  var.
- **[A consumer relying on raw ANSI passthrough breaks]** → Migration: move those sites to
  `Segment.Raw` / `LineBuilder.Raw`. The break is a compile-clean behavioural change (their escapes
  vanish in `strip`), so it is discoverable; calling it out in release notes is part of the work.
- **[`Raw` re-opens the full injection surface]** → Accepted and contained: `Raw` is the single audited
  seam, greppable, and documented as "you now own terminal integrity for this text." It does not widen
  the *default* surface at all.
- **[Per-`Segment` scan cost]** → Mitigated by the allocation-free fast path (Decision 7); the scan is
  O(n) over text that is already being measured by `DisplayWidth` anyway.

## Migration Plan

1. Land the sanitizer + `Segment`/`LineBuilder` changes (additive `Raw`, behavioural change to default
   construction).
2. Audit in-repo call sites (samples, `Dcli.Demo`, `Dcli.Demo.DmonWizard`) for any intentional raw-VT
   passthrough; convert to `Raw` where found (expected: none in the library's own surface).
3. Note in release notes: default construction now strips control bytes; use `Segment.Raw` for
   verbatim, and `DCLI_SANITIZE_MODE=replace` to visualize stripped bytes while debugging.
4. Rollback: revert the change; no persisted state or data migration is involved.

## Open Questions

_None — Decisions 1–7 resolve the items deferred from the proposal (env var name/values/caching, tab
handling, raw-flag equality, `with`-bypass, transform per byte class)._
