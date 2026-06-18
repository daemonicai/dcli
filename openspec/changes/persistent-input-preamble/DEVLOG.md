# DEVLOG — persistent-input-preamble

## Status

Sections 1 + 2 in progress (worker running).

## Pre-flight notes

- Change branch `change/persistent-input-preamble` created from `proposals/terminal-input-chrome`
  (not from `main`) because the proposal artifacts only exist on that branch.
- `openspec validate` passes on the correct base branch.

## Section 1 + 2 — Public surface & render model/composer

Sections 1 and 2 are implemented together in one worker call because `InputPreambleSurface`
(Section 1) contains the nested `SetInputPreambleCommand` whose `Apply` references
`model.FixedRegion.Preamble` — the preamble holder that is Section 2 task 2.1. They cannot
compile independently, so the worker produces both sections, then two separate commits are made.

### Key decisions carried into implementation

- D1: Mirror `StatusSurface` exactly — same constructor, same overloads, nested command class.
- D2: Preamble rows placed below an above-input overlay, above the input editor rows.
- D3: Preamble yields before input editor; status is sacred.
- D4: SetRows is set-and-hold; empty argument clears.
- D5: Preamble is presentational — never in the intercept chain.
- D6: Interface `IInputPreamble`, impl `InputPreambleSurface`, parallel to `IStatus`/`StatusSurface`.

### Internal model shape

- New `PreambleLine` class in `src/Dcli/Internal/FixedRegion/` — mirrors `StatusLine`.
- `FixedRegionComposer` gains `_preamble : PreambleLine` field and `Preamble` property.
- `RenderModel.FixedRegion` constructed as `new(new TextBuffer(), new PreambleLine(), new StatusLine())`.
- `IInputPreamble` declared in `src/Dcli/ITerminal.cs` alongside `IStatus`.
- `InputPreambleSurface` in `src/Dcli/InputPreambleSurface.cs`.
- `ITerminal.InputPreamble : IInputPreamble` property added after `Status`.
- `Terminal` ctor: `InputPreamble = new InputPreambleSurface(loop)` after `Status = ...`.
