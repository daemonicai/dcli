# DEVLOG — applying the `core-rendering-architecture` change

This log lets a fresh session resume the OpenSpec change **`core-rendering-architecture`** without losing history.
It is **not** part of the change's deliverables (it's a working note); don't tick tasks for it.

## What this is

We are applying the single active OpenSpec change `openspec/changes/core-rendering-architecture/` by following the
**authoritative OpenSpec Apply Workflow in `CLAUDE.md`**. That workflow is, per section:

1. Orchestrator (main thread) **never writes feature code** — it briefs the `worker` agent, audits with the `reviewer`
   agent, runs gates, ticks boxes, commits.
2. Unit of work = one `## N.` section of `tasks.md`. Complex sections (parser, render loop, painter, scrollback,
   fixed region) are split into **Chunk A / Chunk B** across multiple `worker` calls but land as **one commit per section**.
3. Loop: brief `worker` → `reviewer` audits the diff → fix loop until sign-off → **four gates** → tick boxes → commit.
4. Four gates (all must pass before ticking): `dotnet build -c Release` (0 warnings, warnings-as-errors),
   `dotnet test -c Release` (all green), `dotnet format --verify-no-changes`, `openspec validate core-rendering-architecture --strict`.
5. Stop-and-ask on ambiguity, out-of-scope needs, unresolved Open Questions, or human-in-the-loop (real-terminal) verification.

> Note: `SendMessage` is **not available** in this environment, so each worker/reviewer round in a fix loop spawns a
> **fresh** agent that re-reads the on-disk (uncommitted) state. Brief them with file paths + the prior findings.

## How to resume

- Branch: **`change/core-rendering-architecture`** (created from `main`). Stay on it.
- Working tree is **DIRTY mid-§12** (expected): §12 **Chunk A** (selection dialogs) is implemented and **reviewer-approved but uncommitted** —
  §12 commits as **one section commit** only when all of 12.1–12.7 are done. Do **not** revert it; resume by continuing §12 **Chunk B**.
- Sanity check (dirty tree): `dotnet build -c Release && dotnet test -c Release && dotnet format --verify-no-changes && openspec validate core-rendering-architecture --strict`
  → expect **0 warnings, 572 tests green, clean format, valid**.
- Resume point = §12 **Chunk B (InputAsync)** — tasks 12.1 (input dialog), then Chunk C (façade+events) and Chunk D (ITerminal+fakeable tests). See the §12 section below.
- Check the memory files (see below) before briefing — several encode hard-won constraints for upcoming sections.

## Section status (1 commit per section)

