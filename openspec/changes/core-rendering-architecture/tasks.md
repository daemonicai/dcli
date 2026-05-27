## 1. Project setup

- [x] 1.1 Create the `dcli` class-library project (target `net10.0`; nullable + analyzers enabled) and the test project
- [x] 1.2 Add CI building/testing on Linux, macOS, and Windows; wire NuGet packaging metadata (id, license, README placeholder)
- [x] 1.3 Define the public namespace/assembly layout and `InternalsVisibleTo` for tests

## 2. Styled text (styled-text)

- [x] 2.1 Implement `[Flags] Format` enum (None, Bold, Italic, Underline, Dim, Reverse, Strikethrough)
- [x] 2.2 Implement `Color` (named / 256-indexed / 24-bit truecolor) and `Style` (optional fg/bg + `Format`)
- [x] 2.3 Implement `Segment` and `Line` records and `LineBuilder` (append-order composition)
- [x] 2.4 Tests: segments retain styles, markup-like text is literal, `Format` combination, builder ordering

## 3. Display width & wrapping (shared)

- [x] 3.1 Implement display-width measurement (wcwidth: wide CJK = 2, combining/zero-width = 0, control/tab handling)
- [x] 3.2 Implement width-aware wrapping of a `Line` into visual rows at a given width
- [x] 3.3 Tests: CJK/emoji/combining/zero-width width counts; wrap boundaries

## 4. Terminal driver: raw mode & capability detection (terminal-input)

- [x] 4.1 Implement `RawModeSession` for POSIX (P/Invoke `termios`: clear ICANON/ECHO/ISIG/IEXTEN, IXON/ICRNL; VMIN=0/VTIME=1) with platform-specific struct layout
- [x] 4.2 Implement `RawModeSession` for Windows (`SetConsoleMode`: +VIRTUAL_TERMINAL_INPUT, −LINE/ECHO/PROCESSED_INPUT; +VIRTUAL_TERMINAL_PROCESSING on output)
- [x] 4.3 Capability detection: require a modern VT terminal; fail with a clear error otherwise
- [x] 4.4 Guaranteed restore: `IDisposable` + `PosixSignalRegistration` (SIGINT/SIGTERM/SIGQUIT/SIGCONT) + `AppDomain.ProcessExit`
- [x] 4.5 Tests/manual harness: raw mode entered; restore on normal exit, exception, and signal

## 5. VT input parser (terminal-input)

- [x] 5.1 Implement the parser state machine (GROUND → ESC → CSI → emit) with UTF-8 accumulation, emitting `KeyEvent`/`PasteEvent`/`ResizeEvent`
- [x] 5.2 Implement `KeyCode = Char(Rune) | Named(...)` + `[Flags]` modifiers; named-key table (arrows, Home/End, PgUp/Dn, F-keys, BackTab, etc.)
- [x] 5.3 ESC disambiguation via read timeout; Alt-as-ESC-prefix
- [x] 5.4 Bracketed paste (`ESC[?2004h`) → single `PasteEvent`
- [x] 5.5 Terminal-truth rules: named key for Ctrl-letter collisions; Shift implicit in runes; no Shift under Ctrl
- [x] 5.6 Tests: byte-fixture suite covering arrows, unicode scalars, paste, lone-ESC vs sequence, Tab≡0x09, 'A' has no Shift

## 6. Input reader thread (terminal-input)

- [x] 6.1 Implement `InputReader` on a dedicated thread doing timed byte reads, feeding the parser, posting events to the inbound channel
- [x] 6.2 Surface Ctrl+C as a `KeyEvent` (no process termination by the library)

## 7. Render loop core (render-loop)

- [x] 7.1 Define the inbound message union (input events + API commands) and outbound `TerminalEvent` types
- [x] 7.2 Implement the single-writer loop on a dedicated thread: `await WaitToReadAsync(orUntil: nextPaintDeadline)`, drain-all, apply, throttled paint
- [x] 7.3 Implement fire-and-forget command intake (unbounded inbound `Channel`) and snapshot reads (terminal size)
- [x] 7.4 Implement the outbound `Channel<TerminalEvent>` (consumer drains on its own thread; loop never runs consumer code)
- [x] 7.5 Implement `Terminal.StartAsync` / `DisposeAsync` lifecycle, wiring raw mode + restore into the loop's `finally`
- [x] 7.6 Tests: no interleaving under concurrent producers; burst coalescing → one frame; idle = no repaint; slow consumer doesn't stall the loop (deterministic, via the headless harness — §15)

## 8. Frame painting (render-loop — Open Question #1)

- [x] 8.1 Implement `renderFrame()` cursor accounting for the bounded live window + fixed region (move-to-anchor, clear, repaint)
- [x] 8.2 Wrap each frame in a synchronized-output fence (`ESC[?2026h … l`); no-op safely where unsupported
- [x] 8.3 Park/hide the hardware cursor per fixed-region rules at end of frame
- [x] 8.4 (Deferred) per-line diff reconciler as a flicker optimization — leave a seam, do not implement in v1
- [x] 8.5 Tests: golden-frame output for representative live-window + fixed-region states (snapshot via the headless harness — §15)

## 9. Scrollback model (inline-scrollback)

- [x] 9.1 Implement the `ILineObject` contract `Render(width) → rows` for `TextBlock`
- [x] 9.2 Implement the commit horizon + bounded live window; commit/freeze topmost live lines on overflow; drop committed objects from memory
- [x] 9.3 Implement the live (streaming) block: `AppendText` / `SetContent` / `Commit`
- [x] 9.4 Implement one-way `Collapsible`: collapsed summary, single `Expand`, freeze-collapsed at horizon
- [x] 9.5 Implement oversized-expansion reprint-into-flow (β)
- [x] 9.6 Tests: overflow commits & never rewrites frozen rows; live append/settle/commit; expand-once & no-recollapse; oversized reprint

