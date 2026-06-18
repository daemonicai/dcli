## 1. Public surface

- [x] 1.1 Add `IInputPreamble` to `src/Dcli/` — interface with `SetRows(params Line[])` and `SetRows(IReadOnlyList<Line>)`, doc-comments mirroring `IStatus`
- [x] 1.2 Add `InputPreambleSurface` (impl) mirroring `StatusSurface`: each `SetRows` overload posts `_loop.Post(new SetInputPreambleCommand(rows))`; empty argument clears
- [x] 1.3 Add `ITerminal.InputPreamble` property (and on `HeadlessTerminal`); wire `InputPreamble = new InputPreambleSurface(loop)` in `Terminal` construction exactly as `Status` is wired

## 2. Render model + composer band

- [x] 2.1 Add a `Preamble` rows holder to `RenderModel.FixedRegion` (parallel to `Status`)
- [x] 2.2 Add `SetInputPreambleCommand : ILoopCommand` whose `Apply(model)` sets `model.FixedRegion.Preamble.Rows = rows` and calls `model.MarkDirty()` (mirror `SetStatusCommand`)
- [x] 2.3 In `FixedRegionComposer.Compose`, emit the preamble rows immediately above the input editor band (below an above-input overlay, above the input rows); reuse the existing `fixedRows.AddRange(...)` assembly
- [x] 2.4 Fold the preamble into the height-budget arithmetic: preamble truncates before the input editor loses its last usable row; status remains sacred. Null/empty preamble contributes zero rows

## 3. Tests (tests/Dcli.Tests, via HeadlessTerminal)

- [ ] 3.1 Preamble rows render directly above the input editor on the next frame
- [ ] 3.2 Preamble persists across multiple input submissions without being re-set
- [ ] 3.3 `SetRows` with an empty argument clears the preamble and returns its rows to the budget
- [ ] 3.4 Under a constrained `MaxFixedHeight`, the preamble truncates while the input editor keeps ≥1 row and the status rows stay fully rendered
- [ ] 3.5 Keys routed with a preamble set (no overlay) all reach the input editor; the hardware cursor parks at the input caret
- [ ] 3.6 Existing fixed-region/status tests stay green (regression guard)

## 4. Sample / demo

- [ ] 4.1 Update a sample (or the demo) to set a persistent preamble (e.g. a labelled rule above the input) so the surface is exercised end-to-end

## 5. Validation & packaging

- [ ] 5.1 `dotnet build` clean (analyzers warnings-as-errors; nullable enabled)
- [ ] 5.2 `dotnet test` all green
- [ ] 5.3 `dotnet format --verify-no-changes` clean
- [ ] 5.4 `openspec validate persistent-input-preamble --strict` passes
- [ ] 5.5 Version bump: `src/Dcli/Dcli.csproj` and `src/Dcli.Testing/Dcli.Testing.csproj` `0.2.0-rc.4` → `0.2.0-rc.5`
- [ ] 5.6 Update `CHANGELOG.md` with the new surface
- [ ] 5.7 Keep a `DEVLOG.md` in the change directory while applying
- [ ] 5.8 dmon coordination: note that dmon's Terminal UX change consumes `ITerminal.InputPreamble` and must reference dcli `0.2.0-rc.5`
