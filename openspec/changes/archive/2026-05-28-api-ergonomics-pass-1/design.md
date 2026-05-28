## Context

`dcli` v1 shipped under change `core-rendering-architecture` (archived as `2026-05-28-core-rendering-architecture`). The §14.4 dmon-wizard port at `samples/Dcli.Demo.DmonWizard/` proved the public surface works but exposed concrete papercuts. Five gaps were recorded in memory `section14-api-ergonomics-findings`:

1. `DialogOutcome.Back` is structurally dead (no v1 keybinding).
2. `MultiSelectAsync` is a validated upgrade — **not a gap**, recorded as positive.
3. `IsSecret` default rendering leaks clear-text on first paint.
4. No `internal → public` widening was required — **not a gap**, recorded as positive.
5. `new LineBuilder().Text(s).Build()` appears ~12× in the renderer — heavy string ceremony.

Items #1, #3, #5 are blockers for the upcoming `dmon-migration` change. Items #2 and #4 are positive validations that need no action.

The change targets the smallest possible diff that closes the three blockers cleanly, without touching unrelated deferred work (`Input.Prompt`/`ReadOnly`, `Scrollback.AppendRule`, incremental `Collapsible.AppendLine`, `PasteEvent` routing, VT-escape sanitisation).

## Goals / Non-Goals

**Goals:**
- Cut the call-site ceremony for label-only `Line`s by ~80% — `Line.FromText(s)` replaces `new LineBuilder().Text(s).Build()` everywhere.
- Make `DialogOutcome.Back` a real, reachable outcome under explicit consumer opt-in so wizard-style "go back one step" UIs work without synthetic list items.
- Stop leaking the seeded default's clear-text content when `IsSecret=true`.
- Preserve the v1 public surface: every change is an overload or opt-in flag. Existing `Dcli`-consumer code keeps compiling and behaving identically.
- Tests use only the `Dcli.Testing` headless harness — no new test-fakes invented in pass-1.

**Non-Goals:**
- Multi-select `Back` outcome (deferred; the Backspace-at-empty-toggle-position semantics are ambiguous and a pass-2 design problem).
- An implicit `string → Line` conversion (rejected — see Decision 1).
- `Input.Prompt` / `Input.ReadOnly` on the fixed-region input surface (still deferred from §12; out of scope here).
- Any other §14.4-adjacent feature (`AppendRule`, `Collapsible.AppendLine`, `PasteEvent` editor routing).
- Touching the `Dcli.Testing` public surface.
- Touching internal mechanics (loop, scrollback model, parser). This change is **surface-only** — it adds factories, overloads, one keybinding arm, and one rendering tweak.

## Decisions

### 1. Explicit `Line.FromText` factory; reject implicit `string → Line` conversion

`Line.FromText(string text, Style? style = null) → Line` is a static factory on `Line`. An implicit operator `string → Line` was considered and rejected.

- **Why explicit:** `Line` is the rendering surface; styling is the whole point of having `Line` as a separate type from `string`. An implicit conversion would let consumers accidentally pass a `string` where a styled `Line` was intended, losing styling silently. The explicit factory is the seam — the type system stops accidental flattening.
- **Why a static method, not a constructor:** `Line` is a `record class` with `IReadOnlyList<Segment> Segments` — adding a `Line(string, Style?)` constructor would compete with the existing `Line(IReadOnlyList<Segment>)` and create ambiguity at construction sites. A named factory reads better at call sites (`Line.FromText("hello")`) and leaves the canonical constructor intact for the styled case.
- **Alternative considered — extension method (`"hello".AsLine()`):** rejected. Extension-method-on-string discoverability is poor for a core library type; the factory lives on `Line` itself where IntelliSense surfaces it under the type being constructed.

### 2. String-accepting overloads, not type parameters

The `*Request` records (`InputRequest`, `SelectRequest`, `MultiSelectRequest`, `ChoiceRequest`) each grow a sibling constructor / record signature accepting `string` / `IReadOnlyList<string>` (or `params string[]`) variants. The originals stay.

- **Why overloads, not generics:** introducing `SelectRequest<T>` for `T : Line` would explode the type graph and break consumer call sites that already use the non-generic form.
- **Why `params string[]` AND `IReadOnlyList<string>`:** `params` reads beautifully for inline literal lists (`new SelectRequest("Pick one", "Yes", "No", "Cancel")`) while `IReadOnlyList<string>` covers the "I computed these strings" case. Both compose to the same internal representation by mapping each string through `Line.FromText`.
- **Backward compatibility:** every overload is an addition. The original `IReadOnlyList<Line>` constructors stay first in the file so default-parameter rules don't shift overload resolution.

### 3. `IScrollback.Append(string)` overload

Same shape as the request overloads. Internally calls `Append(Line.FromText(text))`. One line in `ScrollbackSurface`, one new method on `IScrollback`.

- **Why on the interface:** consumers depend on `IScrollback`. Putting the overload only on the concrete `ScrollbackSurface` would invisibly fork the test-fake surface. The interface gets the overload; implementers (production + the tier-A fake) get a one-line default forwarder.

### 4. `AllowBack` flag on `SelectRequest` / `ChoiceRequest`; Backspace-at-empty as the keybinding

Pass-1 wires Back to a single overlay event: **Backspace pressed when the dialog is at its initial (no movement, no input) position**.