## 10. Fixed region: input editor & status (fixed-region)

- [x] 10.1 Implement the owned input `TextBuffer`: caret, multiline, width-aware wrap, internal scroll, history recall
- [x] 10.2 Implement grapheme-cluster handling at the editing layer (navigate/measure by visual character)
- [x] 10.3 Implement the `StatusLine` component and the bottom-pinned component stack
- [x] 10.4 Implement the `MaxHeight` budget `clamp(appSet ?? 50%, 8, rows)` + internal-scroll priority (status sacred / caret visible / overlay absorbs squeeze)
- [x] 10.5 Tests: wrap tracks caret; history recall; default/min-floor/tiny-terminal cap values

## 11. Fixed region: overlays — autocomplete & dialog (fixed-region)

- [x] 11.1 Implement the reusable `ScrollableList` (bounded viewport, selection, auto-scroll, optional multi-select via space)
- [x] 11.2 Implement the `OverlayState = None | Dialog | Autocomplete` invariant + intercept-chain routing (active overlay first)
- [x] 11.3 Implement Autocomplete (below input): show/hide, consumer-supplied candidates, apply accepted insert-text to the buffer
- [x] 11.4 Implement the Dialog slot (above input): modal-by-default with opt-in type-to-filter; hide hardware cursor while modal
- [x] 11.5 Tests: dialog suppresses autocomplete; nav keys consumed vs printable fall-through; modal captures all keys; cursor placement

## 12. Public API: dialogs, events, façade (render-loop + fixed-region)

- [x] 12.1 Implement `DialogResult<T>` / `DialogOutcome` and the awaitable dialog methods (`SelectAsync`, `MultiSelectAsync`, `InputAsync`, `ChoiceAsync`) via TCS-bundled open-dialog commands
- [x] 12.2 Implement dialog cancellation via `CancellationToken` (closes overlay → `Cancelled`) and reject/queue a second concurrent dialog
- [x] 12.3 Implement the `Scrollback`, `Input`, `Status`, `Autocomplete` façade surfaces over the inbound channel
- [x] 12.4 Implement the `Events` stream emission (`InputSubmitted`, `InputChanged`, `KeyPressed`, `Resized`)
- [x] 12.5 Tests: select submit/cancel; multi-select toggle; cancellation token; input-change → candidates round-trip
- [x] 12.6 Expose the façade as `ITerminal` (+ `IScrollback`/`IInput`/`IStatus`/`IAutocomplete`); make event/result types (`KeyEvent`/`PasteEvent`/`ResizeEvent`/`TerminalEvent.*`/`DialogResult<T>`/`DialogOutcome`) public and constructible; no static/singleton state on any consumer-facing path (tier A — `test-harness`)
- [x] 12.7 Tests: a hand-written fake `ITerminal` substitutes for the real façade; synthesized events/results drive consumer-style code; command-side calls (scrollback/status/dialog) are recorded and asserted

## 13. Resize & reflow (Open Question #2)

- [ ] 13.1 Wire resize delivery: `PosixSignalRegistration(SIGWINCH)` on POSIX; buffer-size event / polling on Windows → inbound `ResizeEvent`
- [ ] 13.2 On resize, recompute width and reflow + repaint the live window and fixed region (frozen scrollback left to the terminal)
- [ ] 13.3 Detect truecolor / synchronized-output / unicode-width support; record fallbacks
- [ ] 13.4 Tests: live window reflows on width change; cap recomputes; no rewrite of frozen content

## 14. Cross-platform validation & packaging

- [ ] 14.1 Manual/smoke validation on Windows Terminal, macOS, and Linux (xterm-class) terminals
- [ ] 14.2 Build a sample/demo app exercising streaming, collapsible thinking, autocomplete, and a wizard-style dialog sequence
- [ ] 14.3 Finalize NuGet package (README, XML docs on the public surface) and publish a pre-release
- [ ] 14.4 Port a vertical slice of dmon's `Dmon.Terminal` onto dcli to validate the API ergonomics end-to-end (using the headless harness from §15 for its tests)

## 15. Headless test harness — `Dcli.Testing` (test-harness)

> The OS-facing edges are introduced behind interfaces as the driver/loop/painting are built (§4, §6–§8); this section generalizes that substrate into the public, documented `Dcli.Testing` package and retargets dcli's own terminal-free tests onto it.

- [ ] 15.1 Factor the OS-facing edges behind interfaces — raw-mode session, input byte source, terminal-size source, output sink, clock — so the real loop/driver run against swappable edges (the load-bearing core is never mocked)
- [ ] 15.2 Implement `HeadlessTerminal` (a real `ITerminal` over in-memory edges) with scripted input drivers: `Feed(bytes)`, `SendKey`, `Type`, `Paste`, `Resize`
- [ ] 15.3 Implement deterministic `SettleAsync` (drain all pending work → exactly one coalesced frame, no wall-clock time) and a controllable virtual clock for cadence tests
- [ ] 15.4 Implement the structured frame `Snapshot` (live-window rows as styled runs, fixed-region layout, caret position, active overlay) + a human-diffable pretty-printer for golden frames
- [ ] 15.5 Ship `Dcli.Testing` as a separate package with an XML-documented public API; the production `dcli` package takes no dependency on it
- [ ] 15.6 Retarget the terminal-free tests onto the harness (parser fixtures §5.6, loop coalescing §7.6, golden frames §8.5) so dcli and its consumers share one substrate
- [ ] 15.7 Tests: byte-feed → parsed event; `SettleAsync` → a single frame; virtual-clock advance triggers a throttled paint; snapshot reflects scrollback content + overlay state
