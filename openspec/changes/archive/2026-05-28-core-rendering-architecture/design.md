## Context

`dcli` is a standalone terminal-rendering library distributed on **NuGet** (C# / .NET). Its first consumer is **dmon**, a CLI front-end to a separate **dmoncore** backend process. dmoncore streams content (assistant output, tool results, thinking) to dmon over a **custom RPC framed as JSONL over stdio**; dmon deserializes that and drives dcli to render.

The target UX is Claude-Code-like:

```
┌───────────────────────────────────────────────┐
│  scrollback: marked-up lines, flows UP into     │  ← native terminal
│  the terminal's real history                    │     scrollback
│  ⊞ Ctrl+O to view thinking (collapsible)        │
│  • streaming tail that grows…                   │
├───────────────────────────────────────────────┤  ← commit horizon
│  > input (owned editor)_                         │  ← fixed region,
│  ▸ autocomplete (scrollable, capped)             │     pinned, re-rendered
│  status                                          │
└───────────────────────────────────────────────┘
```

The defining constraint is **inline rendering**: dcli does *not* take over the screen with the alternate-screen buffer. Output stays in the user's real terminal history (scrollable, copyable). That single choice cascades through the entire design, which is why these decisions are captured before any code.

## Goals / Non-Goals

**Goals:**
- A reusable .NET/NuGet library for **inline** terminal UIs with a pinned, interactive bottom region.
- Preserve native terminal scrollback, copy, and scroll — never own the whole screen.
- Keep the re-rendered region **bounded** (≤ one screen) so cursor accounting and reconciliation stay tractable.
- Provide an **owned input editor** and a reusable **scrollable selection** component (autocomplete, menus, pickers).
- A clean boundary: dcli = rendering + widget mechanics; consumer = data + semantics.

**Non-Goals (v1):**
- Full-screen / alt-screen TUI (vim / htop / Terminal.Gui territory).
- Two-way toggles — collapsibles are **one-way** (expand only).
- Expanding **historical** (already-committed) content in place.
- **Nested** composite blocks — the scrollback model is flat in v1.
- A markup *parser* — styling is programmatic only.
- Owning the RPC/JSONL protocol — that is dmon's concern.
- A per-line diff reconciler — deferred optimization, not day-one.
- Legacy / non-VT terminals — a modern VT-capable terminal (Windows Terminal, Win10 1809+, any xterm-class emulator) is **required**; no legacy conhost `INPUT_RECORD` path.
- Mouse input, the **Kitty keyboard protocol**, and focus events — deferred; the VT parser stays extensible for them.

## Decisions

### 1. Inline rendering with native scrollback (the "Fork")

Render inline; preserve the terminal's native scrollback. Above a moving **commit horizon**, content is frozen, write-once, and owned by the terminal; below it, a **live window** is re-renderable.

- **Why:** Output stays in real terminal history (scroll/copy/search for free). The re-render region is bounded by the screen, so the classic cursor-accounting/reconciliation cost shrinks to a small fixed box. Simplest model that delivers the target UX.
- **Alternatives considered:**
  - **(B) Viewport owner (alt-screen):** dcli keeps its own scroll buffer and re-renders a visible window. Collapse/expand works anywhere, but you reimplement scrolling, mouse selection, search, copy — large scope, and you lose native scrollback ergonomics.
  - **(C) Hybrid:** most content commits frozen, a window stays re-renderable. More moving parts than (A) for marginal gain at v1.
- **Trade-off:** content that scrolls past the horizon can never be re-rendered (see Risks).

### 2. The commit horizon / live window

```
   live window height = terminalRows − fixedRegionHeight
```

The live window holds the streaming tail, not-yet-committed collapsibles, and sits above the fixed region. When new content would push the live window taller than it can be, the **topmost live lines commit** (print → scroll into native scrollback → freeze). Committed line-objects may be **dropped from memory** — dcli need not retain history; if the consumer wants it, the consumer keeps it.

- **Why:** Falls directly out of Decision 1. Bounds memory and per-frame work to one screen.

### 3. dcli = mechanics + rendering; consumer = data + semantics

The recurring boundary across every interactive piece:

```
   dcli owns                          dmon owns
   ─────────                          ─────────
   caret, edit ops, wrap, scroll      RPC/JSONL → scrollback content
   selection highlight, viewport      the candidate list itself
   measure, paint, park the cursor    what "accepting" an item does
   emits events                       what a "Turn" is
```

- **Why:** Keeps the NuGet package free of dmon's protocol; makes components reusable; mirrors React's controlled/owned split. dcli never knows *what* content means or *why*.
- **Alternative considered:** baking RPC/JSONL into the library — rejected; it would couple a general-purpose renderer to one app's protocol.

### 4. Platform: C# / .NET (`net10.0`), shipped on NuGet

Fixed requirement: a **single** target framework — **`net10.0`**, not multi-targeting. dcli is built and consumed on current .NET, so one TFM keeps the per-platform P/Invoke shims, analyzers, and packaging simple; there is no legacy-runtime support burden. Implications baked into later decisions: idiomatic single-writer loop via `System.Threading.Channels`; raw-mode keyboard input is a known .NET soft spot (see Open Questions); positioning relative to Spectre.Console (which does `Live`/`Status`/markup but **not** the collapsible-scrollback + pinned-input inline combo).

### 5. Scrollback data model: flat list, content ≠ visual

```
   Segment    = styled run of text  (style lives here, not in the text)
   Line       = [Segment...]        (one LOGICAL line; wraps to 1..N visual rows)
   LineObject = TextBlock | Collapsible   (elements of the scrollback list)
   scrollback = List<LineObject>          (FLAT — Model 1)
```

Every line-object implements one contract: `Render(width) → visual rows`. All width / wrapping / `wcwidth` complexity (CJK=2, combining=0, emoji, tabs) is contained there. **Content model ≠ visual model** — store segments/lines/blocks, paint rows of cells; the discipline that prevents smearing bugs.

- **Why flat (Model 1):** matches dmon's needs (thinking = a flat blob); `Render()` never recurses; simplest.
- **Alternative considered:** **(Model 2) tree** — a block contains blocks (tool card → foldable diff → …). Future-proof but recursive height math and expand semantics. Deferred; revisit only if dmon needs composite nested blocks.
- **Payoff under Decision 1:** `Render()` is only ever called on **live** blocks; frozen blocks were rendered once and can be dropped; resize reflows only the live window.

### 6. Collapsible semantics: one-way expand + reprint overflow

A `Collapsible` starts collapsed (summary line) and expands **once**; it never re-collapses.

- **Why one-way:** makes Decision 1 internally consistent. One-way ⇒ **monotonic height** ⇒ never need to push committed content back *up* (which is impossible anyway). Also removes all toggle/remeasure state.
- **Oversized expansion (β):** if an expanded block is taller than the live window, it does **not** stay re-renderable — its content **reprints into the flow** as normal output and scrolls away; the block stays a collapsed marker. "If it doesn't fit the live window, it's not a live toggle, it's just output."
- **Consumer policy example (dmon):** "show thinking" is on for the current *Turn*; dmon expands thinking blocks as they arrive while still live; the flag resets next Turn. dcli knows nothing of Turns — it only exposes "expand this block."
- **Alternative considered:** two-way toggle / expanding history — rejected; incompatible with inline rendering (Decision 1).

### 7. Styling: one programmatic primitive, shared

A single `Segment` / `Style` primitive (programmatic — no markup string parser) is used by **both** the scrollback line-objects and the fixed-region components.

- **Why:** avoids parser complexity; one styled-text type, two consumers, consistent rendering. Scrollback content arrives pre-styled (translated from RPC by dmon); fixed-region widgets are built programmatically.

### 8. Fixed region: component stack, intercept routing, MaxHeight budget

A **component stack** (Decision: option *b*), not a rigid input+status pair. The fixed region is one contiguous block pinned at the bottom, ordered around the Input:

```
   live window
   ───────────────────────────────
   ▸ Dialog overlay   (ABOVE)        ┐ at most ONE overlay active:
   > Input (caret)                   │ OverlayState = None | Dialog | Autocomplete
   ▸ Autocomplete overlay (BELOW)    ┘
   Status
```

v1 component vocabulary:

```
   Input          owned editor — caret, multiline, wrap, history, internal scroll
   ScrollableList bounded viewport over a longer list — maxRows, selection,
                  auto-scroll, optional multi-select via space  (autocomplete AND
                  dialog selection lists are both this)
   StatusLine     plain styled row(s)
```

- **Owned input editor:** dcli owns the `TextBuffer` (edit ops, caret, wrap, history). Rationale: caret↔wrapping math is so entangled with rendering that pushing it onto consumers means everyone reinvents a buggy readline.
- **Autocomplete is a `ScrollableList`,** fed candidates by dmon — generalizes free to every dropdown. The interaction loop:

```
   user types → Input buffer mutates → dcli raises TextChanged
        dcli renders dropdown ← dmon sets Items ← dmon computes candidates
   user ↑/↓ (dcli scrolls) → Enter → dcli raises Accepted(item)
        dcli Input updated ← dmon applies the completion
```

- **Overlays — Dialog (above) + Autocomplete (below):** both are *overlays* attached to the Input, unified by one invariant — **at most one active** (`OverlayState = None | Dialog | Autocomplete`), enforced by dcli (opening a Dialog suppresses Autocomplete; Autocomplete can't open while a Dialog is up). Because only one is ever active, nothing fights over `↑↓ / Enter / Esc`. Touches nothing load-bearing: the fixed region stays one contiguous bottom block (Dialog is just its topmost rows), so the commit horizon and `MaxHeight` math are unchanged; the Dialog gets its own maxRows + internal scroll, and exclusivity bounds the worst case.
  - *Dialog is a slot,* not a wizard engine — it hosts an interactive component (the `ScrollableList`, with `space` = multi-select toggle). A multi-step **wizard is dmon** swapping the slot's contents page-by-page and reacting to events; dcli owns only slot + routing + rendering (mechanics vs. semantics, Decision 3).
  - *Modal by default:* an active Dialog consumes **all** keys (the Input is inert beneath it); a Dialog may **opt in** to pass printable keys through for a type-to-filter list.
  - *Dialog events:* `Submitted(selection)` (Enter), `Cancelled` (Esc), `SelectionChanged`; `space` toggles in multi-select.
- **Key routing — intercept chain (option *i*):** components are ordered; each gets first refusal and consumes-or-passes. The **active overlay is the chain front** → Input → passives. An overlay eats its nav keys (Autocomplete: `↑↓ Enter Tab Esc`; Dialog: `↑↓ space Enter Esc`, plus *everything* when modal) and passes the rest to Input. "Active overlay = intercepting" is the entire state machine — no focus token to save/restore.
  - *Alternative considered:* focus-token model — scales to multi-field tabbing, but more state juggling than this UI needs.
- **Height budget — `MaxHeight` cap (not fixed size):**

```
   fixedRegion.MaxHeight = clamp( appSet ?? 50% of rows,  min 8,  max rows )
   live window           = rows − actualFixedHeight     (actual ≤ MaxHeight)
```

  The region takes only the rows its content needs, up to the cap; beyond that, components scroll internally. The `min 8` floor yields to the terminal height on TTYs smaller than 8 rows.
- **Internal over-budget priority:** status is sacred (always shown); the **caret line is always visible** (Input scrolls internally if taller); the **dropdown absorbs the squeeze** (capped at the remainder, up to its own maxRows).
- **Cursor:** the terminal has exactly one hardware cursor. Normally — and during Autocomplete — it parks at the **Input** caret after each frame; while a **modal Dialog** is active it is **hidden**. A selected row (autocomplete or dialog list) is reverse-video styling, never the real cursor.

### 9. Raw-mode input: one VT byte-stream, one parser, built in-house

We build the input/terminal-driver layer ourselves. `System.Console.ReadKey` is rejected (blocking, and its terminfo-based decoding silently drops Ctrl/Shift+Arrow, Alt combos, paste, etc.). There is no clean .NET equivalent of Rust's crossterm to adopt; Spectre.Console is higher-level, Terminal.Gui is a heavy alt-screen framework whose driver we may study but not depend on.

**Unifying strategy — make every platform deliver a raw VT byte stream, then run ONE parser:**

```
   POSIX (mac/Linux): termios raw mode — clear ICANON/ECHO/ISIG/IEXTEN,
                       IXON/ICRNL; VMIN=0,VTIME=1 for timed reads. (P/Invoke;
                       termios struct layout is platform-specific.)
   Windows:           SetConsoleMode + ENABLE_VIRTUAL_TERMINAL_INPUT (console
                       EMITS VT sequences), − ENABLE_LINE/ECHO/PROCESSED_INPUT;
                       + ENABLE_VIRTUAL_TERMINAL_PROCESSING on output.
   Floor:             a modern VT terminal is REQUIRED (Windows Terminal /
                       Win10 1809+ / xterm-class). No legacy conhost path.
```

Windows thereby stops being a second input model and becomes "Unix that flips modes differently." Cross-platform surface = two thin OS shims + one shared parser.

- **Why build it:** no standalone "raw VT input" package exists for .NET; this loop *is* dcli's core value; the pure parser is cheap to own and test.
- **Alternatives considered:** `Console.ReadKey` (lossy/blocking — rejected); depending on Terminal.Gui's driver (heavy, alt-screen-oriented — rejected, may study its quirk tables); supporting legacy conhost `ReadConsoleInput` (a whole second non-VT input model — rejected via the WT-only floor).

**Three layers:**
- `RawModeSession` — OS shim, `IDisposable`. Saves/restores original mode. Restore is non-negotiable (see Risks).
- `VtInputParser` — pure state machine (GROUND → ESC → CSI → emit; UTF-8 accumulation). No I/O; fully unit-testable against byte fixtures.
- `InputReader` — dedicated thread; timed byte reads feed the parser; posts events onto the `Channel<Event>` as one producer for the single-writer render loop.

**ESC ambiguity:** a lone `ESC` is indistinguishable from the start of `ESC[…` until a short timeout elapses → timed reads (VMIN/VTIME on POSIX, `WaitForSingleObject` on Windows). Alt+key arrives as an `ESC` prefix, resolved within the same timeout.

**Event model (School A):**

```
   InputEvent = KeyEvent | PasteEvent(string) | ResizeEvent(cols,rows)
                (MouseEvent, FocusEvent deferred; parser stays extensible)
   KeyEvent   = (KeyCode, Modifiers)
   KeyCode    = Char(Rune) | Named(Enter|Tab|Backspace|Escape|arrows|Home|
                End|PgUp|PgDn|Ins|Del|F1..F12|BackTab)
   Modifiers  = [Ctrl, Alt, Shift]   (flags)
```

Granularity: **Rune per key**, **string per paste**; grapheme clustering (combining marks, ZWJ) is handled at the `TextBuffer`/editor layer, not in `KeyEvent`. Rejected School B (`ConsoleKeyInfo`'s `Key`+`KeyChar`+`Modifiers`) — lossy for non-BMP/combining input and conflates physical key with letter.

**Terminal truths (accepted limitations — terminal physics, not bugs; documented):**
1. Ctrl-letter collisions are real on the wire — `Tab≡Ctrl+I`, `Enter≡Ctrl+M`, `Backspace≡Ctrl+H`, `Escape≡Ctrl+[`. We report the **named** key.
2. Shift is **implicit** in printable runes (`'A'`, not `'a'+Shift`); the Shift modifier appears only on **named** keys (`Shift+Arrow`, `Shift+Tab`/BackTab).
3. Shift is **lost under Ctrl** (`Ctrl+A ≡ Ctrl+Shift+A` → same byte). Only the Kitty protocol (deferred) could disambiguate.

**Bracketed paste** is enabled (`ESC[?2004h`) and surfaced as a single `PasteEvent` — so a pasted newline doesn't submit and autocomplete doesn't fire per-character.

**Ctrl+C is eaten**, surfaced as `KeyEvent(Char('c'), Ctrl)`; dcli takes no action — dmon decides whether it means clear-input, interrupt-Turn, or exit (mechanics vs. semantics, Decision 3).

**Consumption:** events flow down the intercept chain (Decision 8) via `bool HandleKey(KeyEvent)`, with the active overlay at the front. Autocomplete consumes `↑↓ Enter Tab Esc`; a Dialog consumes `↑↓ Enter Esc` plus `Char(' ')` (space = multi-select toggle), and **all** keys when modal (note: space is a printable `Char(' ')`, not a `Named` key). Everything unconsumed falls through to the Input editor; a `PasteEvent` goes to the Input as a literal insert (or to a Dialog that opted into type-to-filter).

### 10. Render loop: actor model, event-driven, dual channels

The loop is the **single owner of all mutable UI state** (the render model), stdout, *and* the raw-mode session — extending "single writer of stdout" to "single mutator of everything." An actor / single-dispatch design.

```
   Producers (ANY thread)                  THE LOOP (one dedicated thread)
   ──────────────────────                  ───────────────────────────────
   InputReader ─┐                           owns: render model + stdout + raw mode
   dmon's API   ┼─ TryWrite ─▶ inbound ───▶ drain → apply → (throttled) paint
   resize       ┘            Channel<Msg>
   dmon ◀── drains ── outbound Channel<OutEvent> ◀── Submitted/Accepted/KeyEvent/…
```

- **Inbound `Channel<Msg>` (MPSC):** input events *and* dmon's API commands share one FIFO → deterministic ordering, both reflected in the same frame.
- **Outbound `Channel<OutEvent>`:** dcli→dmon events; dmon drains on its **own** thread. The loop **never executes consumer code**, so a slow `Submitted` handler can't stall rendering.
- **Two dedicated long-running threads:** `InputReader` (blocking timed reads) and the render loop — not thread-pool tasks.
- **No locks:** every mutation is applied on the loop thread; the render model needs no synchronization.
- **API is fire-and-forget async:** `Append(line)` enqueues and returns *before* the paint. Value reads (terminal size, etc.) are served from a volatile snapshot, not a round-trip.

**Cadence — event-driven drain-coalesce-throttle** (not a fixed tick):

```
   loop until shutdown:
     await WaitToReadAsync(orUntil: nextPaintDeadline)   // idle when quiet
     while TryRead(out msg): apply(msg)                   // drain the whole batch
     if dirty && now ≥ nextPaintDeadline:
         renderFrame(); nextPaintDeadline = now + minFrameInterval
   finally: restore terminal      // ← Decision 9's guaranteed restore lives here
```

Properties: **idle-efficient** (sleeps on the channel when quiet); **burst-coalescing** (N streamed append-commands → one paint); **fps-capped** (`minFrameInterval`; terminals gain nothing past ~30–60fps). State is applied immediately on drain — only the *paint* is throttled — so input stays responsive and needs no separate priority lane. (`renderFrame()`'s escape-sequence mechanics are Open Question #1 below.)

- **Channel sizing:** inbound **unbounded** for v1 (apply is cheap, drains fast); bounded + backpressure to dmoncore is a later refinement.
- *Alternatives considered:* lock-protected shared state + repaint thread (contention, tearing — rejected); fixed-tick frame loop (burns CPU when idle — rejected); synchronous dcli→dmon callbacks (a blocking consumer handler stalls the loop — rejected for a library facing arbitrary consumers).

### 11. Public API surface: commands in, events out, dialogs awaited

The façade is a single `Terminal` object started in the background (matching the actor loop), with fire-and-forget command methods, an outbound `Events` stream, and `await`-able dialog methods.

**Keystone — dialogs are awaitable Tasks, messages internally.** `ShowSelectAsync(...)` posts an open-dialog command carrying a `TaskCompletionSource`; the loop drives the modal overlay (Decision 8); on submit/back/cancel it completes the TCS. So request/response reads as `await` on the outside while staying a pure message inside the actor loop (Decision 10) — and dmon's existing `await …Async()` call sites port nearly unchanged, while the blocking `Console.ReadKey` loops disappear.

```csharp
// Lifecycle — background loop (Decisions 9, 10)
await using Terminal term = await Terminal.StartAsync(new TerminalOptions {
    MaxFixedHeight = null,        // default 50% / min 8 (Decision 8)
    MinFrameIntervalMs = 16,      // throttle (Decision 10)
});                               // DisposeAsync stops the loop + restores the terminal

// Styled text (Decision 7) — Format is a [Flags] enum, NOT bool fields
[Flags] enum Format { None=0, Bold=1, Italic=2, Underline=4, Dim=8, Reverse=16, Strikethrough=32 }
readonly record struct Style(Color? Foreground = null, Color? Background = null, Format Format = Format.None);
record Segment(string Text, Style Style = default);
record Line(IReadOnlyList<Segment> Segments);
new LineBuilder().Dim("✻ ").Text("Thinking…").Build();

// Scrollback — fire-and-forget commands
term.Scrollback.Append(Line line);
term.Scrollback.AppendRule(Line? label);
ILiveBlock   tail = term.Scrollback.BeginLive();             // AppendText / SetContent / Commit
ICollapsible t    = term.Scrollback.BeginCollapsible(sum);   // AppendLine / Expand (one-way) / Commit

// Fixed region (Decision 8)
term.Input.Prompt = promptLine;  term.Input.ReadOnly = isThinking;
term.Status.Set(modelLine, modeLine);
term.Autocomplete.Show(items);   term.Autocomplete.Hide();

// Dialogs — async modals
enum DialogOutcome { Submitted, Back, Cancelled }            // ↔ dmon's WizardStepOutcome
readonly record struct DialogResult<T>(DialogOutcome Outcome, T Value);
await term.SelectAsync(req, ct);       // → DialogResult<int>
await term.MultiSelectAsync(req, ct);  // → DialogResult<int[]>   (was a dmon TODO)
await term.InputAsync(req, ct);        // → DialogResult<string>  (secret / default / validate)
await term.ChoiceAsync(req, ct);       // → DialogResult<int>     (yes/no, tool-confirm)

// Outbound events (Decision 10; dmon drains on its own thread)
ChannelReader<TerminalEvent> Events { get; }
//   InputSubmitted(string) · InputChanged(string) · KeyPressed(KeyEvent) · Resized(c,r)
```

**Decisions within:**
- **Dialogs awaitable** (TCS-bundled command) — *not* fire-and-forget + a later `DialogSubmitted` event, which would shred every wizard step into a state-machine fragment.
- **Autocomplete via event round-trip:** `InputChanged` → dmon computes candidates → `Autocomplete.Show`. Rejected a registered `Func<string,items>` provider (it would run consumer code on the loop thread — banned by Decision 10; round-trip latency is negligible at typing speed).
- **Lifecycle = background loop + `StartAsync`/`DisposeAsync` + `Events` stream.** Rejected a blocking `RunAsync(handlers)` — background lets dmon's RPC reader and input-event consumer run independently.
- **`Style.Format` is a `[Flags]` enum,** not a row of `bool` fields — composable and idiomatic.
- **Markdown stays in dmon:** dcli ships `Segment`/`Line`/`Style`/`Format`/`LineBuilder`; `Markdown → Line[]` is dmon's (content/semantics, Decision 3). An optional `Dcli.Markdown` companion is possible later.
- **One overlay at a time** (Decision 8 invariant): opening a dialog while one is active is rejected/queued by the loop; a dialog's `CancellationToken` closes the overlay and returns `Cancelled`.

**Absorbs the current `Dmon.Terminal` layer** (validates the surface): `AppendToken`/`SettleTurn` → `BeginLive`/`SetContent`/`Commit`; `ConsolePicker` & `InlinePrompt.ChooseAsync` → `SelectAsync`; `ReadLineAsync(secret)` → `InputAsync`; `WizardRenderer` step → dmon maps a `WizardStep` to a dialog call and reads `DialogResult.Outcome`; `ToolConfirmPrompt` → `ChoiceAsync`; `SlashCommandParser` autocomplete → `InputChanged` + `Autocomplete.Show`; `MarkdownRenderer` → `Markdown → Line[]` in dmon; the `ConsoleEventHandler` god-object splits into an RPC→`Scrollback` translator and an `Events` consumer. Every changed call site is a net improvement — real scrollback, no blocking reads, no Spectre coupling, first-class multi-select.

### 12. Testability: a fakeable façade (tier A) + a public headless harness (tier B)

The public surface (Decision 11) must be **substitutable**, and dcli must ship a **terminal-free** way to drive and observe itself. This is a first-class constraint on the API, not an afterthought — because a renderer that can only be exercised against a real TTY is a renderer the consumer can't test.

**The motivating evidence (dmon's `Dmon.Terminal`):** exactly one source file there is unit-tested — `WizardEngine` — and *only* because its render boundary was hand-quarantined behind an injected `Func<WizardStep, Outcome>` delegate. Every console-coupled file (`TerminalRenderer`, `InlinePrompt`, `ConsolePicker`, `ToolConfirmPrompt`, …) is untested, for one reason: **Spectre.Console's static `AnsiConsole` cannot be substituted.** Even the wizard leaks — its success message goes straight to `AnsiConsole.MarkupLine`, invisible to the test. The rule we draw from this: *testability is the inverse of static coupling; never repeat the static-singleton sin.*

Two tiers, serving the two rungs of a consumer's test pyramid.

**Tier A — the façade is fakeable (lean, in the main package).** The `Terminal` of Decision 11 implements an interface `ITerminal` (with sub-surfaces `IScrollback` / `IInput` / `IStatus` / `IAutocomplete`); consumers depend on the interface and substitute their own fake. Requirements:

- **No static singletons** anywhere a consumer touches — the entire anti-Spectre lesson, encoded.
- The **event and dialog-result types are public and constructible in tests** — `KeyEvent`, `PasteEvent`, `ResizeEvent`, the `TerminalEvent.*` cases, `DialogResult<T>`, `DialogOutcome`. A test must be able to synthesize "the user pressed ↓ then Enter" or "the dialog returned `Submitted(2)`" with no real parser or loop. (Watch for internal-only constructors added for safety — they'd silently break this.)
- A fake **records the command side** (scrollback appends, status sets, which dialog was shown with what content), so those calls become assertable — closing the `AnsiConsole.MarkupLine` class of blind spot.

This makes the `WizardEngine` test pattern the **default** posture for every dmon controller, not a per-feature hand-roll: `await terminal.SelectAsync(req)` replaces the injected `renderStep`; the fake returns a scripted `DialogResult`. dmon already prefers writing its own three-line fakes — dcli's obligation is simply to *not prevent* them.

**Tier B — a public, documented headless harness, shipped as `Dcli.Testing`.** Run the *real* engine terminal-free and assert on rendered output. Guiding principle: **abstract the OS edges, share the core** — one render loop + layout (Decisions 9–10), swap only the I/O endpoints:

```
   PRODUCTION edge              TEST edge (Dcli.Testing)        the seam*
   ──────────────────────       ────────────────────────       ───────────────
   Posix/Win RawModeSession     no-op session                  raw-mode session
   tty timed byte reader        scripted feeder (bytes/keys)   input source
   ioctl size + SIGWINCH        injected size + Resize()       size / resize source
   stdout writer                in-memory capture              output sink
   system clock / cadence       virtual clock + Settle()       clock / cadence
              └────────── SAME loop · layout · overlays · scrollback ──────────┘
   (* exact interface names are spec/impl detail — they are the testable boundary,
      not new architecture; the load-bearing core is never mocked.)
```

- **`HeadlessTerminal`** — a real `ITerminal` wired to the test-double edges; driven by `Feed(bytes)` / `SendKey(KeyEvent)` / `Type(string)` / `Paste(string)` / `Resize(cols, rows)`.
- **Determinism — `await harness.SettleAsync()`** drains all pending inbound work and produces exactly **one** coalesced frame with no wall-clock time passing. A **virtual clock** backs the handful of cadence tests that *must* assert timing (the coalesce / throttle / idle properties of task 7.6) — which `Settle` deliberately bypasses.
- **Observation = a structured frame snapshot, not VT bytes.** `harness.Snapshot` exposes the *logical* frame — live-window visual rows as `{text, style}` runs, fixed-region layout, caret position, active overlay — which is stable and human-diffable (golden frames). Raw byte output is asserted only by a thin separate fidelity layer. This snapshot is exactly the artifact task 8.5 already requires for dcli's own golden-frame tests.

- **Why public, and why a separate package:** dcli must build this substrate to test *itself* (5.6 / 7.6 / 8.5 are all terminal-free). Promoting it to a supported, documented contract hands dmon integration-level confidence for free. A separate `Dcli.Testing` package keeps the production NuGet lean and lets the harness version independently — and it honours Decision 3: the snapshot is a **testing** contract that production code and dcli's own semantics never depend on.
- **Alternatives considered:** baking the harness into the main package (bloats the prod surface, couples release cadence — rejected); a second standalone "headless" engine (drifts from the real one — rejected; we swap edges, never the core); making raw VT bytes the primary assertion surface (escape-soup brittleness — kept only as the fidelity layer).
- **Trade-off:** the edge-interfaces and the snapshot type become a maintained surface (`Dcli.Testing`'s public contract). Accepted — it's the surface dcli's own tests need regardless, and the production API (Decision 11) is untouched.

In short: **tier A** = test your controller against a *faked* dcli (cheap, what dmon reaches for today); **tier B** = test the integrated render against a *real, terminal-free* dcli (the confidence layer, there when a rendering bug needs catching).

## Risks / Trade-offs

- **Collapsibles freeze at the horizon** → a block that scrolls out of the live window before expansion is frozen as its summary; `Ctrl+O` on history does nothing. *Mitigation:* β reprint-into-flow covers "I really want to see it"; the consumer's per-Turn policy expands proactively while content is still live. Accepted as a deliberate property of inline rendering.
- **Opening tall bottom UI permanently commits scrollback** → a large paste / open dropdown shrinks the live window, committing top lines that can't be un-committed when it closes. *Mitigation:* document as expected; on-brand for Decision 1.
- **Cursor accounting + display-width bugs** (smearing) → off-by-one visual-row math wrecks the frame. *Mitigation:* contain all width/wrap/`wcwidth` math inside `Render()`; enforce content-model ≠ visual-model; the bounded live window limits blast radius.
- **Interleaved writes corrupt the display** (async RPC + keystrokes) → *Mitigation:* a single render loop that is the **sole owner of stdout** and all UI state, fed by an inbound `Channel` (Decision 10).
- **Unbounded inbound channel under sustained flood** → if dmoncore streams faster than the loop drains, queued commands grow memory. *Mitigation:* `apply` is O(1)-cheap and paints are coalesced, so the queue drains far faster than it fills in practice; bounded channel + backpressure to dmoncore is the planned refinement if it bites.
- **Raw-mode keyboard input is a .NET soft spot** → now designed in Decision 9 (own VT-stream driver, WT-only floor). Residual risk: per-platform `termios` struct marshalling, and **guaranteed terminal restore** on crash/signal — if missed, a crash leaves the user's shell with no echo. *Mitigation:* `RawModeSession` as `IDisposable` **plus** a last-resort net — `PosixSignalRegistration` (SIGINT/SIGTERM/SIGQUIT/SIGCONT) and `AppDomain.ProcessExit`; the pure `VtInputParser` is unit-tested against byte fixtures.
- **Lossy key encoding under VT (accepted)** → Ctrl-letter collisions, Shift implicit in runes, Shift lost under Ctrl. *Mitigation:* none possible without the Kitty protocol (deferred); documented as terminal physics, not bugs (Decision 9).
- **Live window squeezed to zero** on tiny terminals → *Mitigation:* clamp and degrade gracefully; live window may be 0.
- **Headless harness drift / snapshot brittleness** (Decision 12) → a `Dcli.Testing` that diverges from the real loop, or golden snapshots that churn on incidental detail, erode trust in the tests. *Mitigation:* swap only the OS edges over the shared core (never a second engine); keep the snapshot minimal and logical (`{text, style}` runs + caret + overlay), with a pretty-printer for diffs; dcli's own tests (5.6 / 7.6 / 8.5) run on the same harness, so drift surfaces immediately.

## Open Questions

These are **mechanism, not architecture** — none can contradict the decisions above. Raw-mode input is settled in Decision 9; the render-loop topology and cadence in Decision 10; the public API surface in Decision 11; testability (fakeable façade + headless harness) in Decision 12. Remaining:

1. **Frame strategy — `renderFrame()` internals** — cadence/coalescing/throttle is decided (Decision 10); what's open is the *paint mechanism*: leaning synchronized-output fence (`ESC [ ? 2026 h … l`) + clear/repaint the bounded live window for v1, with a per-line diff reconciler as a later optimization for flicker on terminals that ignore sync-output.
2. **Resize + capability detection** — SIGWINCH via `PosixSignalRegistration` (POSIX) / buffer-size events or polling (Windows); plus truecolor, synchronized-output, and unicode-width detection and fallbacks. Resize is delivered as an inbound message (Decision 10); what's open is detection + the reflow/repaint mechanics (intersects with #1).

(Over-budget priority resolved in Decision 8; render-loop shape in Decision 10; public API surface in Decision 11.)
