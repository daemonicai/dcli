# Getting started

This guide takes you from an empty project to a working interactive CLI.

## Requirements

- **.NET 10.0** or later (`net10.0`).
- A **modern VT-capable terminal**, attached to a real tty:
  - **macOS** — Terminal.app, iTerm2, Ghostty, WezTerm.
  - **Windows** — Windows Terminal, or any Win10 1809+ console host. Legacy `conhost.exe` is not supported.
  - **Linux** — xterm, `xterm-256color`, or any xterm-class emulator.

If stdout/stdin is redirected to a file or pipe, or `TERM=dumb`, or the platform isn't VT-capable,
`Terminal.StartAsync` throws [`TerminalNotSupportedException`](api-reference.md#terminalnotsupportedexception).

## Install

```bash
dotnet add package dcli --prerelease
```

The `--prerelease` flag is needed while the library is on a release-candidate line
(`0.2.0-rc.x`). For tests, also add the harness:

```bash
dotnet add package Dcli.Testing --prerelease
```

## Your first program

```csharp
using System.Text;
using Dcli;

await using Terminal terminal = await Terminal.StartAsync(new TerminalOptions());

terminal.Status.SetRows(Line.FromText("Type a message and press Enter. Ctrl+C to quit."));
terminal.Scrollback.Append("Welcome to the echo demo.");

await foreach (TerminalEvent evt in terminal.Events.ReadAllAsync())
{
    switch (evt)
    {
        case InputSubmitted(var text):
            terminal.Scrollback.Append(new LineBuilder().Dim("> ").Text(text).Build());
            terminal.Input.Clear();
            break;

        case KeyPressed(var key)
            when key.Modifiers == Modifiers.Ctrl
              && key.Code.Kind == KeyCode.KeyCodeKind.UnicodeScalar
              && key.Code.RuneValue == new Rune('c'):
            return; // `await using` restores the terminal on the way out
    }
}
```

Run it. Type, press Enter, watch lines flow into scrollback above the input. Press Ctrl+C to exit
— and notice your shell prompt comes back clean.

### What just happened

1. **`StartAsync`** entered raw mode and started the [render loop](concepts.md#the-single-writer-render-loop).
   `TerminalOptions` controls the fixed-region height cap and frame rate; the defaults are sensible.
2. **`Status.SetRows`** and **`Scrollback.Append`** posted fire-and-forget commands. They returned
   immediately; the loop painted them on its own thread.
3. **`Events.ReadAllAsync`** drained the outbound channel on *your* loop. `InputSubmitted` fires
   when the user presses Enter; `KeyPressed` carries keys the editor didn't consume.
4. **Ctrl+C** arrived as a key event, not a signal — in raw mode dcli eats it and hands it to you.
   See [Input & events](events.md#ctrlc-and-signals).
5. **`await using`** disposed the terminal at the end, restoring raw mode. See below.

## The lifecycle

```csharp
// 1. Start — enters raw mode, starts the loop. Throws TerminalNotSupportedException
//    if the environment can't do VT.
await using Terminal terminal = await Terminal.StartAsync(new TerminalOptions
{
    MaxFixedHeight = 12,     // cap the pinned region at 12 rows (null = ~50% of height, min 8)
    MinFrameIntervalMs = 16, // ≈ 60 fps ceiling
});

// 2. Drive it — post commands, await dialogs, drain events.

// 3. Dispose — `await using` calls DisposeAsync, which stops the loop, joins threads,
//    and restores the terminal. If the loop crashed, DisposeAsync rethrows the fault
//    (after restoring), so you learn about it.
```

The restore is guaranteed on **every** exit path — clean dispose, a crash on the loop thread, or
an OS signal/`ProcessExit`. You never need a `try/finally` to un-break the terminal; that's the
library's job. See [Architecture](architecture.md#guaranteed-terminal-restore).

> **Always use `await using` (or a `try/finally` with `DisposeAsync`).** Leaking a live `Terminal`
> leaves raw mode on.

## Depend on the interface, not the class

For anything beyond a `Program.cs` smoke test, type your code against
[`ITerminal`](api-reference.md#iterminal), not the concrete `Terminal`:

```csharp
public sealed class ChatController(ITerminal terminal)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (TerminalEvent evt in terminal.Events.ReadAllAsync(ct))
        {
            // ...
        }
    }
}
```

`ITerminal` exposes every surface and dialog method. Depending on it lets you substitute a fake or
the [`HeadlessTerminal`](testing.md) harness in tests, with no real tty and no static state to
reset. See [Testing](testing.md).

## Next steps

- [Styled text](styled-text.md) — build the `Line`s you append.
- [Scrollback](scrollback.md) — streaming blocks and collapsibles.
- [Dialogs](dialogs.md) — prompt the user with `await`.
- [The fixed region](fixed-region.md) — status bar and autocomplete.
