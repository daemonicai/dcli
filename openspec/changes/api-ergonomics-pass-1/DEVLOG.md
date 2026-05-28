# DEVLOG — `api-ergonomics-pass-1`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see the project's `CLAUDE.md`). Captures per-section narrative the spec
> files don't carry: decisions under uncertainty, deviations, surfaced bugs, HITL
> verifications. On archive this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-api-ergonomics-pass-1/DEVLOG.md` and the
> status flips to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/api-ergonomics-pass-1`** (created from `main`). Stay on it.
- Working tree state: **CLEAN** (no section work has started; only OpenSpec artefacts + this DEVLOG are committed).
- Sanity check command:
  `dotnet build -c Release && dotnet test -c Release && dotnet format --verify-no-changes && openspec validate api-ergonomics-pass-1 --strict`
  → expect **0 warnings, 688 tests green** (baseline from `core-rendering-architecture`), clean format, valid.
- Resume point: **§1 — `Line.FromText` factory** (first unticked task: `1.1`). See the Section status table for what's done so far.
- Check the memory files listed at the bottom before briefing — several encode hard-won constraints that survive into this change (in particular: CA2007 / loop-thread discipline if any new dialog test touches the loop).

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | `Line.FromText` factory | _pending_ | 693 (688 + 5) | XML doc also states "no implicit `string → Line` conversion is defined" — covers the doc half of task 2.9 ahead of §2. |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped. Examples worth recording for this change:

- Backspace-at-empty-position semantics for `AllowBack` (Decision 4 in `design.md`) — if a reviewer or implementation reveals a case where the heuristic fails, log it here and propose the resolution before proceeding to §3.6/§3.7 tests.
- Implicit `string → Line` conversion is **rejected** (Decision 1) — if a consumer migration would have been much cleaner with the implicit, log the case so a future change can revisit.
- Secret-default masking reuses the existing `_userEdited` flag (Decision 5) — if reviewer finds the flag is dirty (programmatic `SetText` mutates without flipping it), the resolution goes here.

*(No decisions logged yet — change has not started.)*

## Human-in-the-loop verifications

Anything that can't be settled by automated gates. For each: section reference, exact copy-pasteable command, what the user should see, and current status.

*(No HITL verifications anticipated for this change — the §14.4 manual harness invariants are preserved; the new surface is fully testable via `Dcli.Testing.HeadlessTerminal`. If a §5 demo update or §3 `AllowBack` keybinding reveals a real-terminal interaction worth eyeballing, log it here.)*

## Open follow-ups / known gaps (after this change lands — NOT in scope here)

Surface gaps for future changes. Link to memory files where the constraint is encoded so the next change's orchestrator picks them up.

- **`DialogOutcome.Back` for multi-select** — deferred from this change (Backspace-at-empty-toggle-position is ambiguous in multi-select; see `design.md` Decision 4). A future change can revisit once a real consumer needs it.
- **`Input.Prompt` / `Input.ReadOnly` on the fixed-region input surface** — still deferred from §12 of the architecture change. Memory file: [[section14-api-ergonomics-findings]] entry 3-related; not blocked by this change.
- **`Scrollback.AppendRule`, incremental `Collapsible.AppendLine`, `PasteEvent` editor routing** — separate future changes; this change does not touch them.
- **VT-escape sanitisation of `Segment.Text`** — separate future change. Memory file: [[vt-escape-sanitization-gap]].

## Memory files (indexed by `~/.claude/projects/-Users-emmz-github-emmz-dcli/memory/MEMORY.md`)

- [[section14-api-ergonomics-findings]] — the five §14.4 ergonomics gaps; this change closes three of them. Source of truth for which gaps are in scope vs deferred.
- [[ca2007-render-loop-thread-discipline]] — CA2007 is suppressed repo-wide; loop-thread correctness must be checked by hand. Applies to any new dialog test that posts commands to `LoopEngine.InputWriter`.
- [[restore-on-signal-rendering-state]] — restore-on-signal protocol is load-bearing; not touched by this change but worth knowing if any new render-path code is added.

## Resume point

> **Currently at §2.1 — `IScrollback.Append(string text)` overload.** §1 shipped (`Line.FromText` factory + 5 tests; build 0 warnings, 693 tests green, format/validate clean). Next worker brief: implement §2 end-to-end — `IScrollback.Append(string)` (2.1–2.2), the `*Request` string overloads (2.3–2.6), `FacadeTests` round-trips (2.7), `FakeTerminalTests` tier-A symmetry (2.8), and confirm no implicit conversion via grep (2.9; the doc-half is already in `Line.FromText`'s XML).
