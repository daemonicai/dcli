# DEVLOG — `multi-line-dialog-prompts`

> **Status: shipped.** Final commit `56de80d`. Archived 2026-05-28 to
> `openspec/changes/archive/2026-05-28-multi-line-dialog-prompts/DEVLOG.md`. Released as
> `dcli` + `dcli.testing` `0.2.0-rc.2`. dmon-migration Phase 2 resumed on the same day
> against rc.2 (user-confirmed). PR link backfilled below once merged.

## How to resume

> _Historical — frozen at archive._

- Branch at archive: **`change/multi-line-dialog-prompts`** (13 commits ahead of `main`).
- Final working-tree state: **CLEAN** at `56de80d`.
- Final sanity-check command (passed at archive): `dotnet build -c Release && dotnet test -c Release && dotnet format --verify-no-changes && openspec validate multi-line-dialog-prompts --strict` (735/735 green).
- The change was consumer-driven by `dmon-migration`. dmon Phase 2 confirmed resumed against `0.2.0-rc.2` on 2026-05-28 — the scrollback workaround in `ToolConfirmPrompt` was replaced with multi-line `ChoiceRequest.Prompt`.

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Widen the request records | `8b97189` | 714 passed / 0 failed (baseline) | Primary record ctor preamble field widened to `IReadOnlyList<Line>?` on all four request types. Convenience ctors per Decision 3 added: single-`Line` (PascalCase `Title`/`Prompt`/`AllowBack` so named-arg call sites bind to the secondary), `params Line[]`, `IReadOnlyList<string>?`, single-`string`, `params string[]`. `InputRequest` `params` overloads drop `Default`/`IsSecret` (`params` must be last — documented in XML). 1.7 explicit-only contract noted on every new multi-line ctor. |
| 2 | Renderer: iterate the preamble | `159e18c` | 714 passed / 0 failed | `Dialog.cs` + `InputDialog.cs` internal ctors widened to `IReadOnlyList<Line>?`. `Render` truncates preamble to `Math.Min(count, _maxRows)` then list/buffer gets the remainder. `Terminal.cs` four `§1 bridge:` sites reverted to pass `req.Title` / `req.Prompt` directly; bridge comments deleted. §2.2 `ChoiceDialog.cs` does not exist — `ChoiceRequest` rendered by `Dialog.cs`, covered by 2.1 (tasks.md updated). Test-file edits in `DialogSelectionTests.cs` (5 sites + helper) and `InputDialogTests.cs` (3 sites + helper) are signature-fallout only — no net-new tests (reviewer-confirmed). |
| 3 | Tests | `106b20a` | 735 passed / 0 failed (+21 new) | 21 new tests across 5 files (one new file). Per request type: one multi-line preamble test via `HeadlessTerminal`/`FrameSnapshot` asserting ordered rows above the widget. New `DialogRequestsTests.cs` carries 15 round-trip + null-preamble property tests. Two §3.7 truncation tests (Select + Input) assert overlay-still-active + row-count-bounded WITHOUT caret-in-frame coupling (§2 reviewer flag respected). §1 reviewer flag resolved via **Option A**: added `(IReadOnlyList<Line>, string?, bool)` ctor overloads on `SelectRequest`/`MultiSelectRequest`/`ChoiceRequest` so the spec's literal `new ChoiceRequest(options, prompt: "Permission:")` shape compiles; `InputRequest` already had the equivalent. 3.2 placed in `DialogSelectionTests.cs` (no dedicated MultiSelect file); 3.5 placed in new `DialogRequestsTests.cs`. |
| 4 | Sample updates | `300cc0a` (+`4132a3d` env-var, `e635343` tick) | 735 passed / 0 failed | 4.1: `WizardRenderer.RenderTextInputAsync` gates a 3-line preamble on `step.Secret == true`: `Bold(step.Prompt)` + `Dim("Used only for this session. Not persisted to disk.")` + `Dim("Press Esc to cancel; Enter to confirm.")`. Non-secret inputs unchanged. 4.2: `Program.cs` Phase 5d "Run the tour again?" choice gets a 2-line prompt (bold + dim navigation hint). Follow-up env-var `DCLI_DEMO_DMONWIZARD_INTERACTIVE=1` added to disable the DmonWizard sample's pre-existing 10s auto-cancel so the HITL verification was actually possible. **4.1 HITL verified by user 2026-05-28** — three preamble rows confirmed above the input caret; Enter advances the wizard. |
| 5 | Validation & packaging | `34f84ac` | 735 passed / 0 failed | All four gates green (build, test, format, validate) against the rc.2 version bump. `Dcli.csproj` + `Dcli.Testing.csproj` `Version` bumped 0.2.0-rc.1 → 0.2.0-rc.2. `dotnet pack -c Release --no-build` produced `dcli.0.2.0-rc.2.{nupkg,snupkg}` in `src/Dcli/bin/Release/` and `dcli.testing.0.2.0-rc.2.{nupkg,snupkg}` in `src/Dcli.Testing/bin/Release/`. 5.8 (dmon Phase 2 resume recipe) handed off to user — see Open follow-ups. |

