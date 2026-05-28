# DEVLOG — `api-ergonomics-pass-1`

> **Status: shipped.** Archived `2026-05-28` to `openspec/changes/archive/2026-05-28-api-ergonomics-pass-1/`.
> Final on-branch commit before archive: `f3ac49b` (§6 hash backfill) — see Section status for
> per-section hashes. Branch: `change/api-ergonomics-pass-1`. Delta specs synced into
> `openspec/specs/{styled-text,fixed-region}/spec.md` immediately before the archive move.

## Final state at archive

- Branch: **`change/api-ergonomics-pass-1`** (created from `main`). Held all section commits.
- Working tree at archive: clean post-sync; the archive move was its own commit.
- Sanity check command (post-archive, against `main`-side specs):
  `dotnet build -c Release && dotnet test -c Release && dotnet format --verify-no-changes`
  → 0 warnings, **714 tests green** (688 baseline + 26 new across §§1-4), format clean.
- All six sections shipped. See Section status table for per-section commit hashes and tests-after counts.
- Memory files referenced during the change: see "Memory files" section below — those constraints survive into future changes.

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | `Line.FromText` factory | `3dd518c` | 693 (688 + 5) | XML doc also states "no implicit `string → Line` conversion is defined" — covers the doc half of task 2.9 ahead of §2. |
| 2 | String-accepting consumer overloads | `55cd4de` | 702 (693 + 9) | Tasks.md target paths (`IScrollback.cs`, `*Request.cs` per record) didn't match real layout; actually edited `ITerminal.cs` (interface) and `DialogRequests.cs` (all four records together). `new InputRequest(null)` is ambiguous between the `Line?` primary and the new `string?` secondary; resolved at call sites via `new InputRequest()` (primary all-defaulted) or explicit `(string?)null` / `Line.FromText(...)`. Tests cover both. |
| 3 | `AllowBack` flag on `SelectRequest`/`ChoiceRequest` | `fb90d34` | 709 (702 + 7) | No separate `ChoiceDialog` — `Choice` reuses the unified `Dialog` (`Terminal.cs:195`); created new `ChoiceDialogTests.cs` rather than splitting the class. `OverlayCloseKind` enum ordering becomes `Submit, Back, Cancel` (internal enum so no ABI consumer impact). The `Dialog.HandleKey` Backspace-Back branch is placed BEFORE the existing `TypeToFilter` Backspace-trim branch, with a `_filterText.Length == 0` guard defending against any future widening of `TypeToFilter` to Select/Choice. |
| 4 | Secret-default masking in `InputDialog` | `9cbd883` | 714 (709 + 5) | **Bug didn't actually exist** — `InputDialog.Render` has unconditionally masked the buffer rows whenever `_isSecret=true` since the file was introduced in `core-rendering-architecture` (`394c9ba`). Proposal/design Decision 5's premise that "bullets only kick in once the user edits" was inaccurate. The spec scenarios still hold and are now pinned by regression tests. Introduced `_userEdited` (sticky) as a single edit-flag — it doesn't change rendering today but aligns the code with the spec's vocabulary and pins sticky semantics for any future refactor that gates masking on edit-state. |
| 5 | Demo updates | `3bde5fd` | 714 (unchanged — samples only) | `WizardRenderer.cs` 12 → 10 LineBuilders (2 label-only `Line.FromText` replacements); `Dcli.Demo/Program.cs` 43 → 37 (6 replacements — 2 via `Scrollback.Append(string)`, 4 via `Line.FromText` in MultiSelect items). 5.3 took Option A — `WizardEngine.cs` already routed `WizardStepOutcome.Back`, so activating `AllowBack: true` in `RenderChooseOneAsync` + mapping `DialogOutcome.Back → WizardStepOutcome.Back` was sufficient; no engine change. |
| 6 | Validation & packaging | `a9c64e1` | 714 (unchanged) | Orchestrator-direct (no feature code, so no worker/reviewer cycle). Gates re-ran clean: build 0 warnings, 714 tests green, format clean, validate clean. Versions bumped `0.1.0-rc.1 → 0.2.0-rc.1` in both `Dcli.csproj` and `Dcli.Testing.csproj`. `dotnet pack -c Release` produced all four expected artifacts: `dcli.0.2.0-rc.1.{nupkg,snupkg}` and `dcli.testing.0.2.0-rc.1.{nupkg,snupkg}`. CLAUDE.md "Where to look for historical context" subsection updated to the canonical devlog-skill wording ("also keep" / "inside the change directory"). |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped. Examples worth recording for this change:

