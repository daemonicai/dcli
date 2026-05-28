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
| 2 | Renderer: iterate the preamble | _pending_ | 714 passed / 0 failed | `Dialog.cs` + `InputDialog.cs` internal ctors widened to `IReadOnlyList<Line>?`. `Render` truncates preamble to `Math.Min(count, _maxRows)` then list/buffer gets the remainder. `Terminal.cs` four `§1 bridge:` sites reverted to pass `req.Title` / `req.Prompt` directly; bridge comments deleted. §2.2 `ChoiceDialog.cs` does not exist — `ChoiceRequest` rendered by `Dialog.cs`, covered by 2.1 (tasks.md updated). Test-file edits in `DialogSelectionTests.cs` (5 sites + helper) and `InputDialogTests.cs` (3 sites + helper) are signature-fallout only — no net-new tests (reviewer-confirmed). |

## Decisions & deviations

- **§1 — `Terminal.cs` bridge edit (4 lines).** §1 unblocks the build by collapsing `req.Title` / `req.Prompt` to `req.X is { Count: > 0 } t ? t[0] : null` at the four dialog entry points, because internal `Dialog`/`InputDialog` ctors still take `Line?`. Reviewer cleared this as harmless during the §1→§2 window (no public caller can yet pass multi-line in §1 — consumer changes land later). **§2 must revert these four sites to `title: req.Title` / `prompt: req.Prompt` once `Dialog.cs` / `InputDialog.cs` are widened to `IReadOnlyList<Line>?`.**
- **§1 reviewer flag for §3 (NOT blocking §1).** Spec scenario "Single-string preamble constructor still works" shows `new ChoiceRequest(options, prompt: "Permission:")`. There's no `(IReadOnlyList<Line> Options, string? Prompt, bool AllowBack)` ctor — the only candidate for that exact call shape is `(IReadOnlyList<Line> options, params string[] prompt)` via named-arg expanded-form binding. §3.5's round-trip test will exercise the literal scenario and reveal whether the compiler binds it. If it doesn't bind, §3 must either add a `(IRO<Line>, string?, bool)` overload or weaken the scenario wording.
- **§2 reviewer flag for §3 (NOT blocking §2).** `InputDialog.Render` reports `_lastCaret.Row = r.CaretPosition.Row + promptRows` even when `promptRows > _maxRows` (over-budget preamble). In that path prompt fills all rows in `result`, buffer rows are dropped, and the reported caret is off-screen. Same shape as `Dialog.cs` over-budget truncation, ratified by spec scenario "Multi-line preamble truncates when over budget" ("widget remains usable"). §3.7's truncation regression test should target preamble + widget visibility, not caret-in-frame.
- **§2 tasks.md deviation.** Task 2.2 references `src/Dcli/Internal/FixedRegion/ChoiceDialog.cs`; no such file exists. `ChoiceRequest` is rendered by `Dialog.cs` (see `Terminal.cs:195`). 2.2 folded into 2.1; tasks.md marked accordingly.

## Human-in-the-loop verifications

- **§4.1 — Sample wizard step with multi-line preamble.**
  - Command: `dotnet run --project samples/Dcli.Demo.DmonWizard` and walk to the auth-config step.
  - Expected: the multi-line preamble renders above the input field; submission still works.
  - Status: **pending**.

## Open follow-ups / known gaps (after this change lands — NOT in scope here)

- **Multi-select `AllowBack` still deferred** from `api-ergonomics-pass-1`. Not affected by this change.
- **VT-escape sanitisation of `Segment.Text`** — still tracked in memory `vt-escape-sanitization-gap`.

## Memory files (indexed by the user's per-project memory index)

- `dmon-migration-phase2-paused-on-dcli` (dmon-core project) — Phase 2 of the consumer migration is awaiting this change; resume recipe in that memory.
- `feedback-workaround-as-substrate-signal` (dmon-core project) — the principle that motivated this change.
- `section14-api-ergonomics-findings` (this project) — the original dmon-wizard slice that surfaced pass-1's gaps.

## Resume point

> **Currently at §3 — Tests (first unticked task: `3.1`).** §1 + §2 landed: API surface and renderer are end-to-end multi-line. Next worker call: add ~6–8 tests via `HeadlessTerminal` covering (a) one multi-line preamble test per request type using `FrameSnapshot` assertions, (b) backwards-compat round-trip tests for single-`Line` / single-`string` ctors, (c) null/empty preamble paints zero rows, (d) over-budget truncation regression (widget remains visible — do NOT assert caret-in-frame; see §2 reviewer flag above). Heads-up for §3.5: verify `new ChoiceRequest(options, prompt: "Permission:")` actually compiles given the §1 ctor matrix (§1 reviewer flag above). After §3: §4 sample updates, §5 packages.