- **Why Backspace specifically:** the Latin keyboard intuition for "go back" is Backspace; this is what every browser/wizard/CLI back-stack consumer expects. It does not collide with arrow nav (still consumes `↑↓`) or Enter (Submit) or Esc (Cancel).
- **Why "at empty position" / opt-in:** Backspace is a printable-ish key in many overlays. In a Select dialog with no list movement yet the keystroke is unambiguous; once the user has moved the selection it could plausibly mean "reset". The simplest semantic for pass-1: **only fire Back if the user has not yet moved the selection AND `AllowBack=true`.** Once the user has touched `↑↓`, Backspace becomes a no-op for the rest of that overlay session. Consumers that want unconditional Back can simply re-call the dialog.
- **Why opt-in (`AllowBack=false` default):** existing `SelectAsync` callers must not change behaviour. Today Backspace is a no-op in a Select dialog; that stays the case unless the consumer requests `AllowBack=true`.
- **Why no flag on `MultiSelectRequest`:** in multi-select, Backspace-at-empty-position could mean "unselect last" or "go back". Both are reasonable; neither dominates. Defer to a pass-2 design conversation rather than pick the wrong one now.
- **Fallback to `[`:** **rejected for pass-1.** A single keybinding is easier to test and document. If real users hit a conflict, pass-2 can add `[` as an alternate; no flag-design lock-in is needed today.
- **Alternative considered — Escape-twice:** rejected. Esc already means Cancel and conflicts with the modal-dismiss semantic. Two Escs would need a 500ms-style double-tap window — far more complex than Backspace-at-empty.

### 5. Secret-default rendering tweak

When `InputDialog` is constructed with `IsSecret=true` and a non-empty `Default`, the first paint renders the default as `'•' * displayWidth(default)` instead of the real string. Mechanism:

- The dialog already tracks whether the user has edited the buffer (it has to, for `InputChanged` emission). Reuse that flag: while `!_userEdited && IsSecret`, paint as masked; otherwise paint normally (which is already masked for `IsSecret=true` via the existing render path).
- `Submit` is unaffected — returns the real buffer contents (the seeded `Default` if untouched, the edited text otherwise).
- **Why not always mask under `IsSecret`:** the existing render path *does* mask, but it masks the *current buffer contents*. The bug is that on first paint the buffer contains the default string un-marked-as-secret. The fix is exactly the "untouched + secret + non-empty default" case.

### 6. No production-loop / scrollback / parser code touched

This change is **surface-only**. The internal loop, scrollback model, fixed-region composer, frame painter, and parser stay identical. The whole change should fit in:
- `src/Dcli/Line.cs` (factory).
- `src/Dcli/IScrollback.cs` + `src/Dcli/ScrollbackSurface.cs` (overload).
- `src/Dcli/InputRequest.cs` / `SelectRequest.cs` / `MultiSelectRequest.cs` / `ChoiceRequest.cs` (overloads + `AllowBack` flag on the two relevant records).
- `src/Dcli/Internal/FixedRegion/Dialog.cs` + `ChoiceDialog.cs` (Back keybinding, gated on the new flag).
- `src/Dcli/Internal/FixedRegion/InputDialog.cs` (secret-default mask).
- The fakes (`tests/Dcli.Tests/FakeTerminalTests.cs`) get the same overloads as fire-and-forget pass-throughs — preserves tier-A symmetry.

### 7. Tests ride on `Dcli.Testing` only

Every new test lives in `tests/Dcli.Tests/` and uses `HeadlessTerminal` from `Dcli.Testing` rather than a hand-rolled fake. Tier-A `FakeTerminalTests` adds the new overload assertions so the fakeability contract still covers the new surface.

## Risks / Trade-offs

- **Adding `Line.FromText` invites convergence at call sites** — if a future change adds another shorthand, the conventions can drift (e.g. `Line.FromText(s, style)` vs `Line.Of(s)`). *Mitigation:* document the factory as the canonical short form; future changes that want different shorthands must justify why `FromText` doesn't cover their case.
- **Backspace-at-empty is a heuristic, not a contract** — a future overlay where Backspace has a different meaning (e.g. a future code-completion overlay) might collide. *Mitigation:* the trigger is opt-in (`AllowBack=false` default); collisions surface only when a consumer asks for both.
- **Pass-1 doesn't cover multi-select Back** — wizards that want a "back through a multi-select step" lose the affordance. *Mitigation:* consumers wrap multi-select in a state machine that maps "cancel" → "go back" and re-shows; or wait for pass-2. Document this on `MultiSelectRequest`.
- **Secret-default masking depends on the existing `_userEdited` flag being honest** — if a programmatic `SetText` mutates the buffer without flipping the flag, the mask would persist (or, depending on direction, the leak returns). *Mitigation:* the existing `InputDialog` already has to handle this for `InputChanged` emission, so the bug surface is shared, not new. Add a test that confirms a programmatic `SetText` (post-construction) flips the flag *or* keeps the mask, whichever is decided in code review.
- **Implicit `string → Line` rejected** — some consumers may write `term.Scrollback.Append("hello")` and expect default styling, and want to upgrade to styled later. They have to opt into `Line.FromText("hello", new Style(...))` or the full `LineBuilder` at that point. *Mitigation:* both forms read fine; the explicit upgrade path is healthier than silent stylelessness.

## Migration Plan

- Open the change on a branch `change/api-ergonomics-pass-1` off `main`.
- Apply per `tasks.md`, one §-section per commit, gated by the standard four gates + reviewer audit.
- No archived spec breakage — every delta is `MODIFIED` with strictly additive requirements.
- After archive, run `openspec sync-specs` (manual if needed) to fold the deltas into `openspec/specs/{styled-text,fixed-region}/spec.md`.
- Bump dcli version to `0.2.0-rc.1` (preview minor — additive, no breaking changes). `Dcli.Testing` versions in lockstep.
- Open the follow-up `dmon-migration` change *after* this lands so the migration uses the new surface directly.

## Open Questions

None. The Backspace-at-empty trigger is a deliberate pick (Decision 4); the secret-default tweak is unambiguous (Decision 5); the rejected-implicit-conversion is settled (Decision 1).
