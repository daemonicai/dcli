# Testing

A renderer you can only exercise against a real TTY is a renderer you can't test. dcli is built so
that terminal UIs are testable two ways, neither of which needs a terminal:

- **Tier A — fake the façade.** Your code depends on [`ITerminal`](api-reference.md#iterminal); in a
  unit test you substitute a hand-written fake. Best for testing *your controller logic*.
- **Tier B — the headless harness.** The `Dcli.Testing` package ships
  [`HeadlessTerminal`](#headlessterminal), which runs the **real** render engine, input parser, and
  fixed-region composer against in-memory OS edges. Best for *integration* tests that assert on what
  actually renders. It's the same harness dcli uses to test itself.

```bash
dotnet add package Dcli.Testing --prerelease
```

## Tier A — faking `ITerminal`

`ITerminal` has no static state and exposes every surface and dialog method, so a fake is
straightforward. Because all the event/result types are publicly constructible, you can synthesize
events and dialog results without a terminal.

```csharp
public sealed class FakeTerminal : ITerminal
{
    private readonly Channel<TerminalEvent> _events = Channel.CreateUnbounded<TerminalEvent>();
    public List<Line> Appended { get; } = [];

    public ChannelReader<TerminalEvent> Events => _events.Reader;
    public (int Columns, int Rows) GetTerminalSize() => (80, 24);

    // Drive your controller by writing events the way the real terminal would:
    public void EmitSubmit(string text) => _events.Writer.TryWrite(new InputSubmitted(text));

    // Stub the dialogs to return canned answers:
    public Task<DialogResult<int>> SelectAsync(SelectRequest req, CancellationToken ct = default)
        => Task.FromResult(new DialogResult<int>(DialogOutcome.Submitted, 0));
    // ... implement the remaining members (Scrollback/Input/Status/Autocomplete + the other dialogs)
}
```

Then test your controller in isolation:

```csharp
var terminal = new FakeTerminal();
var controller = new ChatController(terminal);
var run = controller.RunAsync(CancellationToken.None);

terminal.EmitSubmit("hello");
// assert the controller did what you expect with the fake's recorded calls
```

This is the right tool when the thing under test is *your* logic and you don't care about pixel-level
rendering.

## Tier B — `HeadlessTerminal`

When you want to assert on what dcli actually paints — that a key edited the buffer, that a dialog
opened, that content committed on resize — drive the real engine through `HeadlessTerminal`.

```csharp
using Dcli;
using Dcli.Testing;

await using HeadlessTerminal h = await HeadlessTerminal.StartAsync(
    new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

// Drive the real ITerminal exactly as production code does:
h.Terminal.Scrollback.Append("hello");
h.Terminal.Status.SetRows(Line.FromText("ready"));

await h.SettleAsync();                 // drain pending work, wait for one coalesced frame
FrameSnapshot frame = h.Snapshot;      // immutable snapshot of what was painted

Assert.Contains(frame.LiveWindowRows, line => line == Line.FromText("hello"));
```

### Scripting input

`HeadlessTerminal` posts through the same channels production uses — it never pokes model state
directly.

| Method | Use |
| --- | --- |
| `Type(string)` | Types text rune-by-rune as `KeyCode.FromRune` events (printables only). |
| `SendKey(KeyEvent)` | Injects one key event — use for named keys (Enter, Tab, arrows, …). |
| `Paste(string)` | Injects a bracketed-paste block. |
| `Feed(ReadOnlySpan<byte>)` | Feeds **raw bytes** through the real VT parser (test the parser itself). |
| `Resize(int columns, int rows)` | Fires a resize through the same path as POSIX `SIGWINCH`. |

```csharp
h.Type("/hel");
h.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
await h.SettleAsync();
```

### Settling deterministically

`SettleAsync()` drains all pending inbound work and waits for exactly one coalesced frame (when the
model is dirty), **without advancing wall-clock time**. Call it after scripting input, before
reading `Snapshot`.

- Event-level scripting (`Type` / `SendKey` / `Paste` / `Resize`) posts directly to the loop, so a
  **single** `SettleAsync` makes it visible.
- `Feed` enqueues raw bytes the independent reader thread decodes asynchronously — you may need
  **two** `SettleAsync` calls after a large feed: one to let the reader drain the bytes, one to let
  the resulting events reach the loop.

### Asserting on a dialog

Dialogs are awaitable, so kick one off, settle, drive its keys, and await the result:

```csharp
Task<DialogResult<int>> pending = h.Terminal.SelectAsync(
    new SelectRequest([Line.FromText("A"), Line.FromText("B"), Line.FromText("C")], "pick"));

await h.SettleAsync();
Assert.Equal(OverlayKind.Dialog, h.Snapshot.Overlay.Kind);

h.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));   // move to "B"
h.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));  // submit
await h.SettleAsync();

DialogResult<int> result = await pending;
Assert.Equal(DialogOutcome.Submitted, result.Outcome);
Assert.Equal(1, result.Value);
```

### Cadence tests with the virtual clock

For tests that exercise throttling/coalescing, set a non-zero `MinFrameInterval` and advance the
[`VirtualClock`](api-reference.md#virtualclock) yourself — no real sleeps:

```csharp
var clock = new VirtualClock();
await using var h = await HeadlessTerminal.StartAsync(new HeadlessTerminalOptions
{
    MinFrameInterval = TimeSpan.FromMilliseconds(16),
    Clock = clock,
});

h.Terminal.Scrollback.Append("a");
clock.Advance(TimeSpan.FromMilliseconds(16)); // release the throttled paint
await h.SettleAsync();
```

## Frame snapshots

[`FrameSnapshot`](api-reference.md#framesnapshot) is the immutable logical frame from the most
recent paint. Key fields:

| Field | Meaning |
| --- | --- |
| `LiveWindowRows` | The re-renderable rows above the fixed region. |
| `FixedRegionRows` | Input editor, status, and any overlay. |
| `NewlyCommittedRows` | Rows that crossed the commit horizon on this frame. |
| `Caret` | `(Row, Col)` or `null` (hidden / modal dialog active). |
| `IsCursorVisible` | Hardware cursor visibility at end of frame. |
| `Size` | `(Columns, Rows)` at snapshot time. |
| `Overlay` | An [`OverlayDescriptor`](api-reference.md#overlaydescriptor): `Kind` (`None`/`Autocomplete`/`Dialog`/`Input`), `SelectedIndex`, `VisibleRowCount`, `InputText`, `IsSecret`. |

Before the first paint, `Snapshot` returns an empty frame (size `(0,0)`, empty lists) — it never
throws.

### Golden-frame assertions

Assert on *structure*, not raw ANSI. `FrameSnapshotPrinter.PrettyPrint` renders a stable,
style-stripped, ASCII-bordered view ideal for golden strings and readable diffs:

```csharp
string rendered = FrameSnapshotPrinter.PrettyPrint(h.Snapshot);
// Compare against a stored golden string, or assert on substrings.
```

The format lists any newly-committed rows first (`[committed N]`), then a box with numbered
live-window rows, a `--- horizon ---` separator, the fixed-region rows, and a summary line for
caret + overlay state. Style information is intentionally stripped so golden strings stay readable.

## See also

- [Getting started: depend on the interface](getting-started.md#depend-on-the-interface-not-the-class)
- [Input & events](events.md) — the event vocabulary you'll script and assert on.
- [API reference: testing harness](api-reference.md#testing-harness-dclitesting)
