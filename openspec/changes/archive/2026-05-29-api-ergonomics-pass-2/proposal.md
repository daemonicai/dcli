## Why

`api-ergonomics-pass-1` (released `0.2.0-rc.1`) closed three of the five §14.4 dmon-wizard-port ergonomics gaps; `multi-line-dialog-prompts` (`0.2.0-rc.2`) widened dialog preambles. Two classes of friction remain, plus three contracts the specs already *name* but the code never satisfied: the headline `LineBuilder` ceremony for single-style label strings (~24 concrete call sites across the two samples), a `Scrollback.AppendRule` command the `inline-scrollback` spec already mandates but ships unimplemented, an incremental `Collapsible.AppendLine` left as a documented gap, `PasteEvent` text that the input editor never inserts (the spec already lists "paste" as a first-edit trigger), and a multi-select "go back" affordance deferred twice for want of an unambiguous keybinding. This change closes all five before the rest of the dmon migration leans on them, so consumer call sites don't churn a third time.

## What Changes

- **`Line` single-style shorthand factories** — `Line.Bold(string)`, `Line.Dim(string)`, `Line.Fg(string, Color)`, `Line.Bg(string, Color)`, analogous to the existing `Line.FromText`. Each builds a single **sanitized** `Segment` and is semantically equal to `Line.FromText(text, new Style(...))`. No `Italic`/`Underline`/`Reverse`/`Strikethrough` factories (no current call sites). **No `Line.Raw` shorthand** — `Segment.Raw` / `LineBuilder.Raw` remain the only verbatim (unsanitized) seams, preserving the single-escape-hatch invariant from the vt-escape-sanitization design.
- **`IScrollback.AppendRule()`** — closes a spec/impl gap: the `inline-scrollback` "Scrollback command surface" requirement already says the library SHALL expose a command to "append a rule/separator", but no method, scenario, or implementation exists (`ScrollbackSurface` carries an explicit `// AppendRule … deferred` comment). Adds a width-aware horizontal-rule line-object spanning the live-window content width, posted as a fire-and-forget loop command like `Append`.
- **`ICollapsible.AppendLine(Line)` + `AppendLine(string)`** — closes the "incremental append to the hidden-line list … is a documented gap" noted on `BeginCollapsible`. Appends to a collapsible's hidden-line set via a loop command, with explicit one-way semantics: append is honored while the block is collapsed and live; it is a **no-op** once the block has expanded or frozen past the commit horizon (mirroring `Expand()`'s past-horizon no-op).
- **`PasteEvent` routing into the owned input editor** — `PasteEvent` already exists in the `terminal-input` event model and parser, but its text is never inserted. Route it through the intercept chain to the active input surface (overlay-first), inserting at the caret with display-width- and multiline-aware semantics identical to typed insertion. Paste **counts as a first edit**: it flips the `InputDialog` secret-default state so a seeded `IsSecret` `Default` switches from default-masking to buffer-masking — making the spec's existing "insert, delete, paste, history-recall" wording true in code.
- **`MultiSelectRequest.AllowBack`** (default `false`) — settles the twice-deferred multi-select Back affordance. When `true`, pressing **`[`** at any time produces `OverlayCloseKind.Back` → `DialogOutcome.Back`. A distinct key (not Backspace) sidesteps the Backspace-at-toggle-position ambiguity that caused the prior deferrals. `Select`/`Choice` additionally accept `[` as a secondary Back key (when their existing `AllowBack=true`) for cross-dialog consistency; their Backspace-before-first-move binding from pass-1 is unchanged.

No breaking changes. Every addition is a new factory, a new overload, a new method, or an opt-in flag defaulting to existing behaviour; all current call sites compile and behave identically. Package bump `0.2.0-rc.2 → 0.2.0-rc.3`.

## Capabilities

### New Capabilities

None — this change extends existing capabilities only.

### Modified Capabilities

- `styled-text`: ADDED requirement for the `Line.Bold` / `Line.Dim` / `Line.Fg` / `Line.Bg` single-style shorthand factories (single sanitized segment; no implicit conversion; no `Raw` shorthand). No behavioural change to existing requirements.
- `inline-scrollback`: MODIFIED "Scrollback command surface" to add a satisfiable `AppendRule` scenario; MODIFIED "One-way collapsible" to define incremental hidden-line `AppendLine` with explicit post-expand / post-horizon no-op semantics.
- `fixed-region`: MODIFIED "Owned input editor" (and intercept-chain routing) so `PasteEvent` text is inserted at the caret and counts as a first edit (flips secret-default masking); MODIFIED "Awaitable modal dialogs" to give `MultiSelectRequest` an opt-in `AllowBack` bound to `[` — reversing the spec's current "MultiSelectRequest SHALL continue to omit AllowBack" sentence.

## Impact

- **Public API (`Dcli`):** net additions only — four `Line` factories; `IScrollback.AppendRule`; `ICollapsible.AppendLine(Line)` / `(string)`; `MultiSelectRequest.AllowBack`; `[`-as-Back accepted by `Select`/`Choice`/`MultiSelect` dialogs under `AllowBack`. Existing members unchanged.
- **Public API (`Dcli.Testing`):** none directly; new tests use the headless harness as substrate.
- **Production code:** `Line` factories are thin wrappers over `FromText`. `AppendRule` needs a new width-aware rule line-object + loop command. `Collapsible.AppendLine` needs a loop command appending to the hidden-line list, guarded by the collapsed/live state. `PasteEvent` routing is a new arm in the intercept chain + the editor's insert path + the secret-default first-edit flip. `MultiSelectRequest.AllowBack` is one flag + one `[` keybinding arm in the dialog key handler. All new loop commands and the paste path must respect render-loop single-thread mutation (see memory `ca2007-render-loop-thread-discipline`).
- **Samples:** `samples/Dcli.Demo.DmonWizard/Engine/WizardRenderer.cs` (~10 single-style sites) and `samples/Dcli.Demo/Program.cs` (~14 sites) migrate onto the new `Line` factories to validate ergonomics, mirroring pass-1 §5.
- **Tests:** ~20–30 new tests across `LineTests` (factories), `ScrollbackTests`/inline-scrollback harness tests (rule, incremental collapsible), input-editor tests (paste insertion + secret-default flip), and `DialogSelectionTests` (multi-select `AllowBack`/`[`). All ride the `Dcli.Testing` `HeadlessTerminal`/`FrameSnapshot` harness.
- **Cross-references:** memory `scrollback-oversized-reprint-ordering` (ensure `AppendLine` doesn't worsen the known commit-order edge); memory `restore-on-signal-rendering-state` (paste routing adds no new restore-path state).
- **Out of scope (deferred to later changes):** `Italic`/`Underline`/`Reverse`/`Strikethrough` `Line` factories; `Line.Raw`; `Input.Prompt` / `Input.ReadOnly` on the fixed-region input surface; deeper rule styling (custom glyph palettes beyond a minimal optional glyph/style); the `InputDialog` over-budget caret-reporting tightening flagged by multi-line-dialog-prompts.
- **Consumers:** unblocks the remaining `dmon-migration` work that leans on rule separators, incremental collapsibles, paste-into-input, and multi-select back-navigation.
