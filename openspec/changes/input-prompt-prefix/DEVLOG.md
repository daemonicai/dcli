# DEVLOG — input-prompt-prefix

## Status

| Section | Title                  | Status   |
|---------|------------------------|----------|
| 1       | Public surface         | complete |
| 2       | Command + editor model | complete |
| 3       | Tests                  | complete |
| 4       | Sample / demo          | complete |
| 5       | Validation & packaging | complete |

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

## Section 5 — Validation & packaging

Gate results (all pass):
- `dotnet build` — clean, 0 warnings, 0 errors (890 tests compiled).
- `dotnet test` — 890 passed, 0 failed, 0 skipped.
- `dotnet format --verify-no-changes` — clean (no output).
- `openspec validate input-prompt-prefix --strict` — "Change 'input-prompt-prefix' is valid".

Actions taken:
- Version bumped `0.2.0-rc.5` → `0.2.0-rc.6` in `src/Dcli/Dcli.csproj` and `src/Dcli.Testing/Dcli.Testing.csproj`.
- CHANGELOG.md updated: added `IInput.SetPrompt` bullet under `[Unreleased] ### Added`.

Task 5.8 (dmon coordination) is informational — no dcli code change required.
