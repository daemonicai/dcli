## Context

`dcli` ships an inline terminal-rendering library. Two prior ergonomics changes —
`api-ergonomics-pass-1` (`0.2.0-rc.1`) and `multi-line-dialog-prompts` (`0.2.0-rc.2`) — closed the
first wave of §14.4 dmon-wizard-port friction. This change (`0.2.0-rc.3`) closes a second wave that
mixes two natures:

1. **Pure conveniences** with concrete call-site evidence (the `Line` single-style factories).
2. **Spec/impl gaps the specs already name** but the code never satisfied:
   - `inline-scrollback` "Scrollback command surface" already says the library SHALL expose an
     "append a rule/separator" command, yet there is no `AppendRule` method, scenario, or
     implementation (`ScrollbackSurface` carries an explicit `// AppendRule … deferred` comment).
   - `ScrollbackSurface.BeginCollapsible` documents incremental hidden-line append as "a documented
     gap for a future refinement".
   - `PasteEvent` already exists in the `terminal-input` event model and parser, and the
     `fixed-region` "Owned input editor" requirement already lists "paste" as a first-edit trigger —
     but the editor never inserts paste text.
   - `MultiSelectRequest` Back was deferred in both prior changes over the Backspace-at-toggle
     ambiguity; the live `fixed-region` spec currently says "MultiSelectRequest SHALL continue to omit
     AllowBack".

Binding constraints carried from memory:
- **`ca2007-render-loop-thread-discipline`** — CA2007 is suppressed repo-wide; the render-loop thread
  is the sole mutator of scrollback/editor state. New loop commands and the paste path must be
  checked by hand for thread correctness, not just by the analyzer.
- **`scrollback-oversized-reprint-ordering`** — a known minor commit-order edge exists in the
  oversized-collapsible reprint path; `AppendLine` must not worsen it.
- **`vt-escape-sanitization-gap` (resolved)** — `Segment.Raw` is the single verbatim escape hatch;
  the `Line` shorthands must not open a second one.

## Goals / Non-Goals

**Goals:**
- Add `Line.Bold/Dim/Fg/Bg` single-style factories that are thin, sanitizing wrappers over
  `Line.FromText`, and migrate the sample call sites onto them.
- Implement the `AppendRule` command the spec already mandates, with a width-aware rule line-object.
- Add incremental `Collapsible.AppendLine(Line)` / `(string)` with explicit one-way semantics.
- Route `PasteEvent` into the owned input editor, and make paste count as a first edit (flipping
  secret-default masking).
- Give `MultiSelectRequest` an opt-in `AllowBack` bound to `[`, and accept `[` as a secondary Back
  key on `Select`/`Choice`.
- Ship `0.2.0-rc.3` with all four gates green and no breaking changes.

**Non-Goals:**
- `Italic`/`Underline`/`Reverse`/`Strikethrough` `Line` factories (no call sites) and `Line.Raw`
  (rejected — preserves the single-escape-hatch invariant).
- Rich rule styling (custom glyph palettes); a minimal optional glyph/style at most.
- `Input.Prompt` / `Input.ReadOnly` on the fixed-region input surface (still deferred).
- Tightening the `InputDialog` over-budget caret reporting flagged by `multi-line-dialog-prompts`.
- Any `dmon`-side migration; this change only ships the library surface that unblocks it.

## Decisions

### Decision 1 — `Line` shorthands are sanitizing wrappers over `FromText`, set Bold/Dim/Fg/Bg only
`Line.Bold(s)` ≡ `Line.FromText(s, new Style(Format: Format.Bold))`; `Dim` likewise; `Fg(s, c)` /
`Bg(s, c)` set the color. They route through the ordinary sanitizing `Segment` constructor, so
control/escape neutralization is identical to `FromText`. **Why this set:** the samples only exercise
Bold/Dim/Fg; Bg is added for fg/bg symmetry. *Alternatives:* full `LineBuilder` parity (8 methods) —
rejected, 5 factories would have zero consumers; an instance-fluent `Line.Styled(s).Bold()` — rejected,
heavier than the one-liner it replaces and `LineBuilder` already covers fluent composition.

### Decision 2 — No `Line.Raw`
`Segment.Raw` / `LineBuilder.Raw` stay the only verbatim seams. *Why:* the vt-escape-sanitization
design deliberately narrowed verbatim output to a single, conspicuous path; a top-level `Line.Raw`
factory would widen the unsanitized surface and invite accidental escape smuggling. Consumers needing a
verbatim single-segment line use `new LineBuilder().Raw(s).Build()`. *Alternative:* add `Line.Raw` for
symmetry — rejected on the security argument.

### Decision 3 — `AppendRule` renders a width-aware rule line-object resolved at paint time
`IScrollback.AppendRule()` posts a fire-and-forget loop command (like `Append`) carrying a new
rule line-object. The object holds no fixed width; the renderer expands it to the live-window content
width at paint time, so it stays correct across resize. Keep the public signature minimal —
parameterless in the first cut, with room for an optional glyph/`Style` later without breaking callers.
*Why a distinct line-object* (not a pre-built `Line` of `─`): width is a render-time property; baking a
fixed-width `Line` would be wrong after a resize and would duplicate the wrapping logic. Remove the
`// AppendRule … deferred` gap comment when landed. *Alternative:* `Append(Line.FromText(new string('─',
width)))` at the call site — rejected, pushes width math onto consumers and breaks on resize.

