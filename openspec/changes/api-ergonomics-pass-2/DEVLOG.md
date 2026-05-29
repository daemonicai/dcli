# DEVLOG — `api-ergonomics-pass-2`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see the project's `CLAUDE.md`). Captures per-section narrative the spec
> files don't carry: decisions under uncertainty, deviations, surfaced bugs, HITL
> verifications. On archive this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-api-ergonomics-pass-2/DEVLOG.md` and the status flips
> to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/api-ergonomics-pass-2`** (created from `main`). Stay on it.
- Working tree state: CLEAN (§1–§7 committed; all tasks ticked; ready to freeze + archive).
- Sanity check command:
  `dotnet build && dotnet test && dotnet format --verify-no-changes && openspec validate api-ergonomics-pass-2 --strict`
- Resume point: **DONE — all 7 sections shipped.** Next action is `/devlog freeze` then propose `/opsx:archive` (awaiting user confirmation).
- Check the memory files listed at the bottom before briefing — several encode hard-won constraints for upcoming sections (render-loop thread discipline for §2/§3/§4; oversized-reprint ordering for §3).

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Line single-style shorthand factories | `9292b4d` | 836 (827 + 9) | Bold/Dim/Fg/Bg as thin sanitizing wrappers over `FromText`. Reviewer clean first pass (all 5 dimensions); null-guard inherited from `FromText` (accepted — message names `text` correctly); the repeated "no Raw" doc paragraph is mandated by task 1.5, not noise. Tests added to `tests/Dcli.Tests/StyledTextTests.cs`. |
| 2 | Scrollback.AppendRule | `512374a` | 838 (836 + 2) | New `RuleBlock : ILineObject` resolves width at paint time (re-expands on resize for free); `AppendRule()` posts a private nested `AppendRuleToScrollbackCommand` mirroring `AppendToScrollbackCommand`; the `// AppendRule … deferred` gap comment removed. Surfaced: adding a member to `IScrollback` required a `FakeScrollback.AppendRule()` stub in `FakeTerminalTests.cs`. Reviewer round 1 should-fix: the test hand-rolled doubles instead of `HeadlessTerminal`/`FrameSnapshot` — rewritten onto the real harness (also fixed a latent size-source/resize-watcher desync by using `harness.Resize`). Round 2 clean. |
| 3 | Incremental Collapsible.AppendLine | `ad8c8a1` | 844 (838 + 6) | `Collapsible._hiddenLines` → owned `List<Line>` (`.ToList()` copy in ctor — also closes a latent off-thread-mutation leak); `AppendHidden` dumb-add; new `ScrollbackModel.AppendToCollapsible` with the SAME guard precedence as `ExpandCollapsible` (horizon-freeze → already-expanded → act); façade `AppendLine(Line)`/`(string)` via nested `AppendLineToCollapsibleFacadeCommand`. Tests are **model-level** (`ScrollbackModelTests` house style — HeadlessTerminal can't observe `IsExpanded`/`NewlyCommittedRows`), incl. a 3.7 oversized-reprint ordering regression. Reviewer round 1 should-fix: worker left an orphan `Commands/AppendToCollapsibleCommand.cs` (never instantiated) — deleted. Round 2 clean. Decision-4 holds: append touches only the pre-expansion snapshot and never initiates a reprint, so it can't worsen [[scrollback-oversized-reprint-ordering]]. |
| 4 | PasteEvent editor routing | `7476fb0` | 848 (844 + 4) | New `IOverlay.HandlePaste(string)→bool`; `InputDialog` inserts+flips `_userEdited`+consumes; `Dialog`→`Modal` (modal consumes/ignores, non-modal passes); `Autocomplete`→`false` (pass-through, cursor in base editor). `LoopEngine.ApplyInputEvent` gains a `PasteEvent` case mirroring the KeyEvent intercept chain; base-editor paste emits `InputChanged`; stale "paste not routed" comment removed. **Terminal-safety verified by reviewer:** pasted escape bytes are neutralized on the render path (`TextBuffer.Render`/`MaskLine` build via the sanitizing `Segment` ctor — `Segment.Raw` is NOT on the paste path). Reviewer note (pre-existing, not §4): `_userEdited` has no runtime reader — secret masking is unconditional on `_isSecret`, so "default→buffer masking" coincides (buffer==default before edit); security outcome holds. **No HITL needed** (no raw-mode/real-terminal behaviour changed; headless tests cover it). Round 1 clean. |
| 5 | Multi-select Back via '[' | `7928485` | 856 (848 + 8) | **The one contended edit** — REVERSES the shipped "MultiSelectRequest SHALL continue to omit AllowBack". `MultiSelectRequest` gains `AllowBack` (mirrors SelectRequest ctor pattern; `params` ctors omit it). One `[`-Back branch in `Dialog.HandleKey` (before type-to-filter, gated `_allowBack && _filterText.Length==0 && (List.MultiSelect \|\| !_hasMoved)`): multi-select fires at any time (Space-toggle doesn't set `_hasMoved`), select/choice only before movement (like Backspace). `Terminal.MultiSelectAsync` passes `allowBack: req.AllowBack`; `OpenModalAsync` already maps Back→`DialogOutcome.Back` (Back returns `default!`/null Value — consumer must null-check). Removed the obsolete `MultiSelectRequestDoesNotHaveAllowBackProperty` test (pinned the reversed contract). Reviewer: approve, 6 dims clean; folded in 2 of 3 nits (arrow-then-`[` regression guard + doc clause). Architectural note: the multi-vs-single suppression divergence is now load-bearing and lives in the `List.MultiSelect` predicate — the spot to revisit if a 4th dialog type appears. |
| 6 | Sample migration onto the new Line factories | `5d8a0aa` | 856 (unchanged — samples only) | `WizardRenderer.cs` 13 single-style `LineBuilder` sites → `Line.Bold/Dim/Fg` (0 `LineBuilder` left); `Program.cs` 22 migrated, 11 multi-segment chains correctly left (`.Bold().Text()`, `.Text().Bold()`, an `.Italic()` site with no factory). 6.3 optional `AppendRule`/`AppendLine` demo skipped (no natural fit — not forced). Equivalence is the §1-proven `Line.X(s) ≡ LineBuilder().X(s).Build()`. Reviewer: approve, all dims clean. |
| 7 | Validation & packaging | `<§7 hash>` | 856 (unchanged) | Orchestrator-direct (no feature code → no worker/reviewer cycle). All four gates green in **Release**: build 0 warnings, 856 tests, format clean, validate `--strict` clean. `Version` bumped `0.2.0-rc.2 → 0.2.0-rc.3` in `Dcli.csproj` + `Dcli.Testing.csproj`. `dotnet pack -c Release --no-build` produced all four artifacts: `dcli.0.2.0-rc.3.{nupkg,snupkg}` (in `src/Dcli/bin/Release/`) and `dcli.testing.0.2.0-rc.3.{nupkg,snupkg}` (in `src/Dcli.Testing/bin/Release/`). |

## Decisions & deviations

Narrative log of anything that wasn't a straight read-off-the-spec-and-implement. One entry per decision, dated/section-scoped.

- **Scope (2026-05-29, pre-flight).** All five candidate items triaged IN by the user: Line single-style factories (Bold/Dim/Fg/**Bg** — not full LineBuilder parity), Scrollback.AppendRule, incremental Collapsible.AppendLine, PasteEvent editor routing, and multi-select Back via `[`. Explicitly OUT: Italic/Underline/Reverse/Strikethrough factories, and `Line.Raw` (rejected — preserves the single-verbatim-seam invariant; see design Decision 2 and [[vt-escape-sanitization-gap]]).
- **§5 reverses a shipped spec sentence.** The live `fixed-region` spec said "MultiSelectRequest SHALL continue to omit AllowBack"; this change reverses it, binding `[` (not Backspace) to Back for multi-select to sidestep the Backspace-at-toggle ambiguity that caused two prior deferrals. This is the one genuinely contended edit — flagged for the reviewer.

## Human-in-the-loop verifications

Anything that can't be settled by automated gates. For each: section reference, exact copy-pasteable command, what the user should see, and current status.

- **§4 — PasteEvent into the input editor — NOT NEEDED (resolved 2026-05-29).** Reviewer confirmed §4 changes no raw-mode/real-terminal behaviour; the intercept-chain routing, caret/wrap, secret-default flip, and the pasted-escape sanitization are all fully covered by headless tests. No real-terminal eyeball required.
- **§5 — `[`-as-Back keybinding — NOT NEEDED (resolved 2026-05-29).** Multi-select Back via `[` is fully covered headlessly (8 tests incl. survives-toggle and survives-arrow-move); reviewer approved with no real-terminal flag. The `[` binding is gated behind opt-in `AllowBack=true` and multi-select dialogs don't type-to-filter, so no key-collision risk. No eyeball required.

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

> **Currently at §7.1 — Validation & packaging.** §1–§6 shipped (856 tests). Final section, mostly
> orchestrator-direct (no feature code): run all four gates against the version bump, bump `Version`
> `0.2.0-rc.2 → 0.2.0-rc.3` in `src/Dcli/Dcli.csproj` AND `src/Dcli.Testing/Dcli.Testing.csproj`,
> `dotnet pack -c Release` → expect `dcli.0.2.0-rc.3.{nupkg,snupkg}` + `dcli.testing.0.2.0-rc.3.{nupkg,snupkg}`,
> then this DEVLOG's per-section hashes are already recorded. After §7 commits, freeze the DEVLOG
> (`/devlog freeze`) and propose `/opsx:archive` — do NOT archive without user confirmation.
