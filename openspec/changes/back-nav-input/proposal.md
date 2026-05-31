# Add back-navigation to the free-text input dialog

## Why

`InputRequest` is the only awaitable-dialog request type still missing `AllowBack` —
`SelectRequest`, `ChoiceRequest`, and `MultiSelectRequest` all expose it (multi-select shipped
in `api-ergonomics-pass-2`, rc.3). This blocks wizard flows that interleave text entry with
selection: a consumer can let the user step back through select/choice steps, but the flow hits a
dead end on any `InputAsync` step because the input dialog has no way to emit `DialogOutcome.Back`.

## What Changes

- Add an opt-in `bool AllowBack = false` parameter to `InputRequest` — on the primary constructor
  and on the convenience overloads that already carry `Default`/`IsSecret` (the `params`-tail
  overloads keep their existing signature, mirroring how `MultiSelectRequest` handles `params`).
  Default `false` keeps every existing caller source-compatible.
- `InputDialog` SHALL emit `DialogOutcome.Back` when `AllowBack=true` **and** Backspace is pressed
  while the input field is **currently empty** (text length zero), regardless of edit history.
  Backspace with any text present deletes as normal. The result `Value` is `default` on Back,
  consistent with the other dialogs.
- Thread `AllowBack` through `Terminal.InputAsync` into the dialog (mirroring `SelectAsync`'s
  `allowBack: req.AllowBack` wiring). `OpenModalAsync` already maps `OverlayCloseKind.Back` to
  `DialogOutcome.Back` generically, so no plumbing changes are needed below the dialog.
- Fix the now-stale `DialogOutcome.Back` XML doc remarks, which still say Back is "produced by
  `SelectAsync` and `ChoiceAsync`" — update to include `MultiSelectAsync` and `InputAsync` and to
  note each type's trigger.
- Bump the package version rc.3 → rc.4 (public-API addition).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- **fixed-region** — the "Awaitable modal dialogs" requirement (which specifies the opt-in
  `AllowBack` family rule for Select/Choice/MultiSelect) is extended to cover `InputRequest` and
  the input dialog's Backspace-on-empty Back trigger.

## Impact

- **Public API (additive, non-breaking):** `InputRequest.AllowBack`; refreshed `DialogOutcome.Back`
  XML docs.
- **Affected code:** `src/Dcli/DialogRequests.cs` (`InputRequest`), `src/Dcli/DialogOutcome.cs`
  (docs), `src/Dcli/Internal/FixedRegion/InputDialog.cs` (Backspace-on-empty Back branch),
  `src/Dcli/Terminal.cs` (`InputAsync` wiring), `src/Dcli/Dcli.csproj` (version).
- **Consumers:** unblocks the dmon wizard adoption (bump dcli, wire `MultiSelectAsync`, flip
  `AllowBack = true` on input steps).
