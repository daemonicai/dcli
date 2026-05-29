## Why

Consumer-supplied text flows into the terminal **verbatim** through `Segment.Text`. Control bytes
(C0/C1/DEL, lone `ESC`, raw newlines) are emitted unchanged and — because `DisplayWidth` counts them
as width 0 — are *invisible to the layout math* while remaining *active at the terminal*. A consumer
that forwards untrusted text into a `Segment` can therefore inject arbitrary VT: close the per-frame
synchronized-output fence (`ESC[?2026l`), permanently desync the painter's cross-frame cursor
accounting with a raw `\n`/`ESC[A`, wipe the screen/scrollback (`ESC[2J`/`ESC[3J`), or run OSC sequences
(clipboard write via OSC 52, hyperlink/title injection). A terminal-rendering library that owns the
output stream must guarantee its own integrity rather than trust every caller; today it does not.

## What Changes

- **BREAKING (contract):** `Segment` text is no longer "stored and returned verbatim". On
  construction, control bytes are **neutralized** so the stored text is terminal-safe. This changes the
  documented styled-text contract and the value a `Segment` round-trips (e.g. an embedded `ESC` is gone
  or replaced). Behaviourally non-breaking for any caller passing ordinary printable text.
- Neutralization happens at the **single construction chokepoint** all public creation paths funnel
  through (`Segment` ctor, `Line.FromText`, `LineBuilder`, the `*Request` prompts, scrollback/status
  surfaces). Stored text == measured text == emitted text, so `DisplayWidth`, wrapping, and `Line`
  equality stay automatically consistent.
- New `Segment.Raw(string text, Style style = default)` escape hatch (plus a matching
  `LineBuilder.Raw(...)`) that **bypasses** sanitization and is emitted verbatim — the single audited,
  heavily-documented "trusted" seam for consumers deliberately forwarding pre-rendered ANSI.
- Sanitization **transform is configurable via an environment variable**, defaulting to **strip
  silently**; an alternative mode replaces neutralized bytes with a visible placeholder.
- Documentation: update the `Segment` XML docs and the styled-text "verbatim" wording to describe the
  safe-by-default contract and the `Raw` opt-out.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `styled-text`: the programmatic styled-text model gains a terminal-safety guarantee — `Segment` text
  is sanitized at construction by default, with a `Raw` escape hatch and an env-var-configurable
  transform. The existing "markup-like text is literal / stored verbatim" requirement is amended:
  *printable* text is still literal, but control/escape bytes are neutralized unless the `Raw` seam is
  used.

## Impact

- **Code:** `src/Dcli/Segment.cs` (sanitizing construction + `Raw` factory + internal trusted flag),
  `src/Dcli/Line.cs` (`FromText` funnels through the safe ctor), `src/Dcli/LineBuilder.cs`
  (`Raw` method), a new internal sanitizer helper, and the env-var read site. `DisplayWidth`,
  `LineWrapper`, and `VtFrameRenderer.EmitLine` are unchanged — they keep operating on the now-safe
  stored text; only `Segment.Raw` content reaches the writer unfiltered.
- **Public API:** additive (`Segment.Raw`, `LineBuilder.Raw`) plus a behavioural change to existing
  `Segment`/`Line.FromText`/`LineBuilder` construction (control bytes neutralized).
- **Tests:** new coverage via `Dcli.Testing.HeadlessTerminal` / `FrameSnapshot` — injected
  ESC/CSI/OSC/newline never reach `InMemoryOutputSink` except through `Segment.Raw`; width/wrapping
  consistency; env-var mode switch; the sync fence cannot be defeated by consumer text.
- **Out of scope:** input-side VT parsing (`VtInputParser`) and the scrollback oversized-reprint
  ordering gap.
