## Why

dmon's Terminal host is redesigning its bottom chrome to frame the input on both sides:

```
── dmon ──────────────────────────────────────   ← header rule, pinned above the input
❯ user typing here █                              ← the always-on input editor
──────────────────────────────────────────────   ← rule  ┐ status rows
[Ready] dmon core v0.2.0-preview.23  gemini…      ← status ┘ (pinned, below input)
```

The **below-input** chrome is already expressible: `StatusSurface.SetRows(...)` pins styled rows beneath the input editor. There is no symmetric surface for the **above-input** chrome — the only way to put a line directly above the always-on editor today is to append it to scrollback, where it scrolls away as the conversation grows instead of staying pinned as a frame.

`multi-line-dialog-prompts` closed this exact gap for **transient** dialogs (`InputRequest.Prompt` et al. render a multi-line preamble above a dialog widget). This change is the analogue for the **persistent** base input editor: a consumer-set preamble band pinned directly above it, mirroring the status surface that already pins rows below it.

dmon is dcli's vehicle for proving and improving the substrate; a workaround in dmon (scrollback re-prints) is a substrate-gap signal. Closing it here lets dmon — and any future consumer wanting persistent input chrome (a session header, a mode banner, a hint line) — use dcli's intended surface.

## What Changes

- **New public surface `ITerminal.InputPreamble`** (type `IInputPreamble`, impl `InputPreambleSurface`), mirroring `ITerminal.Status` / `StatusSurface` one-for-one:
  - `SetRows(params Line[])` and `SetRows(IReadOnlyList<Line>)` replace the preamble content; an empty argument clears it.
  - Fire-and-forget via `_loop.Post(new SetInputPreambleCommand(rows))`; applied on the render-loop thread.
  - Wired in `Terminal` construction exactly as `Status = new StatusSurface(loop)`.
- **New persistent band in the fixed-region stack.** The preamble renders directly above the input editor band (below the live window). `RenderModel.FixedRegion` gains a `Preamble` rows holder; `SetInputPreambleCommand.Apply` sets `model.FixedRegion.Preamble.Rows` and marks dirty; `FixedRegionComposer.Compose` emits the preamble rows immediately above the input rows.
- **Budget participation.** The preamble participates in the fixed-region height budget. Under pressure it truncates **before** the input editor loses its last usable row; the status band stays sacred (never squeezed). Null/empty preamble paints nothing and returns its rows to the budget.
- **Presentational only.** The preamble is not part of the intercept chain — it never consumes keys; all keys reach the input editor (or the active overlay) as before.
- **Version bump:** `dcli` and `Dcli.Testing` `0.2.0-rc.4` → `0.2.0-rc.5` (additive, preview channel).

## Capabilities

### New Capabilities

None — this change extends an existing capability only.

### Modified Capabilities

- `fixed-region`: MODIFIED the **Bottom-pinned component stack** requirement to include a persistent input-preamble band rendered directly above the input editor; ADDED a **Persistent input preamble** requirement specifying the `ITerminal.InputPreamble` surface, its `SetRows` semantics, persistence across renders/turns, null/empty clearing, height-budget participation (truncate-before-input, status stays sacred), and its presentational (non-intercepting) nature.

## Impact

- **Public API (`Dcli`):** additive — one new property `ITerminal.InputPreamble`, one new surface type. No existing signature changes; fully backwards compatible. Binary-compat is irrelevant on the preview channel; dmon takes a local reference and recompiles.
- **Public API (`Dcli.Testing`):** `HeadlessTerminal` exposes the same `InputPreamble` surface so consumers can assert preamble rows in tests; rendering flows through the existing painter.
- **Production code:** small. A surface type + a loop command (both mirror `StatusSurface` / `SetStatusCommand`), a `Preamble` holder on the render model, and one band insertion in `FixedRegionComposer.Compose` plus its budget arithmetic.
- **Tests:** ~6–8 in `tests/Dcli.Tests/` via `HeadlessTerminal` — preamble renders directly above the editor; persists across successive submissions; empty clears; truncates under budget pressure while the editor keeps ≥1 row and status stays fully rendered; keys fall through to the editor. Existing fixed-region tests stay green as a regression guard.
- **Consumers:** unblocks dmon's Terminal UX redesign (the pinned `── dmon ──` header rule above the input). dmon drops the scrollback-reprint workaround.
- **Out of scope (deferred):**
  - The input **prompt-prefix glyph** (`❯` shown before the editable region) — still deferred per the existing note in `InputSurface` (`§10` prompt-prefix refinement). The preamble is above the editor, not on the editor's line; the `❯` is a separate feature dmon can pursue next if it wants the glyph on the live line.
  - Welcome banner / message-of-the-day — these are ordinary scrollback content owned by the consumer (dmon), not fixed-region chrome.
  - Per-band styling/borders beyond what a consumer composes into the `Line` rows themselves.
