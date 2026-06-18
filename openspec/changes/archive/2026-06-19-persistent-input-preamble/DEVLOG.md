# DEVLOG — persistent-input-preamble

## Status

All sections complete. Section 5 gates all pass (build clean, 872 tests green, format clean, openspec valid). Awaiting commit and archive.

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

## Section 5 — Validation & packaging

Gates passed by orchestrator before worker brief:
- `dotnet build` clean (0 warnings, 0 errors)
- `dotnet test` 872/872 green
- `dotnet format --verify-no-changes` clean
- `openspec validate persistent-input-preamble --strict` valid

Version bumped: `0.2.0-rc.4` → `0.2.0-rc.5` in `src/Dcli/Dcli.csproj` and `src/Dcli.Testing/Dcli.Testing.csproj`.
CHANGELOG updated with `IInputPreamble` surface entry.

### dmon coordination

dmon's Terminal UX change consumes `ITerminal.InputPreamble` and must reference dcli `0.2.0-rc.5` (not rc.4). Update the NuGet reference in dmon after this change is published.
