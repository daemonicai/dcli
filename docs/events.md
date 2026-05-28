# Input & events

dcli is the sole owner of stdin and the render thread, so everything the user does reaches you as a
**message on a channel**. You drain `terminal.Events` on your own loop; dcli never calls into your
code. This page covers the event stream, how keys are encoded, and the sharp edges.

## The event channel

`terminal.Events` is a `ChannelReader<TerminalEvent>`. Drain it however you like:

```csharp
await foreach (TerminalEvent evt in terminal.Events.ReadAllAsync(cancellationToken))
{
    // handle evt
}
```

A consumer that never reads simply accumulates events; it does **not** stall the render loop. Drain
on a dedicated task if your handling is slow.

## TerminalEvent — what comes out

[`TerminalEvent`](api-reference.md#terminalevent) is a closed set of records. Pattern-match them:

| Event | Meaning |
| --- | --- |
| `InputSubmitted(string Text)` | User pressed Enter. `Text` is the line as submitted. |
| `InputChanged(string Text)` | The input buffer changed (user edit). `Text` is the new buffer. Drives autocomplete. |
| `KeyPressed(KeyEvent Key)` | A key the editor and overlays didn't consume, forwarded to you for app-level shortcuts. |
| `Resized(int Columns, int Rows)` | The terminal was resized. |

```csharp
switch (evt)
{
    case InputSubmitted(var text):     Submit(text); terminal.Input.Clear(); break;
    case InputChanged(var text):       terminal.Autocomplete.Show(Complete(text)); break;
    case Resized(var cols, var rows):  Reflow(cols, rows); break;
    case KeyPressed(var key):          HandleShortcut(key); break;
}
```

Only **unconsumed** keys arrive as `KeyPressed`. While the editor has focus it consumes printable
characters, Backspace, arrows, etc.; an open dialog or dropdown consumes its navigation keys first.
What's left over — function keys, Ctrl-chords you haven't bound, etc. — falls through to you.

> `Input.SetText` / `Input.Clear` are programmatic and do **not** emit `InputChanged` (see
> [the fixed region](fixed-region.md#driving-input-programmatically)) — so setting text yourself
> won't trigger your autocomplete handler.

## KeyEvent — how keys are encoded

A [`KeyEvent`](api-reference.md#keyevent) is a `KeyCode` plus `Modifiers`.

```csharp
public sealed record KeyEvent(KeyCode Code, Modifiers Modifiers);
```

### KeyCode — a printable scalar or a named key

[`KeyCode`](api-reference.md#keycode) is a discriminated value:

- **`UnicodeScalar`** — a printable character (or a Ctrl-modified letter). Read `RuneValue`.
- **`Named`** — a non-printable key from [`NamedKey`](api-reference.md#namedkey) (arrows, F-keys,
  Home/End, Enter, Tab, Escape, Delete, …). Read `NamedValue`.

```csharp
void HandleShortcut(KeyEvent key)
{
    switch (key.Code.Kind)
    {
        case KeyCode.KeyCodeKind.Named when key.Code.NamedValue == NamedKey.F5:
            Refresh();
            break;

        case KeyCode.KeyCodeKind.UnicodeScalar
            when key.Modifiers == Modifiers.Ctrl && key.Code.RuneValue == new Rune('r'):
            ReverseSearch();
            break;
    }
}
```

Reading the wrong accessor for the stored kind throws `InvalidOperationException`, so always check
`Kind` first (or match on it).

### Modifiers

[`Modifiers`](api-reference.md#modifiers) is a `[Flags]` enum: `None`, `Ctrl`, `Alt`, `Shift`.

```csharp
if (key.Modifiers.HasFlag(Modifiers.Alt)) { /* ... */ }
```

### Key encoding caveats (terminal physics, not bugs)

VT terminals lose information that a richer protocol (e.g. Kitty) would preserve. These are
documented limits, not defects:

- **Ctrl-letter collisions.** `Ctrl+I` is indistinguishable from Tab, `Ctrl+M` from Enter,
  `Ctrl+H` from Backspace — the terminal sends the same bytes. dcli reports them as the named key.
- **Shift is implicit on printables.** `Shift+a` arrives as the rune `'A'`, not as `'a'` + a Shift
  flag. `Shift` only appears on **named** keys (e.g. `Shift+Tab` → `BackTab`, Shift+Arrow).
- **Shift is dropped under Ctrl.** The terminal collapses `Ctrl+Shift+x` on the wire, so dcli never
  reports `Ctrl | Shift` together.

## Ctrl+C and signals

In raw mode, terminal signal generation is off — so **Ctrl+C does not raise SIGINT**. It arrives as
an ordinary key event: `KeyEvent(KeyCode.FromRune('c'), Modifiers.Ctrl)`. dcli "eats" it and hands
it to you; *you* decide whether it means clear-input, interrupt the current operation, or exit.

```csharp
case KeyPressed(var key)
    when key.Modifiers == Modifiers.Ctrl
      && key.Code.Kind == KeyCode.KeyCodeKind.UnicodeScalar
      && key.Code.RuneValue == new Rune('c'):
    return; // exit; `await using` restores the terminal
```

External signals are different: a real `SIGTERM`/`SIGINT`/`SIGQUIT` from `kill`, or `ProcessExit`,
is caught by dcli's restore coordinator, which **always restores the terminal** before the process
dies — even on a crash. See [Architecture](architecture.md#guaranteed-terminal-restore).

## Paste

Pasted text arrives via **bracketed paste** as a single unit, not as a flurry of key events. The
input editor inserts it literally. If you read raw `InputEvent`s (advanced / testing), a paste is a
[`PasteEvent(string Text)`](api-reference.md#pasteevent) with the full UTF-8-decoded text; feed-call
boundaries inside the paste body are transparent.

## Resize

A terminal resize (POSIX `SIGWINCH` or a Windows buffer-size event) is delivered to you as
`Resized(Columns, Rows)`. dcli also reflows the live window and fixed region itself; the event lets
*you* react (e.g. recompute a status line or re-wrap your own content). The current size is also
available synchronously via `terminal.GetTerminalSize()`.

```csharp
case Resized(var cols, var rows):
    terminal.Status.SetRows(Line.FromText($"{cols}×{rows}"));
    break;
```

## InputEvent vs. TerminalEvent

Two families exist, at different layers:

- [`InputEvent`](api-reference.md#inputevent) (`KeyEvent` / `PasteEvent` / `ResizeEvent`) is the
  **low-level** VT-pipeline vocabulary. You mostly meet it via `KeyPressed.Key` and in the
  [headless test harness](testing.md), which scripts `KeyEvent`s and `PasteEvent`s directly.
- [`TerminalEvent`](api-reference.md#terminalevent) (`InputSubmitted` / `InputChanged` /
  `KeyPressed` / `Resized`) is the **high-level** outbound stream you actually consume in an app.

## See also

- [The fixed region](fixed-region.md) — what consumes keys before they reach you.
- [Dialogs](dialogs.md) — modal flows that consume their own navigation keys.
- [API reference](api-reference.md#events--input).
