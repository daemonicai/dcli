## Why

dmon's Terminal redesign wants the always-on input line to read `❯ user typing here █` — a prompt glyph before the editable region. dcli's base input editor has no prompt prefix today; the gap is documented directly in `InputSurface`:

> `Prompt` — a prompt prefix shown before the editable region — is not in the §10 model and is deferred to a later §10 refinement pass.

A consumer can fake a glyph only by echoing it into scrollback *after* submit (which dmon does today), never on the live editing line. This change lifts that deferral: the persistent input surface gains a consumer-set prompt prefix rendered inline before the editable text.

It is the companion to `persistent-input-preamble`: the preamble pins chrome **above** the editor (the `── dmon ──` rule); this puts a glyph **on** the editor's line (the `❯`). Together they complete the framed-input look dmon is after. dcli is the substrate dmon proves out; this closes the second of the two documented input-chrome gaps.

## What Changes

- **Lift the prompt-prefix deferral on the base input editor.** `InputSurface` (`ITerminal.Input`) gains `SetPrompt(Line)` and `SetPrompt(string)` (the latter via `Line.FromText`); an empty or `null` prompt renders no prefix (the v1 default). The prompt persists across renders and submissions until changed.
- **Command + model.** A `SetPromptCommand : ILoopCommand` posts via `_loop.Post` and sets the editor's prompt on the render-loop thread (mirrors `SetTextCommand` → `model.FixedRegion.Editor`). The owned editor (`TextBuffer`) gains a prompt field consumed by its render.
- **Inline render.** The prompt occupies the leading columns of the editor's **first** visual row; the editable text begins immediately after it and the caret is offset by the prompt's display width. Wrapping on the first row is computed against the width reduced by the prefix; wrapped continuation rows begin at column 0 (the prefix is not repeated).
- **Presentational only.** The prefix is chrome, not buffer content: it is never returned by `Submit`/`InputChanged` and is not part of history.
- **Remove the documented deferral** note for `Prompt` in `InputSurface` (the `ReadOnly` deferral stays).
- **Version bump:** `dcli` and `Dcli.Testing` to the next preview revision after `persistent-input-preamble` (`0.2.0-rc.6`, or a shared bump if the two changes are applied together).

## Capabilities

### New Capabilities

None — this change extends an existing capability only.

### Modified Capabilities

- `fixed-region`: ADDED an **Input editor prompt prefix** requirement specifying the `ITerminal.Input.SetPrompt(...)` surface, inline first-row rendering with display-width-aware caret offset, continuation-row behaviour, empty/unset default, persistence across submissions, and the prefix's presentational (not-in-buffer) nature.

## Impact

- **Public API (`Dcli`):** additive — `IInput`/`InputSurface` gain `SetPrompt` overloads. No existing signatures change; fully backwards compatible (no prompt set ⇒ identical to v1).
- **Public API (`Dcli.Testing`):** `HeadlessTerminal`'s input surface exposes `SetPrompt`; rendered prefix is assertable through the existing painter.
- **Production code:** small. A `SetPromptCommand` (mirror `SetTextCommand`), a prompt field on the owned `TextBuffer`/editor, and prefix-aware first-row rendering + caret/width arithmetic in the editor render path.
- **Tests:** ~5–6 in `tests/Dcli.Tests/` via `HeadlessTerminal` — prefix renders before the text; caret sits after the prefix; empty prompt = no prefix (regression); prompt persists across submissions; prompt is not part of submitted text; first-row wrapping accounts for the prefix width.
- **Consumers:** with `persistent-input-preamble`, completes dmon's framed input (`❯` on the live line). dmon drops its scrollback `❯`-echo workaround for the live editor.
- **Out of scope (deferred):**
  - Hanging-indent of wrapped continuation rows under the prompt (continuation rows start at column 0 here; alignment is a later refinement).
  - `ReadOnly` input (still deferred per the remaining `InputSurface` note).
  - A prompt prefix on dialog (`InputRequest`) fields — dialogs already carry an above-widget preamble; an inline dialog-field prefix is a separate future item.