| § | Section | Commit | Tests after | Notes |
|---|---------|--------|-------------|-------|
| 1 | Project setup | `c39f388` | 1 | net10.0, nullable, analyzers warnings-as-errors, `.slnx`, GitHub Actions CI (linux/mac/win), NuGet meta (`PackageId=dcli`, MPL-2.0). |
| 2 | Styled text | `a451373` | 36 | Public `Format`/`Color`/`Style`/`Segment`/`Line`/`LineBuilder`. Model only — **no ANSI emission** (deferred to §8). `Line` has structural equality. |
| 3 | Display width & wrapping | `2969d91` | 83 | Internal `DisplayWidth` (in-house wcwidth table) + `LineWrapper` (hard cell-wrap, wide chars never split). |
| 4 | Raw mode & capability detection | `9dcb9ea` | 109 | `IRawModeSession` (§15 seam) + POSIX/Windows impls (per-platform termios — macOS≠Linux), `TerminalNotSupportedException` (public), `RestoreCoordinator` (signals+ProcessExit, CAS-idempotent). **4.5 manually verified by the user on macOS** (raw-mode entry + restore on normal/exception/signal). |
| 5 | VT input parser | `d17e77e` | 218 | Pure `VtInputParser` (Chunk A: state machine + event model; Chunk B: bracketed paste + fixtures). Public event model. Decodes **both SS3 (`ESC O A`) and CSI (`ESC [ A`) arrows**. |
| 6 | Input reader thread | `14bec73` | 229 | `InputReader` dedicated thread, `IInputByteSource` (real POSIX `read(2)`; Windows stub for §14.1; fake for tests). Ctrl+C → `KeyEvent`, no termination. |
| 7 | Render loop core | `e03918c` | 257 | Actor-model `LoopEngine` (Chunk A: cadence/edges; Chunk B: `Terminal` lifecycle). **Namespace reorg** (see Decisions). Loop faults are surfaced (not swallowed). True-idle wait (no busy-poll). |
| 8 | Frame painting | `ff16233` | 310 | `VtFrameRenderer` (Chunk A: SGR + single-frame; Chunk B: cross-frame cursor accounting). Sync-output fence, never rewrites frozen content. Wired into production `Terminal` (UTF-8/no-BOM/flush-per-frame stdout). Diff reconciler **deferred** (seam only). |
| 9 | Scrollback model | `652f851` | 350 | `ILineObject`/`TextBlock`/`LiveBlock`/`Collapsible`, commit horizon, overflow-commits-top, one-way collapsible, oversized reprint. Feeds `LiveWindowRows`/`NewlyCommittedRows`. |
| 10 | Fixed region: input editor & status | `84cbb48` | 442 | `TextBuffer` (grapheme-cluster nav via `StringInfo`, caret↔visual wrap, history recall), `StatusLine`, `MaxHeight` budget, loop frame-composition. Editor consumes editing keys; Enter/Tab/Ctrl fall through. |
| 11 | Fixed region: overlays (autocomplete & dialog) | `bf35c99` | 554 | `ScrollableList` (reverse-video highlight, never the hardware cursor; viewport reconciled **on render** so it survives `MaxRows` changes between frames), `IOverlay` seam, `Autocomplete`/`Dialog` overlays, `OverlayState` invariant on the model (`ShowAutocomplete`/`ShowDialog`/`ClearOverlay`), intercept-chain routing (overlay→editor→emit), composer overlay-slot (dialog above / autocomplete below) + Decision-8 budget squeeze (status sacred → caret visible → overlay absorbs squeeze, **zero-cap guarded**) + cursor hide-while-modal. Built across 3 worker calls (A=ScrollableList, B-i=components, B-ii=integration). |
| 12 | Public API: dialogs, events, façade | — | — | `ITerminal` + sub-surfaces, awaitable dialogs (`SelectAsync`/`MultiSelectAsync`/`InputAsync`/`ChoiceAsync` → `DialogResult<T>`), `Events` emission (`InputSubmitted`/`InputChanged`/`KeyPressed`/`Resized`), public `Scrollback`/`Input`/`Status`/`Autocomplete` façade + `ILiveBlock`/`ICollapsible` handles. Much of §9/§10/§11 was built as **internal model + commands** specifically so §12 wraps them. |
| 13 | Resize & reflow | — | — | **Open Question #2** — will require a user confirmation like §8 did (SIGWINCH delivery + reflow/repaint; truecolor/sync-output/unicode-width detection + fallbacks). |
| 14 | Cross-platform validation & packaging | — | — | **Human-in-the-loop:** 14.1 manual smoke on WT/macOS/Linux; 14.2 demo app; 14.3 finalize NuGet/README/XML docs + pre-release; 14.4 port a dmon `Dmon.Terminal` slice. |
| 15 | Headless test harness (`Dcli.Testing`) | — | — | Generalize the in-memory edges (raw-mode no-op, scripted input, in-memory sink, virtual clock) into the public `Dcli.Testing` package: `HeadlessTerminal`, `SettleAsync`, frame `Snapshot`; retarget §5.6/§7.6/§8.5 tests onto it. The substrate already exists internally from §4/§6/§7/§8. |

## Codebase layout (post §7 namespace reorg)

- **Public API → root namespace `Dcli`** (files in `src/Dcli/`): `Terminal`, `TerminalOptions`, `TerminalNotSupportedException`,
  `Segment`/`Line`/`Style`/`Color`/`Format`/`LineBuilder`, `InputEvent`/`KeyEvent`/`PasteEvent`/`ResizeEvent`/`KeyCode`/`NamedKey`/`Modifiers`, `TerminalEvent`.
  The flagship type is usable as `Terminal` with `using Dcli;`.
