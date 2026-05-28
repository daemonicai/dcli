## 1. `Line.FromText` factory

- [x] 1.1 Add `public static Line FromText(string text, Style? style = null)` to `src/Dcli/Line.cs`; returns a `Line` with a single `Segment(text, style ?? default)`
- [x] 1.2 XML docs on `FromText` describing it as the canonical short form for label-only `Line`s
- [x] 1.3 Tests in `tests/Dcli.Tests/StyledTextTests.cs`: default style; explicit style; empty string; multi-rune string

## 2. String-accepting consumer overloads

- [x] 2.1 `src/Dcli/IScrollback.cs` — add `void Append(string text)` on the interface
- [x] 2.2 `src/Dcli/ScrollbackSurface.cs` — implement `Append(string)` as `Append(Line.FromText(text))`
- [x] 2.3 `src/Dcli/InputRequest.cs` — add overload accepting `string? Prompt` alongside the existing `Line? Prompt` (preserve `Default`/`IsSecret`)
- [x] 2.4 `src/Dcli/SelectRequest.cs` — add overloads accepting `IReadOnlyList<string>` and `params string[]` items
- [x] 2.5 `src/Dcli/MultiSelectRequest.cs` — add overloads accepting `IReadOnlyList<string>` and `params string[]` items
- [x] 2.6 `src/Dcli/ChoiceRequest.cs` — add overloads accepting `IReadOnlyList<string>` and `params string[]` options
- [x] 2.7 `tests/Dcli.Tests/FacadeTests.cs` — round-trip tests for each string overload (string form produces the same model state as the explicit-`Line` form)
- [x] 2.8 `tests/Dcli.Tests/FakeTerminalTests.cs` — extend the tier-A fake with the new overloads (forward to the `Line` variant); assert the fake records them identically
- [x] 2.9 Compile-fail/lint check: confirm no implicit `string → Line` conversion is defined (no operator on `Line`); document in the XML doc on `Line.FromText` that this is intentional

## 3. `AllowBack` flag on `SelectRequest` / `ChoiceRequest`

- [ ] 3.1 `src/Dcli/SelectRequest.cs` — add `bool AllowBack = false` (default false, additive)
- [ ] 3.2 `src/Dcli/ChoiceRequest.cs` — add `bool AllowBack = false`
- [ ] 3.3 `src/Dcli/Internal/FixedRegion/Dialog.cs` — handle Backspace: if `AllowBack && !_hasMoved`, set `CloseRequest = OverlayCloseKind.Back`; otherwise no-op (preserve v1)
- [ ] 3.4 `src/Dcli/Internal/FixedRegion/Dialog.cs` — set `_hasMoved = true` on `↑`/`↓` (or whatever the current movement keys are); confirm Backspace before any movement still produces Back
- [ ] 3.5 Map `OverlayCloseKind.Back` → `DialogOutcome.Back` in the loop's dismiss hook (the wiring already routes Submit/Cancel; add the Back arm)
- [ ] 3.6 Tests in `tests/Dcli.Tests/DialogSelectionTests.cs`: `AllowBack=true` + Backspace-at-empty produces `DialogOutcome.Back`; `AllowBack=true` + `↓` then Backspace is a no-op; `AllowBack=false` (default) + Backspace is a no-op
- [ ] 3.7 Equivalent tests for `ChoiceRequest` in a new or existing `ChoiceDialogTests.cs`
- [ ] 3.8 Confirm `MultiSelectRequest` did NOT receive an `AllowBack` member (this is deliberate; one test asserting it compiles without)

## 4. Secret-default masking in `InputDialog`

- [ ] 4.1 `src/Dcli/Internal/FixedRegion/InputDialog.cs` — when `IsSecret && !_userEdited && Default is non-empty`, render the buffer as `'•'` repeated by `DisplayWidth.Measure(Default)` instead of the raw default
- [ ] 4.2 Ensure the existing edit-detection (used for `InputChanged` emission) is the source of truth for `_userEdited`; do not introduce a second flag
- [ ] 4.3 `Submit` returns the real string (assert this — should be unchanged)
- [ ] 4.4 Tests in `tests/Dcli.Tests/InputDialogTests.cs`: secret + default + no edits → masked render; secret + default + Submit → real string; secret + default + one edit then revert → still masked? (decide via the existing `_userEdited` semantics; document in the test)
- [ ] 4.5 Non-secret + default test: paint shows clear-text default (regression guard)

## 5. Demo updates

- [ ] 5.1 `samples/Dcli.Demo.DmonWizard/Engine/WizardRenderer.cs` — replace the ~12 `new LineBuilder().Text(s).Build()` sites with `Line.FromText(s)` to validate the ergonomics end-to-end
- [ ] 5.2 Where possible in `samples/Dcli.Demo/`, switch label-only `Line`s to `Line.FromText` or the string overloads
- [ ] 5.3 Add a `AllowBack=true` use case to a wizard step in `Dcli.Demo.DmonWizard` so the new affordance has a live demonstration

## 6. Validation & packaging

- [ ] 6.1 `dotnet build -c Release` — 0 warnings
- [ ] 6.2 `dotnet test -c Release` — green (688 baseline + ~12 new)
- [ ] 6.3 `dotnet format --verify-no-changes` — clean
- [ ] 6.4 `openspec validate api-ergonomics-pass-1 --strict` — valid
- [ ] 6.5 Bump `Dcli.csproj` and `Dcli.Testing.csproj` `Version` from `0.1.0-rc.1` → `0.2.0-rc.1` (additive minor)
- [ ] 6.6 `dotnet pack -c Release` produces `dcli.0.2.0-rc.1.{nupkg,snupkg}` + `dcli.testing.0.2.0-rc.1.{nupkg,snupkg}` cleanly
- [ ] 6.7 Update the repo `CLAUDE.md`'s "Where to look for historical context on a shipped change" subsection to match the canonical wording recommended by the personal `devlog` skill at `~/.claude/skills/devlog/SKILL.md` (covers BOTH the in-flight DEVLOG inside the change directory AND the archived DEVLOG — the wording shipped in PR #2 only covered the archived case)
- [ ] 6.8 Update `openspec/changes/api-ergonomics-pass-1/DEVLOG.md` per the `devlog` skill conventions: a row in the Section status table for each section commit, deviations as they happen, resume-point bumped after each section
