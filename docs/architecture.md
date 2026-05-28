# Architecture

This page explains *why* dcli is shaped the way it is. The decisions below are binding and
interlocking — each one falls out of the first. The authoritative record (with alternatives
considered) lives in the OpenSpec archive under
[`openspec/changes/archive/2026-05-28-core-rendering-architecture/design.md`](../openspec/changes/archive/2026-05-28-core-rendering-architecture/design.md);
this is the readable distillation.

## 1. Inline rendering with native scrollback — "the Fork"

dcli renders on the terminal's **main screen**, appending styled output into the native scrollback,
and only ever re-renders a bounded region near the bottom. It deliberately does **not** use the
alternate screen.

**Why.** The target UX is a conversational CLI (Claude-Code-style), not a dashboard. Users expect
to scroll back through real history, select and copy any prior line, and get a clean shell prompt
on exit. The alternate screen — the standard TUI choice — throws all of that away. Choosing the
main screen is the fork in the road from which everything else follows.

This is the single most important thing to understand about dcli: **it is not a full-screen TUI
framework, and its constraints are features, not limitations.**

## 2. The commit horizon / live window

Since only a bounded region is re-rendered, there is a line below which content is **committed** —
frozen, write-once, terminal-owned. Above that line is the **live window** (`≤ rows −
fixedRegionHeight`), which dcli can still repaint.

**Why.** It falls directly out of Decision 1, and it bounds memory and per-frame work to a single
screen regardless of how much has scrolled past. `Render()` is only ever called on live blocks;
frozen blocks were painted once and dropped; a resize reflows only the live window.