- **Internal machinery → `Dcli.Internal.*`** (files in `src/Dcli/Internal/...`): `Dcli.Internal` (RawModeSession, RestoreCoordinator,
  capability detection, RecordingRawModeSession), `.Posix`, `.Windows`, `.Input` (VtInputParser, InputReader, IInputByteSource),
  `.RenderLoop` (LoopEngine, edges `IClock`/`IOutputSink`/`ITerminalSizeSource`, RenderModel, VtFrameRenderer, SgrTranslator, NoopOutputSink),
  `.Scrollback` (ILineObject/TextBlock/LiveBlock/Collapsible/ScrollbackModel + `Commands/`), `.FixedRegion` (TextBuffer/StatusLine/FixedRegionComposer).
  `InternalsVisibleTo` → `Dcli.Tests` and `Dcli.RawModeHarness`.
- **Tests:** `tests/Dcli.Tests/` (xUnit). **Manual harness:** `samples/Dcli.RawModeHarness/` (non-packable; the §4.5 verifier).
- **Solution:** `dcli.slnx`. Shared build props in `Directory.Build.props`; analysis rules in `.editorconfig` (no project-wide
  suppressions — `CA1031` is a localized `#pragma` around the one `LoopEngine.RunLoop` catch).

## Key decisions & deviations made while applying

- **§7 namespace reorg (user-approved):** the public `Terminal` type collided with the `Dcli.Terminal` namespace (CA1724). Rather than
  suppress, the user chose to move all public types to root `Dcli` and rename internal machinery `Dcli.Terminal.*` → `Dcli.Internal.*`.
