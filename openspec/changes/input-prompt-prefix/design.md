## Context

The owned input editor (`model.FixedRegion.Editor`, a `TextBuffer`) renders via `Editor.Render(width, budget)` → `RenderResult { VisibleRows, CaretPosition }`. `InputSurface` (`ITerminal.Input`) drives it through fire-and-forget `ILoopCommand`s (`SetTextCommand`, `ClearCommand`) that mutate the editor and mark the model dirty. `InputSurface`'s own doc-comment records the prompt prefix as a deferred §10 feature.

The dialog path already renders a *preamble above* its widget (`multi-line-dialog-prompts`); `persistent-input-preamble` adds a persistent band above the base editor. This change is different in kind: an **inline** prefix on the editor's own first row, before the editable text — the `❯` glyph.

## Goals / Non-Goals

**Goals:**
- A consumer-set, persistent prompt prefix on the base editor's first visual row, with the caret correctly offset.
- An API and command that mirror the existing `InputSurface.SetText` path.
- Backwards compatible: no prompt ⇒ exactly v1 behaviour.

**Non-Goals:**
- Hanging-indent of wrapped continuation rows (start at column 0 here).
- `ReadOnly` input (separate deferred item).
- A prefix on dialog input fields (dialogs use an above-widget preamble).

## Decisions

### D1: API on InputSurface, mirroring SetText

`IInput`/`InputSurface` gain `SetPrompt(Line)` and `SetPrompt(string)` (string via `Line.FromText`). Each posts `_loop.Post(new SetPromptCommand(line))`; `SetPromptCommand.Apply` calls `model.FixedRegion.Editor.SetPrompt(line)` and `model.MarkDirty()` — the exact shape of `SetTextCommand`. An empty/`null` prompt clears it (renders no prefix). `SetPrompt` does **not** emit `InputChanged` (consistent with `SetText`/`Clear`).

### D2: Inline first-row render with display-width-aware caret

The prompt occupies the leading columns of the editor's **first** visual row. The editable text begins at column `promptWidth` on that row, and the caret's column is offset by `promptWidth` while it is on row 0. The first row's text capacity is `width - promptWidth`; wrapping is computed against that reduced width for row 0.

This parallels how `InputDialog` already offsets its caret to account for prompt rows (`InputDialog.Render`), but along the column axis on a single row rather than the row axis.

### D3: Continuation rows begin at column 0

Wrapped continuation rows are **not** re-prefixed; they start at column 0 and use the full width. This keeps the width arithmetic simple (only row 0 is reduced) and matches common shell behaviour. Hanging-indent (aligning continuation text under the first row's text) is a deferred refinement.

### D4: The prefix is chrome, never buffer content

The prompt is not inserted into the editor buffer: `Submit` and `InputChanged` return only the user's text, history stores only the user's text, and `SetText`/`Clear` operate on buffer content independent of the prompt. Because the base editor is not a secret field, no masking interaction arises (secret rendering is an `InputRequest`/dialog concern).

### D5: Persistence

`SetPrompt` is set-and-hold: the prefix renders every frame until changed or cleared. The consumer sets it once (e.g. `SetPrompt("❯ ")` at startup); it survives submissions, history recall, and `Clear()` (clearing the buffer does not clear the prompt).

### D6: Remove the deferral note

The `InputSurface` remarks list drops the `Prompt` bullet (the `ReadOnly` bullet remains). No other doc churn.

## Risks / Trade-offs

- **First-row-only width reduction** (D2/D3) means a long first line wraps slightly earlier than continuation lines; acceptable and conventional. Hanging-indent would remove the visual seam but complicates the arithmetic — deferred.
- **Coordination with `persistent-input-preamble`**: both touch the fixed region but in different bands (preamble = a stack band above the editor; prompt = inline on the editor's row). They are independent and can land in either order; the version bump is coordinated (see proposal).

## Open Questions

- **Version target / bundling.** `0.2.0-rc.6` assumed if applied after `persistent-input-preamble`; if the two are applied together, a single bump suffices. Confirm at packaging time.