### Decision 4 — `Collapsible.AppendLine` is honored only while collapsed-and-live; otherwise a no-op
The hidden-line list is mutated only by the render-loop thread via a new `AppendLineToCollapsible`
loop command. The command checks the collapsible's state: if it has already expanded, or frozen past
the commit horizon, the append is dropped (no-op), exactly mirroring how `Expand` no-ops past the
horizon. This keeps the one-way invariant intact (you can never *grow* visible content after the
reveal decision is made) and sidesteps the `scrollback-oversized-reprint-ordering` edge — because
`AppendLine` only ever touches the *pre-expansion* hidden snapshot, it cannot interleave with the
oversized-reprint commit ordering, which runs at/after expansion. *Alternative:* allow post-expansion
append (live-growing revealed content) — rejected, it breaks "expand at most once / one-way" and
collides directly with the reprint-ordering edge.

### Decision 5 — `PasteEvent` routes through the existing intercept chain; paste is a first edit
Paste is delivered to the same intercept chain as keys: overlay-first (an active `Dialog`/`InputDialog`
consumes it), else the base input editor. The editor's insert path is reused — the paste string is
inserted at the caret as one edit with display-width/multiline wrapping, identical to typed insertion.
The single sticky `_userEdited` flag introduced in pass-1 §4 is flipped by the paste path, so a seeded
secret `Default` switches from default-masking to buffer-masking on the next paint. *Why reuse
`_userEdited`:* pass-1's DEVLOG explicitly pre-registered paste/history-recall as future edit triggers
that "must flip `_userEdited` too"; this change makes that real. *Alternative:* a separate paste flag —
rejected, pass-1 deliberately introduced exactly one edit flag.

### Decision 6 — Multi-select Back uses `[`; Select/Choice gain `[` as a secondary key
`MultiSelectRequest` gets `AllowBack` (default `false`). When `true`, `[` at any time → `Back`, with no
movement-suppression (toggling does not disarm it). Select/Choice keep their pass-1
Backspace-before-first-move binding and additionally accept `[` (also movement-suppressed for them, to
match Backspace). *Why `[` for multi-select:* Space toggles and Backspace would be needed for nothing
here, but a Backspace-position heuristic is unreliable amid toggling — a distinct, never-printable-in-a-
list key removes the ambiguity that caused two prior deferrals. *Why also on Select/Choice:* one Back
key across all three dialogs is less surprising for consumers than a per-dialog split. The internal
`OverlayCloseKind` enum already has `Back` (pass-1); only the key-handler arms and the request flag are
new. *Alternative:* keep multi-select Back-less — rejected, the user asked to settle it; *Alternative:*
Backspace-before-first-toggle for multi-select — rejected as the ambiguous path both prior changes
declined.

### Decision 7 — Section = work item = one commit; samples migrate in their own section
tasks.md is organized so each work item is a `## N.` section committed independently per the repo
apply workflow, a sample-migration section (mirroring pass-1 §5) moves WizardRenderer.cs /
Program.cs single-style sites onto the new factories, and a final Validation & packaging section runs
the gates and bumps `0.2.0-rc.3` in both `.csproj` files plus `dotnet pack`.

## Risks / Trade-offs

- **[Paste routing touches the load-bearing intercept chain and secret-masking path]** → Reuse the
  existing key-routing chain and the single `_userEdited` flag rather than adding parallel paths; pin
  the secret-default-flip behaviour with a `HeadlessTerminal` regression test driving a `PasteEvent`.
- **[New loop commands could violate single-thread mutation]** (`ca2007-render-loop-thread-discipline`)
  → `AppendRule` and `AppendLineToCollapsible` mutate state only inside the loop-thread command apply,
  matching `Append`/`Expand`; reviewer checks thread correctness by hand (CA2007 won't catch it).
- **[`AppendLine` interacting with the oversized-reprint ordering edge]**
  (`scrollback-oversized-reprint-ordering`) → constrain `AppendLine` to the pre-expansion hidden
  snapshot only (Decision 4), so it never participates in the reprint-commit ordering; add a test that
  appends, then triggers an oversized expansion, and asserts ordering is unchanged from the baseline.
- **[`[` is a printable character a consumer might expect in a filter]** → `[`-as-Back is gated behind
  opt-in `AllowBack=true`; with the default `false` it has no effect, and the affected dialogs
  (Select/MultiSelect/Choice) do not type-to-filter on bracket characters today. Documented on the
  flag.
- **[Width-aware rule rendering must stay correct across resize]** → the rule line-object resolves its
  width at paint time (Decision 3); a resize test asserts the rule re-expands.

## Migration Plan

No data or protocol migration. Rollout is the standard section-by-section apply on
`change/api-ergonomics-pass-2`, one commit per section, ending with the `0.2.0-rc.3` version bump and
`dotnet pack`. Rollback is reverting the branch — every addition is opt-in or additive, so reverting
cannot break a consumer that was already compiling against `rc.2`.

## Open Questions

None blocking. Minor, resolvable during implementation without a spec change:
- Whether `AppendRule` takes an optional rule glyph / `Style` in this cut or stays parameterless — lean
  parameterless (Decision 3 leaves room to add an overload later without breaking callers).
- Whether the `[` secondary-Back binding on Select/Choice warrants its own scenario beyond the one in
  the spec delta — the reviewer can request additional coverage if the single scenario is thin.
