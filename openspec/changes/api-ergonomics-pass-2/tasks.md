## 1. Line single-style shorthand factories

- [x] 1.1 Add `public static Line Bold(string text)` to `src/Dcli/Line.cs`, equivalent to `FromText(text, new Style(Format: Format.Bold))`.
- [x] 1.2 Add `public static Line Dim(string text)` equivalent to `FromText(text, new Style(Format: Format.Dim))`.
- [x] 1.3 Add `public static Line Fg(string text, Color foreground)` equivalent to `FromText(text, new Style(Foreground: foreground))`.
- [x] 1.4 Add `public static Line Bg(string text, Color background)` equivalent to `FromText(text, new Style(Background: background))`.
- [x] 1.5 XML-doc each factory: single sanitized `Segment`, equivalent `FromText` form, and that there is deliberately no `Italic`/`Underline`/`Reverse`/`Strikethrough`/`Raw` shorthand (`Segment.Raw`/`LineBuilder.Raw` remain the only verbatim seams; no implicit `string`→`Line` conversion).
- [x] 1.6 Add `LineTests` covering: Bold→single bold segment; Dim→single dim segment; Fg→single fg-colored segment + `Format.None`; Bg→single bg-colored segment.
- [x] 1.7 Add `LineTests` asserting `Line.Bold("x")` equals `Line.FromText("x", new Style(Format: Format.Bold))` (equivalence), and that a control/escape byte is neutralized identically to `FromText` under the default sanitization mode.
- [x] 1.8 Build + test + format gates clean for this section.

## 2. Scrollback.AppendRule

- [x] 2.1 Add a width-aware rule line-object in the inline-scrollback widget layer whose rendered width resolves to the live-window content width at paint time (correct across resize); do not bake a fixed width.
- [x] 2.2 Add `void AppendRule()` to `IScrollback` (`src/Dcli/ITerminal.cs`) with XML doc.
- [x] 2.3 Implement `ScrollbackSurface.AppendRule()` posting a new fire-and-forget loop command (sibling of `AppendToScrollbackCommand`); the command appends the rule line-object on the render-loop thread only.
- [x] 2.4 Remove the `// AppendRule … needs a width-aware rule line-object … deferred to a future change` documented-gap comment from `ScrollbackSurface`.
- [x] 2.5 Add a test asserting `AppendRule` enqueues and renders a horizontal separator spanning the live-window content width (via `HeadlessTerminal`/`FrameSnapshot`).
- [x] 2.6 Add a resize test asserting the rule re-expands to the new content width after the terminal resizes.
- [x] 2.7 Build + test + format gates clean for this section.

## 3. Incremental Collapsible.AppendLine

- [x] 3.1 Add `void AppendLine(Line line)` and `void AppendLine(string text)` (string form via `Line.FromText`) to `ICollapsible` (`src/Dcli/ScrollbackSurface.cs`) with XML doc stating the one-way semantics.
- [x] 3.2 Add a new loop command that appends to the collapsible's hidden-line list on the render-loop thread only; it MUST no-op if the collapsible has already expanded or frozen past the commit horizon (mirror `Expand`'s past-horizon no-op).
- [x] 3.3 Wire `CollapsibleHandle.AppendLine` to post the command; update the `BeginCollapsible` "incremental append … is a documented gap" remark to reflect that the gap is now closed.
- [x] 3.4 Add a test: append before expansion, then expand → revealed content includes the appended line in append order after the original hidden lines.
- [x] 3.5 Add a test: append after expansion is a no-op (revealed content unchanged).
- [x] 3.6 Add a test: append after horizon-freeze is a no-op.
- [x] 3.7 Add a regression test guarding `scrollback-oversized-reprint-ordering`: append, then trigger an oversized expansion, and assert commit ordering matches the existing baseline (AppendLine does not worsen the edge).
- [x] 3.8 Build + test + format gates clean for this section.

## 4. PasteEvent editor routing