## Decisions & deviations

- **§1 — `Terminal.cs` bridge edit (4 lines).** §1 unblocks the build by collapsing `req.Title` / `req.Prompt` to `req.X is { Count: > 0 } t ? t[0] : null` at the four dialog entry points, because internal `Dialog`/`InputDialog` ctors still take `Line?`. Reviewer cleared this as harmless during the §1→§2 window (no public caller can yet pass multi-line in §1 — consumer changes land later). **§2 must revert these four sites to `title: req.Title` / `prompt: req.Prompt` once `Dialog.cs` / `InputDialog.cs` are widened to `IReadOnlyList<Line>?`.**
- **§1 reviewer flag for §3 (NOT blocking §1).** Spec scenario "Single-string preamble constructor still works" shows `new ChoiceRequest(options, prompt: "Permission:")`. There's no `(IReadOnlyList<Line> Options, string? Prompt, bool AllowBack)` ctor — the only candidate for that exact call shape is `(IReadOnlyList<Line> options, params string[] prompt)` via named-arg expanded-form binding. §3.5's round-trip test will exercise the literal scenario and reveal whether the compiler binds it. If it doesn't bind, §3 must either add a `(IRO<Line>, string?, bool)` overload or weaken the scenario wording.
- **§2 reviewer flag for §3 (NOT blocking §2).** `InputDialog.Render` reports `_lastCaret.Row = r.CaretPosition.Row + promptRows` even when `promptRows > _maxRows` (over-budget preamble). In that path prompt fills all rows in `result`, buffer rows are dropped, and the reported caret is off-screen. Same shape as `Dialog.cs` over-budget truncation, ratified by spec scenario "Multi-line preamble truncates when over budget" ("widget remains usable"). §3.7's truncation regression test should target preamble + widget visibility, not caret-in-frame.
- **§2 tasks.md deviation.** Task 2.2 references `src/Dcli/Internal/FixedRegion/ChoiceDialog.cs`; no such file exists. `ChoiceRequest` is rendered by `Dialog.cs` (see `Terminal.cs:195`). 2.2 folded into 2.1; tasks.md marked accordingly.
- **§4 follow-up — `DCLI_DEMO_DMONWIZARD_INTERACTIVE` env var.** The DmonWizard sample had a pre-existing 10-second `CancellationTokenSource` auto-cancel that prevented interactive HITL verification of §4.1 (the wizard self-cancelled before any keyboard input was possible). Added a small env-var toggle: when `DCLI_DEMO_DMONWIZARD_INTERACTIVE=1` the cancellation source has no timeout; otherwise the 10s CI default is preserved. Sample-only change; no production-code impact.

## Human-in-the-loop verifications