- Backspace-at-empty-position semantics for `AllowBack` (Decision 4 in `design.md`) — if a reviewer or implementation reveals a case where the heuristic fails, log it here and propose the resolution before proceeding to §3.6/§3.7 tests.
- Implicit `string → Line` conversion is **rejected** (Decision 1) — if a consumer migration would have been much cleaner with the implicit, log the case so a future change can revisit.
- Secret-default masking reuses the existing `_userEdited` flag (Decision 5) — if reviewer finds the flag is dirty (programmatic `SetText` mutates without flipping it), the resolution goes here.

**§2 — `tasks.md` paths deviate from real layout.** Tasks 2.1, 2.3–2.6 named per-record files (`src/Dcli/IScrollback.cs`, `src/Dcli/InputRequest.cs`, `src/Dcli/SelectRequest.cs`, etc.) that don't exist. The actual layout consolidates: `IScrollback` lives in `src/Dcli/ITerminal.cs` next to the façade interface, and all four `*Request` records live in `src/Dcli/DialogRequests.cs`. Implemented in the real files; did not split them out (that would have been scope creep — Decision 6 is surface-only). No spec amendment needed; the file paths in `tasks.md` were ergonomic shorthand for "the file that contains this type".

**§4 — Secret-default leak the spec defends against doesn't actually exist in current code.** Proposal claim ("Today bullets only kick in once the user edits, so the seeded default leaks clear-text on first paint") and design.md Decision 5 ("The dialog already tracks whether the user has edited the buffer (it has to, for `InputChanged` emission). Reuse that flag.") are both inaccurate about the as-shipped code. Verified by trace + git history: `InputDialog.Render` runs `_isSecret ? MaskRows(rows) : rows` unconditionally on every paint since the file was introduced (`394c9ba`, §12 of core-rendering-architecture). There is no `_userEdited` flag in the original code and no `InputChanged` event is emitted at all. The seeded default flows through `_buffer.SetText` in the ctor → `_buffer.Render` → `MaskRows` → width-preserving bullets, on the very first paint. Conclusion: spec scenarios still hold and are now pinned by regression tests, but the rationale paragraphs in `proposal.md` / `design.md` are wrong about the bug premise. Resolution: §4 work degenerates to (a) regression tests pinning the three new spec scenarios and (b) introducing a `_userEdited` flag (sticky, flipped on Insert/Backspace/Delete) so the spec's edit-state vocabulary has a code-level seam. Decision 5's "reuse the existing flag" guidance is therefore moot; introduced ONE flag (per task 4.2's "do not introduce a second flag" intent). Future trap to remember: if a paste path or history-recall keybinding is added later, they must flip `_userEdited` too — the spec lists those as edit triggers (`specs/fixed-region/spec.md` "Owned input editor"). Today neither is wired, so this is a placeholder note, not a gap.

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
- **`Line.Bold(text)` / `Line.Dim(text)` / `Line.Fg(text, color)` single-style shorthands** — surfaced by §5 reviewer audit. `WizardRenderer.cs` still has ~10 `new LineBuilder().Bold(s).Build()` / `.Dim(s)` / `.Fg(s, color)` sites; `Dcli.Demo/Program.cs` has more. Each is one styled segment on a label string. A future ergonomics-pass-2 change could add direct `Line.Bold(s)` etc. factories analogous to `Line.FromText(s)`. Not in scope for pass-1 (proposal limited to `FromText`); recorded here as a concrete candidate.

## Memory files (indexed by `~/.claude/projects/-Users-emmz-github-emmz-dcli/memory/MEMORY.md`)

- [[section14-api-ergonomics-findings]] — the five §14.4 ergonomics gaps; this change closes three of them. Source of truth for which gaps are in scope vs deferred.
- [[ca2007-render-loop-thread-discipline]] — CA2007 is suppressed repo-wide; loop-thread correctness must be checked by hand. Applies to any new dialog test that posts commands to `LoopEngine.InputWriter`.
- [[restore-on-signal-rendering-state]] — restore-on-signal protocol is load-bearing; not touched by this change but worth knowing if any new render-path code is added.

## Resume point

> **Shipped — `2026-05-28`.** All six sections landed on `change/api-ergonomics-pass-1`; final
> branch commit `f3ac49b`. Delta specs synced into the main `openspec/specs/{styled-text,fixed-region}/`
> immediately before archive. NuGet artifacts produced: `dcli.0.2.0-rc.1.{nupkg,snupkg}` and
> `dcli.testing.0.2.0-rc.1.{nupkg,snupkg}`. Follow-ups (multi-select Back, `Input.Prompt`/`ReadOnly`,
> `Scrollback.AppendRule`, incremental `Collapsible.AppendLine`, `PasteEvent` editor routing,
> VT-escape sanitisation of `Segment.Text`, and a likely ergonomics-pass-2 for
> `Line.Bold(s)`/`Dim(s)`/`Fg(s,color)` shorthands) live as separate future OpenSpec changes —
> see "Open follow-ups" above.
