## Why

Porting `dmon-core`'s `WizardEngine` slice in §14.4 of the core-rendering-architecture change validated dcli's surface end-to-end but surfaced five concrete ergonomics gaps. Three of them — verbose `Line` lifting, the structurally-dead `DialogOutcome.Back`, and a leaky `IsSecret` default render — are blocking a clean migration of the rest of `Dmon.Terminal` onto dcli. This change closes those three before the dmon migration starts, so the migration is a single clean pass and the dcli public API doesn't churn twice on the same call sites.

## What Changes

- **`Line.FromText(string text, Style? style = null)` factory** added on `Line`. Replaces the `new LineBuilder().Text(s).Build()` ceremony for label-only strings.
- **String-accepting overloads** added across the consumer surface:
  - `IScrollback.Append(string text)` alongside `Append(Line)`.
  - `InputRequest` constructor / record accepts `string? Prompt` alongside `Line? Prompt`.
  - `SelectRequest` / `MultiSelectRequest` / `ChoiceRequest` accept `IReadOnlyList<string>` or `params string[]` items alongside their existing `Line` item lists.
- **Explicit conversion only** — an implicit `string → Line` conversion is **rejected**. `FromText` is the only seam, so callers don't accidentally pass strings where styled `Line`s were intended.
- **`OverlayCloseKind.Back` becomes reachable** via an opt-in `AllowBack` flag on `SelectRequest` and `ChoiceRequest` (default `false`, backward-compatible). When `true`, the dialog wires **Backspace at the empty initial position** (with `[` as fallback if Backspace becomes ambiguous in a future overlay) to `OverlayCloseKind.Back` → `DialogOutcome.Back`. `MultiSelectRequest` does **not** get this flag in pass-1 (Backspace at a multi-toggle position is semantically ambiguous; defer).
- **`InputDialog` masks its seeded `Default` when `IsSecret=true`**. Today bullets only kick in once the user edits, so the seeded default leaks clear-text on first paint. After this change the default is rendered as `'•' * displayWidth(default)` until the first edit; `Submit` still returns the real string.

No breaking changes. Every public addition is an overload or an opt-in flag; existing call sites continue to compile and behave identically.

## Capabilities

### New Capabilities

None — this change extends existing capabilities only.

### Modified Capabilities

- `styled-text`: ADDED requirement for the `Line.FromText` factory and the `string`-accepting consumer overloads (no behavioural change to existing requirements).
- `fixed-region`: MODIFIED dialog close semantics to add the `Back` outcome under opt-in `AllowBack`; MODIFIED `InputDialog` default-rendering when `IsSecret=true`.

## Impact

- **Public API (`Dcli`):** new factory + overloads on `Line`, `IScrollback`, `InputRequest`, `SelectRequest`, `MultiSelectRequest`, `ChoiceRequest`. Existing members unchanged. Net additions only.
- **Public API (`Dcli.Testing`):** none directly; the headless harness is unaffected. New tests use it as a substrate.
- **Production code:** small. `Line.FromText` is one new static method on a record. The dialog `Back` keybinding is one switch arm in `Dialog.HandleKey`/`ChoiceDialog.HandleKey` gated on the new request flag. The secret-default masking is one render-path change in `InputDialog.Render`.
- **Tests:** ~10–15 new tests across `LineTests` (factory), `FacadeTests` (string overloads round-trip), `DialogSelectionTests`/`ChoiceDialogTests` (`AllowBack`), `InputDialogTests` (secret-default mask). All ride on the `Dcli.Testing` headless harness.
- **Repo docs:** the project `CLAUDE.md` "Where to look for historical context on a shipped change" subsection (added in PR #2) gets refined during this change to match the canonical wording recommended by the personal `devlog` skill at `~/.claude/skills/devlog/SKILL.md`. The skill's wording covers both the in-flight DEVLOG inside the change directory AND the archived DEVLOG, where PR #2's wording only covered the archived case. Small documentation hygiene task; folded into this change for convenience.
- **Out of scope (deferred to later changes):**
  - `DialogOutcome.Back` for multi-select dialogs.
  - `Input.Prompt` / `Input.ReadOnly` on the fixed-region input surface (already deferred from §12).
  - `Scrollback.AppendRule`, incremental `Collapsible.AppendLine`, `PasteEvent` editor-routing.
  - VT-escape sanitisation of `Segment.Text` (tracked in memory `vt-escape-sanitization-gap`).
- **Consumers:** unblocks the upcoming `dmon-migration` change. Ergonomics gaps 2 and 4 from the §14.4 findings (MultiSelect-is-validated, no-widening-needed) require no work — they're already-shipped wins; this change is the three blockers only.
