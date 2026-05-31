# Tasks — back-nav-input

## 1. InputRequest.AllowBack + dialog wiring

- [x] 1.1 Add `bool AllowBack = false` as the trailing parameter of the `InputRequest` primary
  constructor and of the convenience overloads that already accept `Default`/`IsSecret` (the
  `Line?`, `string?`, and `IReadOnlyList<string>?` forms). Leave the two `params`-tail overloads
  (`params Line[]`, `params string[]`) unchanged — `params` must be last, so they cannot carry
  `AllowBack`; document this on each, mirroring `MultiSelectRequest`. Add an `<param>`/`<summary>`
  XML doc for `AllowBack` describing the Backspace-on-empty trigger.
- [x] 1.2 Add an `allowBack` constructor parameter to `InputDialog` and store it in a
  `private readonly bool _allowBack` field; thread `req.AllowBack` through `Terminal.InputAsync`
  via `new InputDialog(req.Prompt, req.Default, req.IsSecret, req.AllowBack)`
  (mirror `SelectAsync`'s `allowBack: req.AllowBack` wiring).
- [x] 1.3 In `InputDialog.HandleKey`, inside the `NamedKey.Backspace` arm, add a Back branch
  guarded by `_allowBack && _buffer.Text.Length == 0`: set `CloseRequest = OverlayCloseKind.Back`
  and return `true` (consumed). Otherwise fall through to the existing `_buffer.Backspace()` delete
  path. Confirm Enter/Escape precedence and the Ctrl/Alt gate above are unaffected (Ctrl+Backspace
  must not trigger Back). No change to `OpenModalAsync` — it already maps `OverlayCloseKind.Back`.
- [x] 1.4 Update the `DialogOutcome.Back` enum-member summary and `<remarks>` in `DialogOutcome.cs`
  to enumerate all four producers (`SelectAsync`, `ChoiceAsync`, `MultiSelectAsync`, `InputAsync`)
  and each trigger: Backspace-before-movement (Select/Choice), `[` (MultiSelect), Backspace-on-empty
  (Input). Keep the `Value` is `default` note.

## 2. Tests

- [ ] 2.1 `InputRequest.AllowBack` defaults to `false` and is settable on the primary ctor and each
  non-`params` convenience overload (compile-level + value assertions).
- [ ] 2.2 `AllowBack=true` + empty field + Backspace → `DialogResult` outcome is `Back`, `Value` is
  `default` (empty string).
- [ ] 2.3 `AllowBack=true`, type text then Backspace back to empty, then one more Backspace → `Back`
  (trigger is current emptiness, not pristine state).
- [ ] 2.4 `AllowBack=true` + non-empty text + Backspace → character deleted, dialog stays open, no
  `Back`.
- [ ] 2.5 `AllowBack=false` (default) + empty field + Backspace → no-op, dialog stays open
  (v1 behaviour preserved).
- [ ] 2.6 `AllowBack=true` + `[` keypress → `[` inserted as literal text, dialog stays open
  (no Back binding for input).

## 3. Validation & packaging

- [ ] 3.1 Bump `<Version>` in `src/Dcli/Dcli.csproj` from `0.2.0-rc.3` to `0.2.0-rc.4`.
- [ ] 3.2 Gates: `dotnet build` clean (warnings-as-errors), `dotnet test` green,
  `openspec validate back-nav-input --strict`, `dotnet format --verify-no-changes` clean.