- [ ] 4.1 Route `PasteEvent` through the existing intercept chain to the active input surface (overlay-first: an active `Dialog`/`InputDialog` consumes it; otherwise the base input editor) on the render-loop thread.
- [ ] 4.2 Insert the paste text at the caret as a single edit using the editor's existing insert path (display-width-aware, multiline-aware; caret advances past the inserted text; wrapping recomputed).
- [ ] 4.3 Flip the existing sticky `_userEdited` flag (from pass-1 §4) on paste so a seeded secret `Default` switches from default-masking to buffer-masking.
- [ ] 4.4 Add a test: paste inserts text at the caret and the caret advances to the end of the inserted text.
- [ ] 4.5 Add a test: pasting text wider than the available width wraps display-width-aware and the caret lands at the correct visual row/column.
- [ ] 4.6 Add a test driving a `PasteEvent` as the first interaction on an `IsSecret=true` `InputRequest` with a non-empty `Default`: next paint shows buffer-masking (seeded default no longer the rendered content) and `Submit` returns the real edited buffer.
- [ ] 4.7 Add a test confirming paste is consumed by an active modal dialog/InputDialog and does not leak to the base editor when an overlay is active.
- [ ] 4.8 Build + test + format gates clean for this section.

## 5. Multi-select Back via '['

- [ ] 5.1 Add `AllowBack` (default `false`) to `MultiSelectRequest` (`src/Dcli/DialogRequests.cs`) with XML doc explaining the `[` binding and why Backspace is not used for multi-select.
- [ ] 5.2 In the dialog key handler, when a multi-select overlay has `AllowBack=true`, map `[` (at any time, no movement-suppression) to `OverlayCloseKind.Back` → `DialogOutcome.Back`.
- [ ] 5.3 For `Select`/`Choice` with `AllowBack=true`, additionally accept `[` as a secondary Back key alongside the existing pass-1 Backspace-before-first-move binding (apply the same movement-suppression as Backspace for these two).
- [ ] 5.4 Add tests: MultiSelect `AllowBack=true` + `[` → `Back`; MultiSelect `[` still produces `Back` after toggling items with Space; MultiSelect `AllowBack=false` (default) → `[` has no effect (v1 behaviour preserved).
- [ ] 5.5 Add tests: Select/Choice `AllowBack=true` + `[` before moving the selection → `Back`; the existing Backspace bindings remain unchanged.
- [ ] 5.6 Build + test + format gates clean for this section.

## 6. Sample migration onto the new Line factories

- [ ] 6.1 Replace the single-style `new LineBuilder().Bold(s)/.Dim(s)/.Fg(s, color).Build()` sites in `samples/Dcli.Demo.DmonWizard/Engine/WizardRenderer.cs` (~10 sites) with `Line.Bold(s)` / `Line.Dim(s)` / `Line.Fg(s, color)`.
- [ ] 6.2 Replace the single-style `new LineBuilder()....Build()` label sites in `samples/Dcli.Demo/Program.cs` (~14 sites) with the corresponding `Line.Bold/Dim/Fg` factories.
- [ ] 6.3 Optionally demonstrate `AppendRule` and/or incremental `Collapsible.AppendLine` in `Program.cs` where it tightens the demo (keep minimal; samples-only).
- [ ] 6.4 Confirm samples build; multi-segment `LineBuilder` sites (not single-style) are left untouched.
- [ ] 6.5 Build + test + format gates clean for this section.

## 7. Validation & packaging

- [ ] 7.1 `openspec validate api-ergonomics-pass-2 --strict` passes.
- [ ] 7.2 `dotnet build` clean (0 warnings, analyzers/warnings-as-errors), `dotnet test` green (all sections' new tests + full existing suite), `dotnet format --verify-no-changes` clean.
- [ ] 7.3 Bump `Version` `0.2.0-rc.2 → 0.2.0-rc.3` in `src/Dcli/Dcli.csproj` and `src/Dcli.Testing/Dcli.Testing.csproj`.
- [ ] 7.4 `dotnet pack -c Release` produces `dcli.0.2.0-rc.3.{nupkg,snupkg}` and `dcli.testing.0.2.0-rc.3.{nupkg,snupkg}`.
- [ ] 7.5 Final gates re-run clean against the version bump; record per-section commit hashes in the DEVLOG.
