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
| 1 | `Line.FromText` factory | `3dd518c` | 693 (688 + 5) | XML doc also states "no implicit `string → Line` conversion is defined" — covers the doc half of task 2.9 ahead of §2. |
| 2 | String-accepting consumer overloads | _pending_ | 702 (693 + 9) | Tasks.md target paths (`IScrollback.cs`, `*Request.cs` per record) didn't match real layout; actually edited `ITerminal.cs` (interface) and `DialogRequests.cs` (all four records together). `new InputRequest(null)` is ambiguous between the `Line?` primary and the new `string?` secondary; resolved at call sites via `new InputRequest()` (primary all-defaulted) or explicit `(string?)null` / `Line.FromText(...)`. Tests cover both. |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped. Examples worth recording for this change:

- Backspace-at-empty-position semantics for `AllowBack` (Decision 4 in `design.md`) — if a reviewer or implementation reveals a case where the heuristic fails, log it here and propose the resolution before proceeding to §3.6/§3.7 tests.
- Implicit `string → Line` conversion is **rejected** (Decision 1) — if a consumer migration would have been much cleaner with the implicit, log the case so a future change can revisit.
- Secret-default masking reuses the existing `_userEdited` flag (Decision 5) — if reviewer finds the flag is dirty (programmatic `SetText` mutates without flipping it), the resolution goes here.

**§2 — `tasks.md` paths deviate from real layout.** Tasks 2.1, 2.3–2.6 named per-record files (`src/Dcli/IScrollback.cs`, `src/Dcli/InputRequest.cs`, `src/Dcli/SelectRequest.cs`, etc.) that don't exist. The actual layout consolidates: `IScrollback` lives in `src/Dcli/ITerminal.cs` next to the façade interface, and all four `*Request` records live in `src/Dcli/DialogRequests.cs`. Implemented in the real files; did not split them out (that would have been scope creep — Decision 6 is surface-only). No spec amendment needed; the file paths in `tasks.md` were ergonomic shorthand for "the file that contains this type".

**§2 — `new InputRequest(null)` is ambiguous (expected).** With both `InputRequest(Line? Prompt = null, …)` and `InputRequest(string? prompt, …)` present, a bare `null` literal as the first argument cannot resolve. This is documented behaviour for C# overload resolution and matches the design's "explicit only" stance (Decision 1). Mitigation: the natural empty-input call `new InputRequest()` resolves unambiguously to the primary (all defaults); callers who want a null prompt explicitly use `new InputRequest((string?)null)` or `new InputRequest((Line?)null)`. Both forms covered by `FacadeTests` (`InputRequestDefaultCtorRemainsUnambiguous`, `InputRequestNullStringPromptProducesNullPrompt`).

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

> **Currently at §3.1 — `AllowBack` flag on `SelectRequest`.** §2 shipped (string-accepting overloads across `IScrollback`/`*Request`s + tier-A fake symmetry + facade round-trips; 702 tests green, 0 warnings, format/validate clean, zero `implicit operator` hits in `src/`). Next worker brief: implement §3 end-to-end — add `bool AllowBack = false` to `SelectRequest`/`ChoiceRequest` (3.1–3.2), wire Backspace-at-empty in `Dialog.cs` (3.3–3.4), map `OverlayCloseKind.Back` → `DialogOutcome.Back` in the dismiss hook (3.5), tests in `DialogSelectionTests`/`ChoiceDialogTests` (3.6–3.7), and confirm `MultiSelectRequest` deliberately lacks `AllowBack` (3.8).
