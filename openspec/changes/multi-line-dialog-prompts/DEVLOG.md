# DEVLOG — `multi-line-dialog-prompts`

> **Status: in-flight.** Maintained while applying the change per the OpenSpec apply
> workflow (see `CLAUDE.md`). Captures per-section narrative the spec files don't carry:
> decisions under uncertainty, deviations, surfaced bugs, HITL verifications. On archive
> this file moves with the change to
> `openspec/changes/archive/YYYY-MM-DD-multi-line-dialog-prompts/DEVLOG.md` and the status
> flips to **shipped** (see `/devlog freeze`).

## How to resume

- Branch: **`change/multi-line-dialog-prompts`** (create from `main` if not yet).
- Working tree state: **CLEAN** (no implementation work has started — proposal only).
- Sanity check command:
  `dotnet build -c Release && dotnet test -c Release && dotnet format --verify-no-changes && openspec validate multi-line-dialog-prompts --strict`
- Resume point: **§1 — Widen the request records** (first unticked: `1.1`).
- This change is consumer-driven by the dmon-migration. dmon Phase 2 is currently paused waiting for this change to ship — see `[[dmon-migration-phase2-paused-on-dcli]]` in the dmon-core project memory.

## Section status

One row per `## N.` section in `tasks.md`. Add a row when the section commits.

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Widen the request records | `8b97189` | 714 passed / 0 failed (baseline) | Primary record ctor preamble field widened to `IReadOnlyList<Line>?` on all four request types. Convenience ctors per Decision 3 added: single-`Line` (PascalCase `Title`/`Prompt`/`AllowBack` so named-arg call sites bind to the secondary), `params Line[]`, `IReadOnlyList<string>?`, single-`string`, `params string[]`. `InputRequest` `params` overloads drop `Default`/`IsSecret` (`params` must be last — documented in XML). 1.7 explicit-only contract noted on every new multi-line ctor. |
| 2 | Renderer: iterate the preamble | `159e18c` | 714 passed / 0 failed | `Dialog.cs` + `InputDialog.cs` internal ctors widened to `IReadOnlyList<Line>?`. `Render` truncates preamble to `Math.Min(count, _maxRows)` then list/buffer gets the remainder. `Terminal.cs` four `§1 bridge:` sites reverted to pass `req.Title` / `req.Prompt` directly; bridge comments deleted. §2.2 `ChoiceDialog.cs` does not exist — `ChoiceRequest` rendered by `Dialog.cs`, covered by 2.1 (tasks.md updated). Test-file edits in `DialogSelectionTests.cs` (5 sites + helper) and `InputDialogTests.cs` (3 sites + helper) are signature-fallout only — no net-new tests (reviewer-confirmed). |
| 3 | Tests | `106b20a` | 735 passed / 0 failed (+21 new) | 21 new tests across 5 files (one new file). Per request type: one multi-line preamble test via `HeadlessTerminal`/`FrameSnapshot` asserting ordered rows above the widget. New `DialogRequestsTests.cs` carries 15 round-trip + null-preamble property tests. Two §3.7 truncation tests (Select + Input) assert overlay-still-active + row-count-bounded WITHOUT caret-in-frame coupling (§2 reviewer flag respected). §1 reviewer flag resolved via **Option A**: added `(IReadOnlyList<Line>, string?, bool)` ctor overloads on `SelectRequest`/`MultiSelectRequest`/`ChoiceRequest` so the spec's literal `new ChoiceRequest(options, prompt: "Permission:")` shape compiles; `InputRequest` already had the equivalent. 3.2 placed in `DialogSelectionTests.cs` (no dedicated MultiSelect file); 3.5 placed in new `DialogRequestsTests.cs`. |
| 4 | Sample updates | `300cc0a` | 735 passed / 0 failed | 4.1: `WizardRenderer.RenderTextInputAsync` gates a 3-line preamble on `step.Secret == true`: `Bold(step.Prompt)` + `Dim("Used only for this session. Not persisted to disk.")` + `Dim("Press Esc to cancel; Enter to confirm.")`. Non-secret inputs unchanged. 4.2: `Program.cs` Phase 5d "Run the tour again?" choice gets a 2-line prompt (bold + dim navigation hint); existing auto-cancel `cts` timeout still drives the demo unattended. **4.1 HITL verification pending** — sample uses `dotnet run --project samples/Dcli.Demo.DmonWizard`; user must walk to the API-key step and confirm three preamble rows render above the input caret (bold prompt + two dim lines). |

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
- **VT-escape sanitisation of `Segment.Text`** — still tracked in memory `vt-escape-sanitization-gap`.

## Memory files (indexed by the user's per-project memory index)

- `dmon-migration-phase2-paused-on-dcli` (dmon-core project) — Phase 2 of the consumer migration is awaiting this change; resume recipe in that memory.
- `feedback-workaround-as-substrate-signal` (dmon-core project) — the principle that motivated this change.
- `section14-api-ergonomics-findings` (this project) — the original dmon-wizard slice that surfaced pass-1's gaps.

## Resume point

> **Currently at §5 — Validation & packaging (first unticked task: `5.1`).** §1–§4 landed; §4.1 HITL-verified by the user. Final section: run the four gates clean, bump `Dcli.csproj` and `Dcli.Testing.csproj` `Version` from `0.2.0-rc.1` → `0.2.0-rc.2`, `dotnet pack` to produce the four nupkg/snupkg files, freeze the DEVLOG (`/devlog freeze`), and notify the user about the dmon Phase 2 resume recipe (5.8).