- **§8 paint mechanism (Open Question #1, user-confirmed):** v1 = synchronized-output fence (`ESC[?2026h…l`) + full clear/repaint of the
  bounded region. Per-line diff reconciler **deferred** (8.4 leaves a seam only).
- **§7 idle wait:** rejected a `Thread.Sleep` busy-poll (reviewer-blocking); uses `WaitToReadAsync` + `IClock.WaitUntilAsync` deadline so the
  loop truly idles and stays deterministic under a virtual clock. Loop stays `await`-free on its dedicated thread (single-mutator discipline).
- **§7 loop faults are surfaced**, not silently swallowed (faults `LoopTerminated`, `DisposeAsync` rethrows, pending settles faulted).
- **Render-frame cursor convention (§8):** the frame's final row has **no trailing `\n`**; cross-frame move-up uses the tracked **caret
  resting row** (not region height) so it never overshoots above the region into frozen content.
- **§11 autocomplete apply-semantics (decided, flagged for §12):** accepting a candidate does a **whole-buffer replace** via `TextBuffer.SetText`
  (caret to end) — the literal reading of spec "the candidate's insert text is applied to the input buffer" and what slash-command-style
  completion needs. `AutocompleteCandidate = (InsertText, Display Line)`. §12 may refine to span-replace; documented in XML docs at the call site.
- **§11 overlay budget (Decision 8 made concrete):** composer renders the input with `allot = budget (= cap − statusCount)` → `inputRows = min(natural, budget)`;
  the overlay gets `overlayCap = clamp(budget − inputRows.Count, 0, 10)` (the `10` default becomes consumer-configurable in §12). **Zero-cap guard:**
  when `overlayCap == 0` the overlay is **not rendered at all** — `ScrollableList.MaxRows` clamps to ≥1, so rendering it would leak one phantom row past the cap.
- **§11 ↔ §12 seam:** `Dialog.CloseRequest` (`Submit`/`Cancel`) + the loop's `ClearOverlay()` on `IsDismissed` is where §12's TCS-bundled open-dialog
  command will complete its `DialogResult` (the dialog object survives the clear so its selection/outcome are still readable). `RenderModel.ShowAutocomplete/ShowDialog`
  are the §12 façade-command entry points (exercised today only by §11.5 tests).

## Human-in-the-loop verifications

- **Done — §4.5:** user ran `samples/Dcli.RawModeHarness` on macOS and confirmed all 4 checks (raw-mode entry/echo-off + restore on
  normal exit, on exception, on `kill -TERM`). The first run surfaced a real bug (managed `ReadByte` treats the `VMIN=0/VTIME=1` timeout
  0-read as EOF) which was fixed before confirmation.
- **Pending:** §13 (Open Question #2 — expect a confirmation like §8). §14.1 (manual cross-platform smoke), §14.2 (demo app), §14.3
  (NuGet pre-release publish — outward-facing, will confirm before publishing). Windows runtime paths are stubbed and deferred to §14.1.

## Open follow-ups / known gaps (after this change lands — NOT in scope here)

These are recorded as memory files and should become their own future OpenSpec changes / passes:

- **VT-escape sanitization gap:** `Segment.Text` is emitted to the terminal **verbatim**; a consumer can smuggle raw VT (even an unbalanced
  `ESC[?2026h` defeating the sync fence). Currently correct per the styled-text "verbatim" contract; needs its own change (sanitize-on-emit).
- **Scrollback oversized-reprint ordering:** a rare cross-object commit-order edge when an oversized-collapsible expand coincides with an
  independent overflow above it in the same paint. Emitted-once is fine; spec doesn't pin cross-object order. Deferred refinement.
- **CA2007 / render-loop thread discipline:** CA2007 is suppressed repo-wide, so §7+ continuation-thread correctness is verified by hand.

## Memory files (in the session memory dir, indexed by `MEMORY.md`)

- `ca2007-render-loop-thread-discipline` — verify loop thread-correctness by hand (CA2007 suppressed).
- `section6-input-reader-read-semantics` — `read(2)` 0≠EOF under VMIN=0/VTIME=1; timed read IS the ESC-disambiguation; `Flush` only on bare-ESC.
- `section5-vt-parser-ss3-arrows` — decode both SS3 and CSI arrows (real-terminal evidence).
- `vt-escape-sanitization-gap` — see above.
- `scrollback-oversized-reprint-ordering` — see above.

## Resume point

**Section 12 — public API: dialogs, events, façade.** This section **wraps** the internal model+commands built in §9/§10/§11; little new
mechanics, mostly surface. Per design Decisions 11 & 12:
- **12.1** `DialogResult<T>` / `DialogOutcome (Submitted|Back|Cancelled)` + awaitable `SelectAsync`/`MultiSelectAsync`/`InputAsync`/`ChoiceAsync`
  via **TCS-bundled open-dialog commands** — the loop drives the modal `Dialog` overlay (§11) and completes the TCS on close
  (hook point: the loop's `ClearOverlay()` on `Dialog.IsDismissed`; map `CloseRequest` Submit/Cancel → outcome; Back is a §12 concept).
- **12.2** cancellation via `CancellationToken` (closes overlay → `Cancelled`); reject/queue a second concurrent dialog (the §11 invariant only
  allows one overlay — decide reject-vs-queue here).
- **12.3** `Scrollback`/`Input`/`Status`/`Autocomplete` façade surfaces posting fire-and-forget commands over the inbound channel
  (`Autocomplete.Show` → `RenderModel.ShowAutocomplete`; `.Hide` → clear). 
- **12.4** `Events` emission: `InputSubmitted`/`InputChanged`/`KeyPressed`/`Resized` (note `KeyPressed`/`Resized` already emitted by the loop; add
  Submitted on Enter fall-through and Changed on buffer mutation).
- **12.5** tests: select submit/cancel, multi-select toggle, cancellation token, input-change→candidates round-trip.
- **12.6** expose `ITerminal` (+ `IScrollback`/`IInput`/`IStatus`/`IAutocomplete`); make `KeyEvent`/`PasteEvent`/`ResizeEvent`/`TerminalEvent.*`/
  `DialogResult<T>`/`DialogOutcome` **public & constructible** (watch for internal-only ctors); no static/singleton state on consumer paths (tier A).
- **12.7** tests: a hand-written fake `ITerminal` substitutes for the real façade; synthesized events/results drive consumer-style code;
  command-side calls recorded & asserted.

### §12 chunk progress & plan

- **Chunk A — DONE (reviewer-approved, uncommitted).** Public `DialogOutcome {Submitted,Back,Cancelled}`, `DialogResult<T>(Outcome,Value)` (constructible);
  `SelectRequest`/`MultiSelectRequest`/`ChoiceRequest` (minimal: items/options + optional title/prompt `Line`); `Terminal.SelectAsync`/`MultiSelectAsync`/`ChoiceAsync`.
  Plumbing: `OpenDialogCommand` + `CancelDialogCommand` (`src/Dcli/Internal/RenderLoop/`); `RenderModel.PendingDialogCompletion` (`Action<Dialog>`, set with
  `ShowDialog`, invoked **before** `ClearOverlay` in the loop dismiss hook, cleared on clear); TCS bundled with `RunContinuationsAsynchronously`.
  Decisions: **reject only a concurrent `Dialog`** (an active Autocomplete is *suppressed* via `ShowDialog`), reject = fault the task with `InvalidOperationException`;
  cancel-token → `Cancelled` (registration disposed on every terminal path; already-cancelled fast path; `CancelDialogCommand` guards on `ReferenceEquals(ActiveOverlay, expected)`);
  `Dialog` got an optional **title row** (`Render` truncates `[title]++list` to `MaxRows`, so title-only at `MaxRows==1`); empty-list Submit → `Submitted(-1)`.
  Carry-forward nits: `AlreadyCancelledTokenReturnsCancelledNoOverlay` re-implements the fast path inline (tighten once Chunk D wires the real façade);
  test cancel-closures use `-1`/`[]` vs production `default!` — cosmetic.
- **Chunk B — NEXT: `InputAsync`** (the 4th dialog; needs *text entry*, not a `ScrollableList`). Plan (worked out, not yet implemented):
  add public `InputRequest(Line? Prompt = null, string? Default = null, bool IsSecret = false)` and `Terminal.InputAsync → DialogResult<string>`.
  Build `InputDialog : IOverlay` (modal, AboveInput) hosting its **own** `TextBuffer` (seeded with `Default`; renders `IsSecret` masked, e.g. `•`); Enter→Submit(text)/Esc→Cancel.
  **Seam work this requires:** (1) the hardware cursor must sit at the InputDialog's *own* caret (it's the focus) — extend `IOverlay` with a nullable
  `CaretInOverlay (Row,Col)` (null for Autocomplete/Dialog; real for InputDialog); the composer places the cursor there (overlay is AboveInput → its rows start
  at fixed-region row 0) instead of the main input / instead of hiding. (2) Generalize the pending-completion machinery so it isn't `Dialog`-only: either make
  `PendingDialogCompletion` a parameterless `Action` (closure captures the overlay+TCS) invoked for any dismissed modal overlay, or extract `IModalOverlay { CloseRequest }`
  that both `Dialog` and `InputDialog` implement, and generalize `OpenDialogCommand`/`ShowDialog`. Reuse Chunk A's cancellation/reject pattern (reject a 2nd modal overlay).
- **Chunk C — façade surfaces + events** (12.3, 12.4, 12.5 round-trip): `Scrollback`/`Input`/`Status`/`Autocomplete` sub-objects on `Terminal` posting fire-and-forget
  `ILoopCommand`s (wrap `ScrollbackModel.Append/BeginLive/BeginCollapsible`, `TextBuffer`, `StatusLine.Rows`, `RenderModel.ShowAutocomplete/ClearOverlay`).
  **Events (currently only `KeyPressed`/`Resized` are emitted):** wire `InputSubmitted(text)` on main-input Enter (no overlay) — emit + add-to-history + clear the buffer
  (readline-style; flag it) — and `InputChanged(text)` on every buffer mutation (this drives the autocomplete round-trip). `Autocomplete` candidates need an `InsertText`+display
  shape (`AutocompleteCandidate` already exists internally).
- **Chunk D — `ITerminal` + public exposure + fakeable tests** (12.6, 12.7): extract `ITerminal` (+ `IScrollback`/`IInput`/`IStatus`/`IAutocomplete`), `Terminal` implements it;
  confirm `KeyEvent`/`PasteEvent`/`ResizeEvent`/`TerminalEvent.*`/`DialogResult<T>`/`DialogOutcome` are public & constructible (watch internal-only ctors); **no static/singleton
  state** on consumer paths; tier-A test = a hand-written fake `ITerminal` records command-side calls and feeds synthesized events/results.

**Carry-over doc nit (not §11 scope):** `RenderModel.cs` XML docs (~lines 36–38, 72–76) still say `MaxFixedHeight` is "recorded but not yet
enforced; §10 will apply…" — stale since §10 shipped and `FixedRegionComposer.ComputeCap` enforces it. Fold a correction into a §12/§13 doc pass.
