# Design — back-nav-input

## Context

The awaitable-dialog family (`SelectAsync`, `ChoiceAsync`, `MultiSelectAsync`, `InputAsync`) shares a
single close-outcome model: `OverlayCloseKind` (Submit / Back / Cancel) at the overlay layer,
mapped to `DialogOutcome` (Submitted / Back / Cancelled) by `Terminal.OpenModalAsync`. Three of the
four request types already expose an opt-in `AllowBack` flag; `InputRequest` does not. This change
closes that one gap. The `OverlayCloseKind.Back` plumbing (overlay → `OpenModalAsync` → `DialogResult`)
already exists and is exercised by the list dialogs, so the work is confined to the request record,
the input overlay, the `InputAsync` wiring, and one stale doc comment.

## Goals / Non-Goals

**Goals**

- Add `InputRequest.AllowBack` (opt-in, default `false`, source-compatible).
- Emit `DialogOutcome.Back` from the input dialog on **Backspace while the field is currently empty**
  when `AllowBack=true`.
- Keep the family's doc surface honest (`DialogOutcome.Back` remarks).

**Non-Goals**

- No change to multi-select (`[`-based Back already shipped in rc.3).
- No new `Terminal` API beyond the `AllowBack` pass-through.
- No change to the `OverlayCloseKind` → `DialogOutcome` mapping in `OpenModalAsync` (already generic).

## Decisions

### Decision 1 — The unifying family rule: "Backspace when there's nothing left to delete → Back"

`SelectRequest`/`ChoiceRequest` bind **Backspace-before-movement** to Back. `MultiSelectRequest` uses
`[` because Space-toggle interplay makes a Backspace-position heuristic unreliable. For a text field
the natural analogue is **Backspace-on-empty**: in an empty field Backspace is otherwise a no-op, so
rebinding it is unambiguous and discoverable — the user is already pressing the "go back / delete the
last thing" key, and there is nothing left to delete. This keeps the whole family under one mental
model: *Backspace when there's nothing left to delete → Back.*

- **Alternative — `[` as the Back key (as in multi-select):** rejected. `[` is a literal character
  users routinely type into a free-text field (URLs, JSON, array indices, regexes, keys). Binding it
  would steal a printable character and corrupt legitimate input. Multi-select can afford `[` because
  it has no text buffer; the input dialog cannot.
- **Alternative — restrict Back to a pristine, never-edited field:** rejected. The trigger is
  **currently empty**, not **never edited**. Typing text and then deleting back to empty must still arm
  Back — a user who clears the field and presses Backspace once more clearly intends to leave it. Gating
  on edit history would make the behaviour depend on invisible state and surprise the user.

### Decision 2 — Trigger on buffer emptiness, not on `_userEdited`

`InputDialog` already tracks `_userEdited` (sticky, set by any insert/Backspace/Delete) for masking
semantics. The Back trigger MUST **not** use `_userEdited`; it MUST check the live buffer text length
(`Text.Length == 0`). The Backspace handler runs before any mutation, so the check is "is the field
empty *at the moment Backspace is pressed*". When `AllowBack=true` and the buffer is empty, set
`CloseRequest = OverlayCloseKind.Back` and return consumed; otherwise fall through to the existing
`_buffer.Backspace()` delete path (which sets `_userEdited`). When `AllowBack=false`, Backspace-on-empty
remains a harmless no-op exactly as today.

### Decision 3 — Place the Back branch inside the existing Backspace case

The new branch lives in the `NamedKey.Backspace` arm of `HandleKey`, guarded by
`_allowBack && _buffer.Text.Length == 0`. Enter (Submit) and Escape (Cancel) precedence is unchanged
and sits above it; printable-rune insertion is unaffected. Ctrl/Alt-modified Backspace continues to
fall to the modal catch-all (the existing Ctrl/Alt gate runs before the named-key switch), so
`Ctrl+Backspace` never triggers Back — consistent with the printable-key gate.

### Decision 4 — Constructor surface mirrors `MultiSelectRequest`

Add `bool AllowBack = false` as the trailing parameter of the primary constructor and of the
convenience overloads that already accept `Default`/`IsSecret` (the `Line?`, `string?`, and
`IReadOnlyList<string>?` forms). The two `params`-tail overloads (`params Line[]`, `params string[]`)
keep their current signatures — `params` must be the last parameter, so `AllowBack` cannot be added
there without a breaking reshape; callers needing `AllowBack` use one of the non-`params` overloads,
exactly as documented for `MultiSelectRequest`. All defaults stay `false`, so existing call sites
recompile unchanged.

### Decision 5 — `DialogOutcome.Back` docs become family-accurate

The enum-member and `<remarks>` text currently name only `SelectAsync`/`ChoiceAsync` and describe
only the Backspace-before-movement trigger. Update them to enumerate all four producers and each
trigger: Backspace-before-movement (Select/Choice), `[` (MultiSelect), Backspace-on-empty (Input).
This is a doc-only edit but is in-scope because the change makes the old text actively wrong.

## Risks / Trade-offs

- **A user who clears a pre-filled (`Default`) field and presses Backspace gets Back, not a no-op.**
  → Intended under Decision 1; this is the discoverable affordance. Consumers that do not want Back
  simply leave `AllowBack=false` (the default).
- **Secret fields (`IsSecret=true`) with `AllowBack=true`:** Backspace-on-empty still arms Back. No
  special-casing — the masking layer only affects rendering, not the buffer length the trigger reads.
- **Existing tests assume Backspace-on-empty is a no-op.** → Those tests construct the dialog with the
  default `AllowBack=false`, so behaviour is unchanged; the Back path only activates on opt-in.

## Migration Plan

Purely additive. No migration for existing consumers — `AllowBack` defaults to `false` everywhere.
Ship as rc.4. Downstream (dmon) adopts by bumping the dcli reference and setting `AllowBack = true`
on input steps in wizard flows.

## Open Questions

None — the brief settled the key bindings and the trigger semantics.
