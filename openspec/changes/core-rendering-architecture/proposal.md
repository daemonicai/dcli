## Why

`dcli` needs a foundational rendering architecture before any feature work can begin. The library targets a specific, deliberately-constrained UX — Claude-Code-style **inline** rendering: marked-up output that scrolls into the terminal's *real* scrollback, with a small interactive region (input + status + dropdowns) pinned at the bottom. This is distinct from a full-screen TUI, and the constraints it imposes shape every later decision, so they must be settled and recorded up front.

## What Changes

- Establish **inline rendering** (no alternate screen) as the core model: output flows into native terminal scrollback; only a bounded region near the bottom is ever re-rendered.
- Define the **commit horizon**: a live, re-renderable window (`≤ rows − fixedRegionHeight`) above which content is frozen, write-once, and owned by the terminal.
- Define the **scrollback data model**: a flat list of line-objects (`Segment → Line → {TextBlock | Collapsible}`), with collapsibles as **one-way** (monotonic-height) expandable blocks.
- Define the **fixed region** as a component stack (`Input`, `ScrollableList`, `StatusLine`) with two mutually-exclusive overlays — a **Dialog** slot above the input and **Autocomplete** below — intercept-chain key routing, and a `MaxHeight` budget.
- Establish the **dcli ↔ consumer boundary**: dcli owns rendering and widget *mechanics*; the consumer (dmon) owns *data and semantics* (RPC/JSONL, completion candidates, the notion of a "Turn").
- Define a single **programmatic styling** primitive (`Segment`/`Style`) shared by both zones — no markup parser.
- Establish the **raw-mode input layer**: one VT byte-stream driver (own `termios`/Win32-console shims, no `Console.ReadKey`), one `VtInputParser`, a School-A `KeyEvent` contract (`Char(Rune) | Named` + modifier flags), bracketed paste, eaten Ctrl+C, and guaranteed terminal restore. Modern VT terminals only.
- Establish the **render loop**: an actor-model single thread owning all UI state, stdout, and the raw-mode session; producers post to an inbound `Channel`, dcli→dmon events flow out on a separate `Channel`; event-driven drain-coalesce-throttle cadence; fire-and-forget async API.
- Establish the **public API surface**: a background `Terminal` façade with fire-and-forget scrollback/status commands, an outbound `Events` stream, and `await`-able modal **dialogs** (select / multi-select / input / choice → `DialogResult<T>`); styling via `Segment`/`Line`/`Style` with a `[Flags] Format` enum. Validated against — and a near-drop-in for — dmon's existing `Dmon.Terminal` layer.
- Make the public surface **testable without a terminal**: the `Terminal` façade is an injectable interface (`ITerminal`) with publicly-constructible event/result types so consumers can fake it (tier A), and a companion **`Dcli.Testing`** package ships a **headless harness** — `HeadlessTerminal` + scripted input + deterministic `SettleAsync` + a structured frame `Snapshot` — for terminal-free integration tests (tier B). This is also the substrate dcli uses to test itself.

## Capabilities

### New Capabilities
- `inline-scrollback`: the append-mostly scrollback buffer — line-objects, the commit horizon, one-way collapsibles, and the content-model-to-visual-rows render contract.
- `fixed-region`: the pinned bottom component stack — owned input editor, two mutually-exclusive overlays (a **Dialog** slot above the input, **Autocomplete** below), the reusable scrollable selection list, status lines, height budgeting, and intercept-chain key routing.
- `styled-text`: the programmatic `Segment`/`Line`/`Style` primitive (with a `[Flags] Format` enum and a `LineBuilder`) shared across both zones.
- `terminal-input`: the raw-mode VT input driver — OS mode shims (`RawModeSession`), the `VtInputParser` state machine, the `KeyEvent`/`PasteEvent`/`ResizeEvent` contract, and lifecycle/restore safety.
- `render-loop`: the actor-model render loop and `Terminal` lifecycle — single-writer event loop, fire-and-forget command intake, the outbound event stream, coalesced/throttled frames, and guaranteed terminal restore.
- `test-harness`: the terminal-free testing surface — a substitutable `ITerminal` façade with publicly-constructible event/result types (tier A) and the public `Dcli.Testing` headless harness (tier B: `HeadlessTerminal`, scripted input drivers, deterministic `SettleAsync` / virtual clock, and a structured frame `Snapshot`). The same substrate dcli uses to test its own loop and frames.

### Modified Capabilities
<!-- None — this is the first change; openspec/specs/ is empty. -->

## Impact

- **New library surface** (NuGet, C#/.NET): **two packages** — `dcli` (public render model + interactive components + input driver, behind a substitutable `ITerminal` façade) and **`Dcli.Testing`** (the public headless harness).
- **Terminal requirement**: a modern VT-capable terminal (Windows Terminal / Win10 1809+ / xterm-class). Legacy conhost is unsupported.
- **Out of scope here, but unlocked next**: the `renderFrame()` **paint mechanism** (synchronized output vs. per-line diff) and **capability detection / resize plumbing**. These are mechanism, tracked as Open Questions in `design.md`. (Raw-mode input and the render-loop topology, originally grouped here, are now specified — see Decisions 9 and 10.)
- **Deferred to a follow-up change — cross-platform manual smoke (§14.1)**: the §14 implementer has access only to macOS, so §14.1 covers macOS only. Windows Terminal and Linux real-terminal smoke are deferred until a tester with those OSes can drive the §14.2 demo. Cross-platform `dotnet build` / `dotnet test` continues to run in CI on Linux / macOS / Windows (§1.2), so cross-platform *build* parity is gated; only *visual-rendering* smoke is deferred. NuGet publication to nuget.org is held until that smoke clears — §14.3 stops at `dotnet pack`.
- **Consumer contract**: dmon must own the RPC/JSONL protocol and feed dcli through the render model; the protocol never leaks into the library.
- **Consumer testing** (Decision 12): consumers unit-test controllers against a faked `ITerminal`, and write terminal-free integration tests against `Dcli.Testing`'s `HeadlessTerminal` + frame `Snapshot` — the same harness dcli uses for its own deterministic loop/frame tests.
