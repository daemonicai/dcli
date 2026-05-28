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

## Decisions & deviations

(No decisions or deviations have been captured yet — this change is fresh out of `/opsx:propose`.)

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

> **Currently at §1 — Widen the request records (first unticked task: `1.1`).** This change was proposed by the dmon orchestrator (via the user) after dmon-migration Phase 2 hit the `ChoiceRequest.Prompt` single-line constraint while porting `ToolConfirmPrompt`. The dmon side has the Phase 2 work uncommitted and is blocked on this change shipping. Next worker call: widen all four request records (`SelectRequest.Title`, `MultiSelectRequest.Title`, `ChoiceRequest.Prompt`, `InputRequest.Prompt`) from `Line?` to `IReadOnlyList<Line>?`, add backwards-compat overload constructors per Decision 3 in `design.md`. After §1 lands, §2 updates the renderer (one `foreach` per dialog), §3 adds tests via `HeadlessTerminal`, §4 updates the demo sample, §5 packages.
