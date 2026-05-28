# dcli.testing

**Headless test harness for the [dcli](https://www.nuget.org/packages/dcli) inline terminal-rendering library.**

`dcli.testing` lets you drive the real dcli render engine — input parser, fixed-region composer,
scrollback model, overlays, virtual clock — against in-memory fakes instead of a real TTY.
No PTY, no raw mode, no ANSI pollution in your test output.

## Quick start

```csharp
using Dcli;
using Dcli.Testing;
using Xunit;

public class MyFeatureTests
{
    [Fact]
    public async Task StatusLineAppearsInSnapshot()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        harness.Terminal.Status.SetRows([new Line([new Segment("Ready")])]);
        await harness.SettleAsync();

        FrameSnapshot snap = harness.Snapshot;
        string pretty = FrameSnapshotPrinter.PrettyPrint(snap);

        Assert.Contains("Ready", pretty);
    }
}
```

## Key types

| Type | Description |
|------|-------------|
| `HeadlessTerminal` | Entry point. `StartAsync()` builds a live harness; `SettleAsync()` drains pending work and waits for a coalesced frame. |
| `HeadlessTerminalOptions` | Configures initial size, minimum frame interval, and virtual clock. |
| `VirtualClock` | Deterministic clock; `Advance(span)` advances time without wall-clock delay, triggering throttled paints. |
| `FrameSnapshot` | Immutable capture of `LiveWindowRows`, `FixedRegionRows`, caret, size, newly-committed rows, and overlay state. |
| `FrameSnapshotPrinter` | `PrettyPrint(snapshot)` — ASCII-bordered, style-stripped frame string, stable for golden-frame assertions. |

## Scripting methods

| Method | What it does |
|--------|--------------|
| `SendKey(KeyEvent)` | Injects a named or character key directly into the render loop. |
| `Type(string)` | Types each rune as a `Char` key event. For named keys (Enter, Tab, arrows) use `SendKey`. |
| `Paste(string)` | Posts a bracketed-paste event. |
| `Feed(ReadOnlySpan<byte>)` | Feeds raw bytes through the real VT parser on the InputReader thread. |
| `Resize(cols, rows)` | Triggers a resize event via the same path as POSIX SIGWINCH. |

## What's NOT here

This package contains only the headless harness. The production dcli rendering engine,
public API (`ITerminal`, `Scrollback`, `Input`, `Status`, `Autocomplete`), and event model
live in the `dcli` package. Add both packages to test projects:

```xml
<PackageReference Include="dcli" Version="0.1.0-rc.1" />
<PackageReference Include="dcli.testing" Version="0.1.0-rc.1" />
```
