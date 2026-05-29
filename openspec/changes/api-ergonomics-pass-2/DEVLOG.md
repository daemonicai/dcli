# DEVLOG — `api-ergonomics-pass-2`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see the project's `CLAUDE.md`). Captures per-section narrative the spec
> files don't carry: decisions under uncertainty, deviations, surfaced bugs, HITL
> verifications. On archive this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-api-ergonomics-pass-2/DEVLOG.md` and the status flips
> to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/api-ergonomics-pass-2`** (created from `main`). Stay on it.
- Working tree state: CLEAN (§1–§3 committed; §4 unstarted).
- Sanity check command:
  `dotnet build && dotnet test && dotnet format --verify-no-changes && openspec validate api-ergonomics-pass-2 --strict`
- Resume point: **§4 — PasteEvent editor routing** (first unticked task: `4.1`). See the Section status table for what's done so far.
- Check the memory files listed at the bottom before briefing — several encode hard-won constraints for upcoming sections (render-loop thread discipline for §2/§3/§4; oversized-reprint ordering for §3).

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Line single-style shorthand factories | `9292b4d` | 836 (827 + 9) | Bold/Dim/Fg/Bg as thin sanitizing wrappers over `FromText`. Reviewer clean first pass (all 5 dimensions); null-guard inherited from `FromText` (accepted — message names `text` correctly); the repeated "no Raw" doc paragraph is mandated by task 1.5, not noise. Tests added to `tests/Dcli.Tests/StyledTextTests.cs`. |
| 2 | Scrollback.AppendRule | `512374a` | 838 (836 + 2) | New `RuleBlock : ILineObject` resolves width at paint time (re-expands on resize for free); `AppendRule()` posts a private nested `AppendRuleToScrollbackCommand` mirroring `AppendToScrollbackCommand`; the `// AppendRule … deferred` gap comment removed. Surfaced: adding a member to `IScrollback` required a `FakeScrollback.AppendRule()` stub in `FakeTerminalTests.cs`. Reviewer round 1 should-fix: the test hand-rolled doubles instead of `HeadlessTerminal`/`FrameSnapshot` — rewritten onto the real harness (also fixed a latent size-source/resize-watcher desync by using `harness.Resize`). Round 2 clean. |
| 3 | Incremental Collapsible.AppendLine | `<§3 hash>` | 844 (838 + 6) | `Collapsible._hiddenLines` → owned `List<Line>` (`.ToList()` copy in ctor — also closes a latent off-thread-mutation leak); `AppendHidden` dumb-add; new `ScrollbackModel.AppendToCollapsible` with the SAME guard precedence as `ExpandCollapsible` (horizon-freeze → already-expanded → act); façade `AppendLine(Line)`/`(string)` via nested `AppendLineToCollapsibleFacadeCommand`. Tests are **model-level** (`ScrollbackModelTests` house style — HeadlessTerminal can't observe `IsExpanded`/`NewlyCommittedRows`), incl. a 3.7 oversized-reprint ordering regression. Reviewer round 1 should-fix: worker left an orphan `Commands/AppendToCollapsibleCommand.cs` (never instantiated) — deleted. Round 2 clean. Decision-4 holds: append touches only the pre-expansion snapshot and never initiates a reprint, so it can't worsen [[scrollback-oversized-reprint-ordering]]. |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped.

- **Scope (2026-05-29, pre-flight).** All five candidate items triaged IN by the user: Line single-style factories (Bold/Dim/Fg/**Bg** — not full LineBuilder parity), Scrollback.AppendRule, incremental Collapsible.AppendLine, PasteEvent editor routing, and multi-select Back via `[`. Explicitly OUT: Italic/Underline/Reverse/Strikethrough factories, and `Line.Raw` (rejected — preserves the single-verbatim-seam invariant; see design Decision 2 and [[vt-escape-sanitization-gap]]).
- **§5 reverses a shipped spec sentence.** The live `fixed-region` spec said "MultiSelectRequest SHALL continue to omit AllowBack"; this change reverses it, binding `[` (not Backspace) to Back for multi-select to sidestep the Backspace-at-toggle ambiguity that caused two prior deferrals. This is the one genuinely contended edit — flagged for the reviewer.

## Human-in-the-loop verifications

Anything that can't be settled by automated gates. For each: section reference, exact copy-pasteable command, what the user should see, and current status.

- **§4 — PasteEvent into the input editor (likely HITL).** Pasting into a real terminal's bracketed-paste path and observing caret/wrap behaviour + secret-default flip may warrant an eyeball in the DmonWizard sample once implemented. Headless tests cover the logic; a real-terminal confirmation recipe will be added here if §4 review surfaces the need.
- **§5 — `[`-as-Back keybinding (possible HITL).** Multi-select Back via `[` is fully testable headlessly, but a real-terminal confirmation that `[` doesn't collide with any list interaction may be worth a quick eyeball.

*(Concrete commands/expected-output added when the relevant section lands and the need is confirmed.)*

## Open follow-ups / known gaps (after this change lands — NOT in scope here)

Surface gaps for future changes. Link to memory files where the constraint is encoded.

- **`Italic`/`Underline`/`Reverse`/`Strikethrough` `Line` factories** — deferred (no call sites). A future pass can add them if consumers appear.
- **`Line.Raw`** — deliberately rejected (single-verbatim-seam invariant). Memory: [[vt-escape-sanitization-gap]].
- **`Input.Prompt` / `Input.ReadOnly`** — still deferred from §12 of the architecture change. Memory: [[section14-api-ergonomics-findings]] entry 3-related.
- **`InputDialog` over-budget caret reporting** — flagged by `multi-line-dialog-prompts`; not addressed here.

## Memory files (indexed by `~/.claude/projects/-Users-emmz-github-emmz-dcli/memory/MEMORY.md`)

- [[ca2007-render-loop-thread-discipline]] — CA2007 suppressed repo-wide; loop-thread correctness for the new AppendRule / Collapsible.AppendLine / paste-routing commands must be checked by hand (§2, §3, §4).
- [[scrollback-oversized-reprint-ordering]] — known minor commit-order edge in the oversized-collapsible reprint path; §3 `AppendLine` must not worsen it (design Decision 4 constrains append to the pre-expansion snapshot).
- [[section14-api-ergonomics-findings]] — the original 5-finding candidate list; this change is the second pass against it.
- [[restore-on-signal-rendering-state]] — restore-on-signal protocol is load-bearing; §4 paste routing must add no new restore-path state.

## Resume point

> **Currently at §4.1 — PasteEvent editor routing.** §1–§3 shipped (844 tests). §4 is the heaviest
> section and the most likely to need HITL. Next: brief the `worker` on §4 (route `PasteEvent`
> through the intercept chain into the active input surface; insert at caret display-width/multiline-
> aware; flip the sticky `_userEdited` flag so a seeded secret `Default` switches to buffer-masking).
> Binding: `fixed-region` "Owned input editor" (MODIFIED) — paste is a first-edit trigger. Design
> Decision 5 (reuse the single `_userEdited` flag from pass-1 §4; route overlay-first like keys).
> Watch [[restore-on-signal-rendering-state]] (add NO new restore-path state) and
> [[ca2007-render-loop-thread-discipline]] (insertion runs on the loop thread). The pass-1 §4
> deviation note pre-registered paste as the trigger that "must flip `_userEdited`" — this makes it
> real. Likely HITL: real-terminal bracketed-paste eyeball in the DmonWizard sample (recipe to be
> added to the HITL section if §4 review confirms the need).
