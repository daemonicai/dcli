## 1. Widen the request records

- [x] 1.1 `src/Dcli/DialogRequests.cs` — change `SelectRequest.Title` from `Line?` to `IReadOnlyList<Line>?`; preserve the existing single-`Line` convenience constructor by wrapping the line into a one-element list; keep the existing `IReadOnlyList<string>` / `params string[]` item constructors unchanged.
- [x] 1.2 `src/Dcli/DialogRequests.cs` — add new `SelectRequest` constructors: `(IReadOnlyList<Line> items, IReadOnlyList<Line>? title, bool allowBack)`, `(IReadOnlyList<Line> items, params Line[] title)`, `(IReadOnlyList<Line> items, IReadOnlyList<string>? title, bool allowBack)`, `(IReadOnlyList<Line> items, params string[] title)`. String forms map each entry through `Line.FromText`.
- [x] 1.3 Repeat 1.1 + 1.2 for `MultiSelectRequest.Title` (note: no `AllowBack` parameter on MultiSelect, per the prior spec).
- [x] 1.4 Repeat 1.1 + 1.2 for `ChoiceRequest.Prompt` (with `AllowBack`).
- [x] 1.5 Repeat 1.1 + 1.2 for `InputRequest.Prompt` (preserve `Default` and `IsSecret` parameters; no `AllowBack`).
- [x] 1.6 XML docs on each record's new constructors describe the multi-line semantics and call out that the single-`Line` / single-`string` forms are equivalent to one-element lists.
- [x] 1.7 No implicit `Line → IReadOnlyList<Line>` conversion is defined (mirrors the api-ergonomics-pass-1 rejection of implicit `string → Line`). Document the explicit-only contract in the XML doc on the new multi-line constructors.

## 2. Renderer: iterate the preamble

- [ ] 2.1 `src/Dcli/Internal/FixedRegion/Dialog.cs` (covers `SelectRequest` + `MultiSelectRequest`) — replace the single-line preamble paint with a `foreach` over `preamble ?? []` that paints each `Line` top-to-bottom above the list.
- [ ] 2.2 `src/Dcli/Internal/FixedRegion/ChoiceDialog.cs` — same change for `ChoiceRequest.Prompt`.
- [ ] 2.3 `src/Dcli/Internal/FixedRegion/InputDialog.cs` — same change for `InputRequest.Prompt`. The preamble paints above the input field; the existing secret-default masking on the field itself is unaffected.
- [ ] 2.4 Confirm the overlay-budget arithmetic (row allocation between preamble + widget) handles variable preamble heights without new logic. The same machinery already serves live-blocks; the dialog overlay re-uses it via the existing layout pipeline.
- [ ] 2.5 Confirm null / empty / single-line / multi-line preambles all paint consistently (zero rows / zero rows / one row / N rows).

## 3. Tests

- [ ] 3.1 `tests/Dcli.Tests/DialogSelectionTests.cs` — add a multi-line `Title` test using `HeadlessTerminal`: construct a `SelectRequest` with a 3-line title; capture a `FrameSnapshot`; assert all 3 lines appear above the list items in order.
- [ ] 3.2 Equivalent multi-line test in `tests/Dcli.Tests/MultiSelectDialogTests.cs` (or wherever multi-select tests live) for `MultiSelectRequest.Title`.
- [ ] 3.3 Equivalent multi-line test in `tests/Dcli.Tests/ChoiceDialogTests.cs` for `ChoiceRequest.Prompt`.
- [ ] 3.4 Equivalent multi-line test in `tests/Dcli.Tests/InputDialogTests.cs` for `InputRequest.Prompt`.
- [ ] 3.5 Backwards-compat round-trip tests in `tests/Dcli.Tests/FacadeTests.cs` (or `DialogRequestsTests.cs` if a dedicated file fits): each single-`Line` / single-`string` constructor produces a request whose preamble is a one-element list.
- [ ] 3.6 Null/empty preamble test: a request with `null` or `[]` preamble paints zero preamble rows.
- [ ] 3.7 Truncation regression test: a request with a preamble taller than the overlay budget paints up to the budget and leaves the widget visible (assert via `FrameSnapshot` shape).

## 4. Sample updates

- [ ] 4.1 `samples/Dcli.Demo.DmonWizard/Engine/WizardRenderer.cs` — pick at least one step (e.g. the auth-config text input) and add a 2–3-line `Prompt` preamble describing what the user is providing and why. Validates the API end-to-end through a real consumer flow.
- [ ] 4.2 (Optional) `samples/Dcli.Demo/` — add a multi-line choice-dialog demonstration if a clean spot exists.

## 5. Validation & packaging

- [ ] 5.1 `dotnet build -c Release` — 0 warnings.
- [ ] 5.2 `dotnet test -c Release` — green (existing baseline + ~6–8 new).
- [ ] 5.3 `dotnet format --verify-no-changes` — clean.
- [ ] 5.4 `openspec validate multi-line-dialog-prompts --strict` — valid.
- [ ] 5.5 Bump `src/Dcli/Dcli.csproj` and `src/Dcli.Testing/Dcli.Testing.csproj` `Version` from `0.2.0-rc.1` → `0.2.0-rc.2` (additive minor revision, preview channel).
- [ ] 5.6 `dotnet pack -c Release` produces `dcli.0.2.0-rc.2.{nupkg,snupkg}` + `dcli.testing.0.2.0-rc.2.{nupkg,snupkg}` cleanly.
- [ ] 5.7 Update `openspec/changes/multi-line-dialog-prompts/DEVLOG.md` per the `devlog` skill conventions: a row in the Section status table for each section commit, deviations as they happen, resume-point bumped after each section.
- [ ] 5.8 Coordinate with dmon (or notify the user) so the dmon Phase 2 resume recipe runs: drop the `ToolConfirmPrompt` scrollback workaround, restore lines into `ChoiceRequest.Prompt`, update `ToolConfirmPromptTests`, run dmon gates, commit dmon Phase 2.
