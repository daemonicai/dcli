# DEVLOG — `api-ergonomics-pass-2`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see the project's `CLAUDE.md`). Captures per-section narrative the spec
> files don't carry: decisions under uncertainty, deviations, surfaced bugs, HITL
> verifications. On archive this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-api-ergonomics-pass-2/DEVLOG.md` and the status flips
> to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/api-ergonomics-pass-2`** (created from `main`). Stay on it.
- Working tree state: CLEAN (proposal base committed at `c56dc0f`; no section work yet).
- Sanity check command:
  `dotnet build && dotnet test && dotnet format --verify-no-changes && openspec validate api-ergonomics-pass-2 --strict`
- Resume point: **§1 — Line single-style shorthand factories** (first unticked task: `1.1`). See the Section status table for what's done so far.
- Check the memory files listed at the bottom before briefing — several encode hard-won constraints for upcoming sections (render-loop thread discipline for §2/§3/§4; oversized-reprint ordering for §3).

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| — | (none committed yet) | — | — | proposal/design/specs/tasks base at `c56dc0f` |

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

> **Currently at §1.1 — `Line.Bold` factory.** Proposal base committed (`c56dc0f`); branch
> `change/api-ergonomics-pass-2` created from `main`; DEVLOG scaffolded. Next: brief the `worker`
> on §1 (the four `Line` single-style factories + their `LineTests`), with the spec excerpt from
> `specs/styled-text/spec.md` ("Single-style line shorthand factories") and design Decisions 1–2
> (sanitizing wrappers over `FromText`; no `Raw`, no Italic/Underline/Reverse/Strikethrough).