**Consequence.** The horizon moves only downward. Opening tall bottom UI (a big dropdown, a long
dialog, a large paste) shrinks the live window and **commits** whatever scrolls above it — and that
content stays committed when the UI closes. Documented as expected behavior, on-brand for inline
rendering. See [the commit horizon](concepts.md#the-commit-horizon).

## 3. dcli = mechanics + rendering; consumer = data + semantics

dcli owns the render model, the widgets, the input driver, and key routing. The consumer owns the
data, the protocol (RPC/JSONL), completion candidates, Markdown→lines conversion, and the notion of
a "turn".

**Why.** A clean boundary is what lets one library back very different CLIs. Concretely: Ctrl+C is
surfaced as an event rather than acted upon; autocomplete is a round-trip the consumer drives;
Markdown rendering stays in the consumer. The protocol never leaks into the library. See
[mechanics vs. semantics](concepts.md#mechanics-vs-semantics--the-dcliconsumer-boundary).

## 4. Platform: C# / .NET (`net10.0`), modern VT only

Shipped as a NuGet package targeting `net10.0`. The terminal floor is a modern VT-capable emulator
(Windows Terminal / Win10 1809+ / xterm-class); legacy conhost is unsupported.

**Why.** A modern VT floor lets the input and rendering layers assume a single, well-understood
escape-sequence vocabulary instead of carrying compatibility shims for terminals nobody targets.

## 5. Scrollback data model: flat list, content ≠ visual

Scrollback is a flat list of line-objects (`Segment → Line → {TextBlock | Collapsible}`). The
**content model** (logical lines) is distinct from the **visual rows** (after cell-wrapping to the
current width).

**Why.** Separating content from presentation means a resize reflows by re-wrapping the live
window's content, with no need to remember per-row pixel state. Frozen content needs no model at
all — it already lives in the terminal.

## 6. One-way collapsibles

A collapsible expands **once** and never re-collapses; its hidden lines are an immutable snapshot
taken at construction.

**Why.** Inline rendering means height can only grow — there's no way to push committed content back
*up* the terminal. One-way ⇒ monotonic height ⇒ the model never has to do the impossible, and all
toggle/remeasure state disappears. If a collapsible has already scrolled past the horizon when
expanded, its hidden lines are reprinted into the native scrollback flow instead. See
[collapsibles](scrollback.md#collapsibles--one-way-expandable-blocks).

## 7. Styling: one programmatic primitive, shared

A single `Segment`/`Style` primitive (with a `[Flags] Format` enum and a `LineBuilder`) is shared by
both zones. There is **no markup parser**.

**Why.** A markup language would be a second, lossy way to express styling and an injection surface.
Programmatic construction is type-safe and unambiguous; Markdown (or any other source format) →
`Line[]` is the consumer's job (Decision 3). See [styled text](styled-text.md).

## 8. Fixed region: component stack, intercept routing, height budget

The pinned bottom region is a stack — input editor + status line — with two **mutually-exclusive**
overlays: a dialog slot above the input and an autocomplete dropdown below. Keys flow down an
**intercept chain** (`bool HandleKey(KeyEvent)`), active overlay first, falling through to the input
editor. The whole region is bounded by a `MaxHeight` budget (default ≈ 50%, min 8 rows); the status
bar is never truncated.

**Why.** One overlay slot keeps the visual model simple and the routing unambiguous. A dialog is a
*slot that hosts a component* — not a wizard engine; a multi-step wizard is the consumer swapping
the slot's contents page by page (Decision 3). See [the fixed region](fixed-region.md).

## 9. Raw-mode input: one VT byte-stream, one parser, built in-house

dcli runs its own raw-mode session (its own `termios` / Win32-console shims — no `Console.ReadKey`),
a single `VtInputParser` state machine, and a School-A `KeyEvent` contract (`Char(Rune) | Named` +
modifier flags), with bracketed paste and eaten Ctrl+C.

**Why.** Raw-mode keyboard input is a .NET soft spot; owning the byte stream and parser is the only
way to get correct, testable key decoding. The parser is pure and unit-tested against byte fixtures.

**Accepted limits (terminal physics, not bugs):** Ctrl-letter collisions, Shift implicit in
printable runes, Shift lost under Ctrl. None are fixable without a richer protocol (Kitty), which is
deferred. See [key encoding caveats](events.md#key-encoding-caveats-terminal-physics-not-bugs).

## 10. Render loop: actor model, event-driven, dual channels

A single thread owns **all** UI state, stdout, and the raw-mode session. Producers post to an
inbound `Channel`; dcli→consumer events flow out on a separate `Channel`. Frames are
drain-coalesce-throttled; the public command API is fire-and-forget.

**Why.** Interleaved writes (async RPC + keystrokes) are the classic way to corrupt a terminal
display. A single owner of stdout makes corruption structurally impossible — there is exactly one
writer. The consumer never runs code on the loop thread, so a slow consumer can't stall rendering.
See [the render loop](concepts.md#the-single-writer-render-loop).

## 11. Public API: commands in, events out, dialogs awaited

The surface is a background `Terminal` façade: fire-and-forget scrollback/status/input commands, an
outbound `Events` stream, and `await`-able modal dialogs.

**The keystone — dialogs are awaitable Tasks, messages internally.** `SelectAsync(...)` posts an
open-dialog command carrying a `TaskCompletionSource`; the loop drives the modal overlay and
completes the TCS on submit/back/cancel. So request/response reads as `await` on the outside while
staying a pure message inside the actor loop. See [dialogs](dialogs.md).

## 12. Testability: a fakeable façade + a public headless harness

The façade is an interface (`ITerminal`) with publicly-constructible event/result types, so
consumers can fake it (tier A). A companion `Dcli.Testing` package ships a headless harness —
`HeadlessTerminal` + scripted input + deterministic `SettleAsync` + a structured `FrameSnapshot` —
for terminal-free integration tests (tier B).

**Why.** A renderer that can only be exercised against a real TTY is one the consumer can't test —
so testability is a first-class API constraint, not an afterthought. The harness swaps only the OS
edges over the *shared* core (never a second engine), so it can't drift from production. It's the
same substrate dcli uses to test itself. See [testing](testing.md).

## Guaranteed terminal restore

The gravest failure mode for a raw-mode program is crashing and leaving the user's shell with no
echo. dcli guarantees restore on **every** exit path, all converging on a single idempotent restore
so no double-restore can occur:

1. **Normal shutdown** — `DisposeAsync` stops the loop, joins threads, restores.
2. **Loop-thread crash** — the loop's `finally` restores before the thread dies (`DisposeAsync`
   then rethrows the captured fault so the caller learns of it).
3. **External signals / `ProcessExit`** — a `RestoreCoordinator` (wired to `SIGINT`/`SIGTERM`/
   `SIGQUIT`/`SIGCONT` and `AppDomain.ProcessExit`) halts the loop and restores as a last resort.

The signal path routes through the loop thread (single-writer discipline holds even during teardown:
ANSI restore → termios restore → signal), so even the emergency path doesn't violate Decision 10.

## Open questions (mechanism, not architecture)

The decisions above are settled. What remains open is *mechanism* that can't contradict them:

- **Frame paint strategy** — the v1 lean is a synchronized-output fence (`ESC [ ? 2026 h … l`) plus
  clear/repaint of the bounded live window, with a per-line diff reconciler as a later optimization
  for terminals that ignore sync-output.
- **Resize + capability detection mechanics** — `SIGWINCH`/buffer-size delivery is settled (it's an
  inbound message); the reflow/repaint mechanics and truecolor/sync-output/unicode-width detection
  and fallbacks are the open part.

## See also

- [Core concepts](concepts.md) — the same ideas, framed for first-time readers.
- The OpenSpec archive — full proposals, specs, and per-change `DEVLOG.md` narratives.
