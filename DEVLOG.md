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
- Working tree is **CLEAN**: §12 is fully committed (`394c9ba`, chunks A–D). Resume at **§13**.
- Sanity check: `dotnet build -c Release && dotnet test -c Release && dotnet format --verify-no-changes && openspec validate core-rendering-architecture --strict`
  → expect **0 warnings, 615 tests green, clean format, valid**.
- Resume point = **§13 — Resize & reflow** (Open Question #2; expect a user confirmation like §8). See the §13 row in the Section status table.
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
| 12 | Public API: dialogs, events, façade | `394c9ba` | 615 | `ITerminal` + sub-surfaces, awaitable dialogs (`SelectAsync`/`MultiSelectAsync`/`InputAsync`/`ChoiceAsync` → `DialogResult<T>`), `Events` emission (`InputSubmitted`/`InputChanged`/`KeyPressed`/`Resized`), public `Scrollback`/`Input`/`Status`/`Autocomplete` façade + `ILiveBlock`/`ICollapsible` handles. Much of §9/§10/§11 was built as **internal model + commands** specifically so §12 wraps them. |
| 13 | Resize & reflow | Chunk A `e2b2b97`, Chunk B `6f9f3e9` | 660 | **Open Question #2** resolved. Chunk A: `IResizeWatcher` + POSIX SIGWINCH + reflow tests + manual CHECK 5; surfaced & fixed an AArch64-Darwin variadic-ABI bug in §7 `PosixTerminalSizeSource.GetSize` (replaced manual `ioctl` P/Invoke with `Console.WindowWidth`). Chunk B: `TerminalCapabilities` (`HasTruecolor`/`HasSynchronizedOutput`/`TreatAmbiguousAsWide`) + `TerminalCapabilityDetector.DetectCapabilities`; `SgrTranslator` static→instance with truecolor→256-indexed nearest-match downgrade (cube + grey ramp, squared-Euclidean). |
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
- **Done — §13 CHECK 5 (Chunk A):** user ran the harness on macOS and confirmed `[CHECK 5] Initial size: …` plus per-resize
  `[CHECK 5] Resized to: …` lines. The first run surfaced a real latent §7 bug (AArch64 Darwin variadic ABI: `ioctl(int, unsigned long, ...)`
  cannot be marshalled by `LibraryImport` as a fixed-arity call on Apple Silicon — args 3+ must travel on the stack, not in registers).
  Replaced the hand-rolled `ioctl` P/Invoke with `Console.WindowWidth`/`Console.WindowHeight` (the .NET runtime's `libSystem.Native` shim
  is non-variadic). Side benefit: `WindowsTerminalSizeSource` is no longer a hardcoded 80×24 stub.
- **Pending:** §13 Chunk B (capability detection — 13.3/13.4, no real-terminal verification expected). §14.1 (manual cross-platform smoke),
  §14.2 (demo app), §14.3 (NuGet pre-release publish — outward-facing, will confirm before publishing). Windows runtime paths are stubbed
  and deferred to §14.1.

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

> **NEXT: §14 — Cross-platform validation & packaging.** §13 is COMPLETE and committed (Chunk A `e2b2b97`, Chunk B `6f9f3e9`). §14 is **human-in-the-loop**: 14.1 manual smoke on Windows Terminal / macOS / Linux; 14.2 demo app; 14.3 finalize NuGet/README/XML docs + pre-release publish; 14.4 port a `Dmon.Terminal` vertical slice to validate end-to-end API ergonomics. After §14 comes §15 (`Dcli.Testing` headless harness).

### §13 chunk progress

- **Chunk A — DONE (committed).** New `IResizeWatcher` edge interface (parallel to `ITerminalSizeSource`/`IInputByteSource`/`IClock`/`IOutputSink`).
  `PosixResizeWatcher` uses `PosixSignalRegistration(SIGWINCH)` → queries `ITerminalSizeSource` → posts `ResizeEvent` via `loop.InputWriter` (the
  signal-handler thread does only the `TryWrite` on the unbounded inbound channel; loop-thread discipline preserved). `WindowsResizeWatcher` is a
  compile-only no-op stub deferred to §14.1, consistent with the §6.1 Windows input stub. Threaded through `Terminal.StartAsync`/`StartCore`;
  `DisposeAsync` disposes the watcher BEFORE the loop. Reflow happens automatically through the existing apply→paint cycle (`ApplyInputEvent`
  marks dirty; `FixedRegionComposer.Compose` and `ScrollbackModel.PrePaint` re-read `model.Columns`/`model.Rows` per paint). 5 new reflow tests
  (`ResizeReflowTests.cs`) drive a `FakeResizeWatcher` to assert: width reflows the live window, cap recomputes on row change, frozen rows are
  not re-emitted, `Resized` outbound is emitted, volatile snapshot updates. Manual harness gained a CHECK 5 — `samples/Dcli.RawModeHarness/Program.cs`
  prints the initial size on raw-mode entry and on every SIGWINCH. **620 tests green.**
  - **Latent §7 P/Invoke bug surfaced & fixed (audit done):** the §13 manual harness was the first real-machine invocation of `PosixTerminalSizeSource.GetSize()`
    on the user's Apple Silicon (tests stub the seam, §4.5 didn't touch it). The hand-rolled `ioctl_winsize` P/Invoke AV'd on AArch64 Darwin because
    `ioctl(2)` is C-variadic and Darwin's AAPCS64 puts variadic args on the **stack**, but `LibraryImport` generates a fixed-arity call (`x2` for arg3).
    On Linux x86_64 SysV the bug is invisible (variadic and fixed args share registers). Fix: dropped the manual ioctl P/Invoke and routed through
    `Console.WindowWidth`/`Console.WindowHeight` (the .NET runtime's `libSystem.Native` shim is non-variadic). Also audited every `[LibraryImport("libc"...)]`
    in `src/Dcli/Internal/Posix/` — `read`/`tcgetattr`/`tcsetattr` are all correct. Side benefit: `WindowsTerminalSizeSource` got promoted from a hardcoded
    `(80, 24)` stub to the same real implementation.
  - **Process note (carry-over reminder):** the fix-and-audit worker call also silently implemented most of Chunk B (capability types, SGR downgrade,
    detection wiring, ~250 lines + 34 tests) without being briefed. Reverted; Chunk B is being redone via a proper brief. The diff was caught only by
    checking `git status` against the worker's claimed file list — keep doing the trust-but-verify check on every worker report.
- **Chunk B — pending.** `TerminalCapabilities` record + `TerminalCapabilityDetector.DetectCapabilities(CapabilityInputs?)`. Truecolor: detect via
  `COLORTERM=truecolor|24bit`; when absent, `SgrTranslator` downgrades `Color.ColorKind.Rgb` to the nearest 256-indexed (xterm cube + grey ramp,
  squared-Euclidean nearest). Synchronized-output: small `TERM_PROGRAM`/`TERM` allow-list; record only (we already emit the fence harmlessly).
  Unicode-width: record an East-Asian flag from `LANG`/`LC_ALL`; behaviour unchanged in v1. Capability test matrix (13.4). All internal, no public surface.

**Section 12 — public API: dialogs, events, façade.** This section **wrapped** the internal model+commands built in §9/§10/§11; little new
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

### §12 chunk progress (COMPLETE — committed `394c9ba`)

- **Chunk A — DONE (reviewer-approved, uncommitted).** Public `DialogOutcome {Submitted,Back,Cancelled}`, `DialogResult<T>(Outcome,Value)` (constructible);
  `SelectRequest`/`MultiSelectRequest`/`ChoiceRequest` (minimal: items/options + optional title/prompt `Line`); `Terminal.SelectAsync`/`MultiSelectAsync`/`ChoiceAsync`.
  Plumbing: `OpenDialogCommand` + `CancelDialogCommand` (`src/Dcli/Internal/RenderLoop/`); `RenderModel.PendingDialogCompletion` (`Action<Dialog>`, set with
  `ShowDialog`, invoked **before** `ClearOverlay` in the loop dismiss hook, cleared on clear); TCS bundled with `RunContinuationsAsynchronously`.
  Decisions: **reject only a concurrent `Dialog`** (an active Autocomplete is *suppressed* via `ShowDialog`), reject = fault the task with `InvalidOperationException`;
  cancel-token → `Cancelled` (registration disposed on every terminal path; already-cancelled fast path; `CancelDialogCommand` guards on `ReferenceEquals(ActiveOverlay, expected)`);
  `Dialog` got an optional **title row** (`Render` truncates `[title]++list` to `MaxRows`, so title-only at `MaxRows==1`); empty-list Submit → `Submitted(-1)`.
  Carry-forward nits: `AlreadyCancelledTokenReturnsCancelledNoOverlay` re-implements the fast path inline (tighten once Chunk D wires the real façade);
  test cancel-closures use `-1`/`[]` vs production `default!` — cosmetic.
- **Chunk B — DONE (reviewer-approved, uncommitted).** Public `InputRequest(Line? Prompt = null, string? Default = null, bool IsSecret = false)` +
  `Terminal.InputAsync → DialogResult<string>` (no validator delegate — a `Func` validator would run consumer code on the loop thread, banned by Decision 10).
  New `InputDialog : IModalOverlay` (modal, AboveInput) hosting its **own** `TextBuffer` (seeded via `SetText(Default)`; `IsSecret` masks each rune with
  `DisplayWidth.Measure(rune)`×`'•'` so caret columns stay correct; real unmasked text returned on Submit). `HandleKey`: Enter→Submit, Esc→Cancel,
  **Ctrl/Alt short-circuit to the modal catch-all (no insert/nav)**, else printable→Insert / Backspace / Delete / arrows / Home-End / Up-Down; modal catch-all swallows the rest.
  **Seam 1 (cursor in overlay):** `IOverlay.CaretInOverlay (Row,Col)?` (null for Autocomplete/Dialog; real for InputDialog). `FixedRegionComposer.Compose` parks the
  hardware cursor at `overlayStartRow + caret.Row` (start = 0 AboveInput / `inputRows.Count` BelowInput) **only when overlay rows were actually rendered** (phantom-row guard);
  `InputDialog.HidesCursor => false` (cursor shown, unlike the select Dialog). **Seam 2 (generalized completion):** new `internal IModalOverlay : IOverlay { OverlayCloseKind? CloseRequest }`
  (`Dialog` + `InputDialog` implement it); `RenderModel.ShowDialog`→`ShowModal(IModalOverlay, Action?)`, `PendingDialogCompletion`→`PendingModalCompletion` (parameterless `Action?`);
  loop dismiss hook is now `PendingModalCompletion?.Invoke(); ClearOverlay()` (no `is Dialog` check); `OpenDialogCommand`/`CancelDialogCommand` take `IModalOverlay`; reject covers any
  concurrent modal overlay (`is IModalOverlay`), Autocomplete still suppressed not rejected; `Terminal.OpenDialogAsync<T>`→`OpenModalAsync<T>(IModalOverlay, Func<DialogResult<T>>, ct)`.
  N1 fix: `InputDialog._lastWidth` seeded on the loop thread via `OpenDialogCommand.Apply → SeedWidth(model.Columns)` (FIFO drain guarantees it's set before any same-batch width-dependent
  `HandleKey`); `Render` keeps updating it for resize-while-open. Tests: `InputDialogTests.cs` (incl. type+Enter, seeded default, secret-mask-vs-real-text, Esc, token-cancel, Backspace,
  **Ctrl+C not inserted**, seam-1 cursor-visible vs select-Dialog-cursor-hidden, cross-modal reject both directions); `DialogSelectionTests`/`OverlayRoutingTests` retargeted to the new API.
  **589 tests green.** Reviewer note (architectural follow-up, not now): if a 2nd overlay ever needs open-time model state, promote the `is InputDialog` seed in `OpenDialogCommand` to an
  `OnShown(model)`-style `IModalOverlay` method rather than chaining `is` checks.
- **Chunk C — DONE (reviewer-approved, uncommitted).** Public concrete surfaces on `Terminal`: `ScrollbackSurface` (`Append(Line)`; `BeginLive()→ILiveBlock` with
  `AppendText/SetContent/Commit`; `BeginCollapsible(summary, hiddenLines)→ICollapsible` with one-way `Expand`), `InputSurface` (`SetText`/`Clear` only — `Prompt`/`ReadOnly`
  **deferred** by user decision, documented gap), `StatusSurface` (`Set(params Line[])` + `IReadOnlyList<Line>` overload → `FixedRegionComposer.Status` now exposes the `StatusLine`),
  `AutocompleteSurface` (`Show(IReadOnlyList<AutocompleteCandidate>)` builds `new Autocomplete(model.FixedRegion.Editor)` on the loop thread + `ShowAutocomplete`; `Hide()` clears
  directly when the active overlay is an `Autocomplete`). `AutocompleteCandidate(string InsertText, Line Display)` **promoted internal→public** in `Dcli` (no internal duplicate left);
  public `ILiveBlock`/`ICollapsible` handle interfaces (internal handle classes hold the LoopEngine + the **pre-created** `LiveBlock`/`Collapsible` — the one allowed off-thread ref;
  only mutated on the loop thread via FIFO-ordered commands, never the capture-callback `BeginLive/BeginCollapsibleCommand`). **Thread discipline:** every façade command pulls
  loop-owned model state from its `Apply(model)` param; surfaces hold only the `LoopEngine`. **Events:** `InputSubmitted(text)` on plain Enter (no overlay, no Ctrl/Alt) →
  emit + `AddToHistory` (skip empty) + `Clear()` (readline-style; suppresses `KeyPressed(Enter)`); `InputChanged(text)` when a key routed to the editor changed `editor.Text`
  (before/after compare — covers Insert/Backspace/Delete/history-recall, excludes pure caret moves, the overlay path, and programmatic `SetText`/`Clear`). **Documented contract note
  (reviewer, non-blocking):** accepting an Autocomplete candidate (`SetText` inside the overlay path) emits **no** `InputChanged` — a buffer-mirroring consumer would lag until the next
  keystroke; revisit (emit-on-accept, or XML-doc it on `AutocompleteSurface`) only if a consumer needs it. Tests: `FacadeTests.cs` (14, incl. the real-`Terminal` `Events`-channel
  round-trip); `FixedRegionTests`/`OverlayRoutingTests`/`RenderLoopTests` retargeted to the new Enter→`InputSubmitted` / printable→`InputChanged` behaviour. **603 tests green.**
  Deferred (noted gaps, not Chunk C): `Scrollback.AppendRule`, incremental `Collapsible.AppendLine`, `PasteEvent` editor routing.
- **Chunk D — DONE (reviewer-approved, committed in `394c9ba`).** New `src/Dcli/ITerminal.cs`: public `IScrollback`/`IInput`/`IStatus`/`IAutocomplete` + `ITerminal : IAsyncDisposable`;
  `Terminal : ITerminal` with the four sub-surface properties retyped concrete→interface; each surface class carries `: IXxx`. Event/result types (`KeyEvent`/`PasteEvent`/`ResizeEvent`/
  `TerminalEvent.*`/`DialogResult<T>`/`DialogOutcome`) were already public & constructible (no internal-only ctors). **Static-state audit clean** — all per-instance; only stateless static
  factories (`KeyCode.FromRune`/`Named`, `Terminal.StartAsync`). **`IStatus.Set`→`SetRows`** (CA1716: `Set` is a VB.NET keyword once it sits on an interface/overridable member; rename-over-
  suppress per the §7 precedent — design sketch's `Status.Set(...)` was illustrative, the normative spec doesn't pin Status method names). Tier-A test `tests/Dcli.Tests/FakeTerminalTests.cs`
  (hand-written `FakeTerminal : ITerminal` records command-side calls + drives a consumer-style method with synthesized `KeyEvent`/`DialogResult`; 12 tests). **615 tests green.** Reviewer nit
  (cosmetic, not re-spun): the fake's `SetCalls` field + a comment still say "Set" while the method is `SetRows` — align if the file is touched again.

**Carry-over doc nit (not §11 scope):** `RenderModel.cs` XML docs (~lines 36–38, 72–76) still say `MaxFixedHeight` is "recorded but not yet
enforced; §10 will apply…" — stale since §10 shipped and `FixedRegionComposer.ComputeCap` enforces it. Fold a correction into a §12/§13 doc pass.
