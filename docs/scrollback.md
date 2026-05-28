# Scrollback

The scrollback is the append-mostly content stream — everything *above* the pinned fixed region.
You reach it through `terminal.Scrollback`, an [`IScrollback`](api-reference.md#iscrollback). It
offers three things: append a line, start a **live** (mutable, streaming) block, and start a
one-way **collapsible** block.

All methods are **fire-and-forget**: they post a command and return before the loop paints. See
[the render loop](concepts.md#the-single-writer-render-loop).

## Appending lines

```csharp
terminal.Scrollback.Append("a plain line");                    // string overload
terminal.Scrollback.Append(Line.FromText("styled", someStyle)); // Line overload
terminal.Scrollback.Append(new LineBuilder().Bold("done").Build());
```

`Append(string)` is exactly `Append(Line.FromText(text))`. Each call adds one logical line; it
will be cell-wrapped to the terminal width at paint time.

Appended lines enter the [live window](concepts.md#the-commit-horizon) and scroll upward as more
content arrives. Once a line passes the commit horizon it's frozen into the terminal's native
scrollback — you can't rewrite it after that, which is exactly why a plain `Append` is the right
tool for finished output.

## Live blocks — streaming, mutable content

When you're streaming a response token-by-token, or building a block whose final form you don't
know yet, use a **live block**. It stays mutable as long as it remains in the live window.

```csharp
ILiveBlock block = terminal.Scrollback.BeginLive();

foreach (string token in StreamTokensAsync())
    block.AppendText(token);   // grows the block, repainting as it goes

block.Commit();                // freeze it; it flows into native scrollback
```

[`ILiveBlock`](api-reference.md#iliveblock) has three methods:

| Method | Effect |
| --- | --- |
| `AppendText(string)` | Append to the block's accumulation buffer. Newlines split it into rows. No-op after `SetContent` or `Commit`. |
| `SetContent(IReadOnlyList<Line>)` | Replace the whole block wholesale with styled lines. After this, `AppendText` is a no-op. |
| `Commit()` | Freeze the block and remove it from the live window on the next paint; its final rows flow into scrollback. |

`AppendText` is for raw streaming (think model tokens). `SetContent` is for "I've now parsed the
buffer into proper styled lines, swap them in" — e.g. streaming raw Markdown text, then replacing
it with rendered lines once a block completes:

```csharp
ILiveBlock block = terminal.Scrollback.BeginLive();

await foreach (string chunk in modelStream)
    block.AppendText(chunk);                 // show raw text as it arrives

block.SetContent(RenderMarkdown(fullText));  // swap in the formatted version
block.Commit();
```

A typical loop has at most one live block open at a time. After `Commit()`, the handle is inert —
start a new block for the next streamed item.

## Collapsibles — one-way expandable blocks

A **collapsible** shows a one-line summary and hides a body that can be revealed once. Use it for
"thinking" traces, verbose logs, tool output — anything the user can optionally expand.

```csharp
ICollapsible thinking = terminal.Scrollback.BeginCollapsible(
    summary: new LineBuilder().Dim("▸ thinking (24 lines hidden)").Build(),
    hiddenLines: reasoningLines);   // IReadOnlyList<Line>, snapshotted now

// later, e.g. when the user presses a key you've mapped to "expand":
thinking.Expand();
```

[`ICollapsible`](api-reference.md#icollapsible) has a single method, `Expand()`:

- It is **one-way and idempotent** — expanded stays expanded; calling it again does nothing.
- The hidden lines are an **immutable snapshot taken at `BeginCollapsible` time**. You can't add to
  them afterward; build the full body first.

### Why one-way?

Inline rendering means a block's height can only grow, never shrink — there's no way to push
already-committed content back *up* the terminal. So collapsibles expand but never re-collapse.
This keeps height monotonic and the model simple. If a collapsible has already scrolled past the
commit horizon when you call `Expand()`, the hidden lines are reprinted into the native scrollback
flow instead of expanding in place (a no-op-looking call still does the right thing). See
[Architecture](architecture.md#one-way-collapsibles).

## What scrollback is *not*

- It's not a log you can rewrite. Committed lines are the terminal's; only the live window is
  mutable.
- It's not where interactive widgets go — input, status, dialogs, and dropdowns live in
  [the fixed region](fixed-region.md).
- It doesn't parse anything. You hand it `Line`s (or strings); converting Markdown/JSON/RPC into
  lines is your job ([mechanics vs. semantics](concepts.md#mechanics-vs-semantics--the-dcliconsumer-boundary)).

## See also

- [Styled text](styled-text.md) — building the lines you append.
- [Core concepts: the commit horizon](concepts.md#the-commit-horizon) — why "live" and "committed" differ.
- [API reference](api-reference.md#iscrollback).
