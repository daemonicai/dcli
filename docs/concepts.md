# Core concepts

dcli renders a CLI the way Claude Code does: your program's output scrolls into the terminal's
**real history**, and only a small region at the bottom is interactive and repainted. This page
explains the handful of ideas that make the rest of the API obvious.

## Inline rendering, not a full-screen TUI

Most TUI frameworks (ncurses, Spectre.Console's live displays, Terminal.Gui) switch the terminal
to its **alternate screen** — a separate buffer that wipes on exit and leaves nothing in
scrollback. That's right for a dashboard or a text editor. It's wrong for a conversational CLI,
where the user expects to scroll up through everything that happened, copy a line from twenty
turns ago, and have their shell prompt return cleanly when the program exits.

dcli stays on the **main screen**. Styled output is *appended* to the terminal's native
scrollback, just like `printf` would, and the terminal's own scrollback buffer keeps the history.
dcli only ever rewrites a bounded region near the cursor.

```
   ── native terminal scrollback (frozen, terminal-owned) ──
   line written 3 minutes ago
   a streamed model response that has since committed
   ...
   ── live window (re-renderable) ──
   the block currently streaming
   ── fixed region (pinned, re-rendered every frame) ──
   > the user's input line_
   status: connected · 12 turns
```

## The commit horizon

Because dcli only repaints a bounded region, there has to be a line below which content is
**frozen** — written once, never touched again, and handed to the terminal forever. That boundary
is the **commit horizon**.

- Below the horizon is the **live window**: at most `rows − fixedRegionHeight` rows that dcli can
  still re-render (e.g. a block that's still streaming).
- Above the horizon, everything is **committed**: frozen text in the terminal's scrollback.

The horizon only ever moves **down**. As new content arrives or the fixed region grows, the
top lines of the live window scroll off and commit. This is a one-way street by design — you
cannot un-commit a line, because it's no longer dcli's to rewrite.

A practical consequence: opening a tall dialog or a big dropdown shrinks the live window, which
**commits** whatever scrolls above it. That committed content stays committed after the dialog
closes. This is expected and on-brand for inline rendering — see
[Architecture](architecture.md) for the full trade-off.

## The two zones

dcli renders exactly two zones:

1. **Scrollback** — the append-mostly content stream. You add lines, streaming "live" blocks, and
   one-way collapsibles. See [Scrollback](scrollback.md).
2. **The fixed region** — the pinned bottom stack: the input editor, the status bar, and two
   mutually-exclusive overlays (a **dialog** slot above the input, an **autocomplete** dropdown
   below). See [The fixed region](fixed-region.md).

Both zones render the same styled-text primitive — `Segment` / `Line` / `Style`. There is no
markup language to parse; you build styled lines programmatically. See [Styled text](styled-text.md).

## The single-writer render loop

All UI state and the sole handle to stdout live on **one** thread — an actor-model render loop.
This is what makes interleaved async writes safe: a background task streaming RPC results and a
user hammering the keyboard can't corrupt the display, because neither touches stdout directly.

You interact with the loop through two channels:

- **Commands in (fire-and-forget).** `Scrollback.Append`, `Status.SetRows`, `Input.SetText`,
  `Autocomplete.Show`, and friends *post* a command and return immediately. The loop applies it
  on its own thread and paints when it next coalesces a frame. Your call does not block on a paint.
- **Events out (a channel you drain).** Key presses the editor didn't consume, input submissions,
  buffer changes, and resizes flow out on `Terminal.Events`, a `ChannelReader<TerminalEvent>`. You
  read them on your own loop. **dcli never runs your code on its render thread.**

Awaitable dialogs bridge the two: `await terminal.SelectAsync(...)` posts an open-dialog command
carrying a `TaskCompletionSource`, the loop drives the modal, and on submit/cancel it completes
your task. It reads like a request/response `await` on the outside while staying a pure message on
the inside.

Frames are **coalesced and throttled**: many commands between paints collapse into a single frame,
capped at `TerminalOptions.MinFrameIntervalMs` (default 16 ms ≈ 60 fps).

## Mechanics vs. semantics — the dcli/consumer boundary

This is the most important boundary to internalize:

> **dcli owns rendering and widget mechanics. Your application owns data and semantics.**

dcli decodes a keystroke, routes it down the overlay→input chain, and repaints. It does **not**
know what your commands are, what a "submit" should do, or how to compute completions. Concretely:

- **Ctrl+C is eaten** and surfaced to you as a `KeyPressed` event. dcli takes no action — *you*
  decide whether it means clear-input, interrupt, or exit. (In raw mode it does not raise SIGINT.)
- **Autocomplete is a round-trip.** dcli emits `InputChanged`; you compute candidates from the
  text and call `Autocomplete.Show(...)`. dcli never calls back into your code to ask for them.
- **Markdown, protocols, "turns"** — all yours. dcli ships `Segment`/`Line`/`Style`; converting
  Markdown (or JSON, or an RPC stream) into lines is your job.

Keeping this line clean is why the same library can back very different CLIs.

## Lifecycle and the restore guarantee

`Terminal.StartAsync` enters raw mode and starts the loop. Disposal stops the loop, joins its
threads, and restores the terminal. The restore is **guaranteed on every exit path**:

- normal `DisposeAsync` (use `await using`),
- an unhandled exception on the loop thread (the loop's `finally` restores),
- external signals and `ProcessExit` (a last-resort coordinator restores).

All paths converge on a single idempotent restore, so a crash can't leave the user's shell with
no echo. See [Getting started](getting-started.md) for the lifecycle in code.

## Where to go next

- [Getting started](getting-started.md) — install and run something.
- [Styled text](styled-text.md) — build the lines you'll append everywhere.
- [Architecture](architecture.md) — the decisions behind all of the above.
