# DEVLOG — input-prompt-prefix

## Status

| Section | Title                  | Status      |
|---------|------------------------|-------------|
| 1       | Public surface         | in progress |
| 2       | Command + editor model | in progress |
| 3       | Tests                  | pending     |
| 4       | Sample / demo          | pending     |
| 5       | Validation & packaging | pending     |

## Context

- Base branch: `main` at commit after `persistent-input-preamble` merge (version `0.2.0-rc.5`).
- Target version: `0.2.0-rc.6`.
- Change branch: `change/input-prompt-prefix`.

## Section 1+2 — Public surface & Command+editor model

Briefed worker to implement sections 1 and 2 together (they are inseparable: the public surface
posts `SetPromptCommand` which lives inside `InputSurface`, and the command mutates `TextBuffer`
which needs the prompt field and render logic).

### Key design notes

- `SetPromptCommand` is a private inner class of `InputSurface` (mirrors `SetTextCommand`/`ClearCommand`).
- `TextBuffer.SetPrompt(Line?)` stores the prompt; null/empty = no prefix (default).
- Render changes are in `BuildVisualRows`: row 0 gets a reduced width (`width - promptWidth`);
  the prompt is prepended to row 0's `Line` via segment prepend; continuation rows use full width.
- Caret column offset: when `VisualRow == 0`, `VisualCol += promptWidth` in `ComputeVisualPositionFromRows`.
- The `Line` type is used to carry styled segments; `DisplayWidth` measures the prompt width.
- `MoveHome`/`MoveEnd` on row 0 must not include the prompt columns in the buffer's char-index
  mapping — the prompt is not in the buffer. The char-index mapping is unchanged; only the
  reported visual column is offset.