- **§4.1 — Sample wizard step with multi-line preamble.**
  - Command (interactive mode): `DCLI_DEMO_DMONWIZARD_INTERACTIVE=1 dotnet run --project samples/Dcli.Demo.DmonWizard`. The env var disables the 10-second CI safety timeout so the wizard waits for keyboard input.
  - Walk the wizard: pick a provider (Anthropic or OpenAI), then a model. Step 3 is the API key entry.
  - Expected at the API-key step (three rows above the input caret):
    - **Line 1 (bold):** "Anthropic API key" or "OpenAI API key" (matches `step.Prompt`)
    - **Line 2 (dim):** "Used only for this session. Not persisted to disk."
    - **Line 3 (dim):** "Press Esc to cancel; Enter to confirm."
  - Type something + Enter and the wizard advances to the next step (validates submission still works through the widened renderer).
  - Sub-issue noted during first verification (2026-05-28): the demo's pre-existing 10s `CancellationTokenSource` timeout self-cancelled the wizard before keyboard input was possible. Pre-existing behaviour (not a §4 regression), but it blocked the interactive verification. Resolved by adding `DCLI_DEMO_DMONWIZARD_INTERACTIVE=1` env-var toggle; default CI behaviour (10s auto-cancel) preserved.
  - Status: **verified by user 2026-05-28** — multi-line preamble renders correctly and Enter advances the wizard through the widened renderer.

## Open follow-ups / known gaps (after this change lands — NOT in scope here)

- **Multi-select `AllowBack` still deferred** from `api-ergonomics-pass-1`. Not affected by this change.
- **VT-escape sanitisation of `Segment.Text`** — still tracked in memory `vt-escape-sanitization-gap`. The preamble widening multiplies the smuggle surface from 1 line to N — still consumer-supplied content, so the gap is unchanged in nature but larger in surface area.
- **dmon Phase 2 resume recipe (task 5.8).** With dcli `0.2.0-rc.2` packed, dmon can now drop the `ToolConfirmPrompt` scrollback workaround. Steps for the dmon side:
  1. Bump the `<PackageReference>` / `<ProjectReference>` on dcli to `0.2.0-rc.2`.
  2. In `ToolConfirmPrompt`, restore the multi-line preamble lines into `ChoiceRequest.Prompt` instead of appending them to `Scrollback` before opening `ChoiceAsync`.
  3. Update `ToolConfirmPromptTests` to assert the preamble renders inside the dialog overlay (not in scrollback rows).
  4. Run dmon gates and commit Phase 2.
  Confirm completion back to dcli so task 5.8 can be ticked.
- **`InputDialog` over-budget caret reporting** (§2 reviewer flag). When `promptRows > _maxRows` the reported caret is off-screen. Spec scenario "Multi-line preamble truncates when over budget" ratifies the truncation behaviour, but the caret-reporting shape could be tightened in a future change if a consumer ever needs it.

## Memory files (indexed by the user's per-project memory index)

- `dmon-migration-phase2-paused-on-dcli` (dmon-core project) — Phase 2 of the consumer migration is awaiting this change; resume recipe in that memory.
- `feedback-workaround-as-substrate-signal` (dmon-core project) — the principle that motivated this change.
- `section14-api-ergonomics-findings` (this project) — the original dmon-wizard slice that surfaced pass-1's gaps.

## Resume point

> **Shipped 2026-05-28.** All 29 tasks ticked. Final commits: §1 `8b97189`, §2 `159e18c`, §3 `106b20a`, §4 `300cc0a` (+ env-var `4132a3d`, 4.1 tick `e635343`), §5 `34f84ac` (5.8 tick `56de80d`). Released as `dcli` + `dcli.testing` `0.2.0-rc.2`. dmon-migration Phase 2 resumed and committed against rc.2 the same day. Any follow-ups (e.g. `InputDialog` over-budget caret reporting, VT-escape sanitisation surface, multi-select `AllowBack`) live in separate future OpenSpec changes — see Open follow-ups above.
