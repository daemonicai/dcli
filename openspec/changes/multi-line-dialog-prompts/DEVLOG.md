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

## Decisions & deviations

- **§1 — `Terminal.cs` bridge edit (4 lines).** §1 unblocks the build by collapsing `req.Title` / `req.Prompt` to `req.X is { Count: > 0 } t ? t[0] : null` at the four dialog entry points, because internal `Dialog`/`InputDialog` ctors still take `Line?`. Reviewer cleared this as harmless during the §1→§2 window (no public caller can yet pass multi-line in §1 — consumer changes land later). **§2 must revert these four sites to `title: req.Title` / `prompt: req.Prompt` once `Dialog.cs` / `InputDialog.cs` are widened to `IReadOnlyList<Line>?`.**
- **§1 reviewer flag for §3 (NOT blocking §1).** Spec scenario "Single-string preamble constructor still works" shows `new ChoiceRequest(options, prompt: "Permission:")`. There's no `(IReadOnlyList<Line> Options, string? Prompt, bool AllowBack)` ctor — the only candidate for that exact call shape is `(IReadOnlyList<Line> options, params string[] prompt)` via named-arg expanded-form binding. §3.5's round-trip test will exercise the literal scenario and reveal whether the compiler binds it. If it doesn't bind, §3 must either add a `(IRO<Line>, string?, bool)` overload or weaken the scenario wording.

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

> **Currently at §2 — Renderer: iterate the preamble (first unticked task: `2.1`).** §1 landed: `DialogRequests.cs` widened to `IReadOnlyList<Line>?` across all four request types with the full convenience-ctor matrix (Decision 3) + `Terminal.cs` bridge stopgap. Next worker call: widen the internal `Dialog`/`ChoiceDialog`/`InputDialog` ctor signature to take `IReadOnlyList<Line>?` for the preamble, replace the one-shot preamble paint with `foreach (Line line in preamble ?? []) PaintLine(line);`, and revert the four `Terminal.cs` bridge sites back to `title: req.Title` / `prompt: req.Prompt`. After §2: §3 adds tests via `HeadlessTerminal`, §4 updates the demo sample, §5 packages.
