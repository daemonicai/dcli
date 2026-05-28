# dcli

**Inline terminal-rendering for .NET.** Styled output flows into the terminal's *real*
scrollback, while a small interactive region — input line, status bar, dropdowns, modal
dialogs — stays pinned at the bottom. It's the rendering model behind Claude-Code-style CLIs,
packaged as a reusable library.

```
  dcli inline terminal rendering library — smoke tour
    Styled output flows into the real terminal scrollback.
    A small interactive region is pinned at the bottom.
    Content above the commit horizon is frozen and terminal-owned.
  ───────────────────────────────────────────────────────────────
  > type a message_                                ← input editor
  Phase 2/6 — streaming live block                 ← status bar
```

dcli is **not** a full-screen TUI framework. It never takes over the alternate screen; your
program's output and the user's history remain in the native scrollback, exactly where a CLI
user expects them. dcli owns the *rendering and widget mechanics*; your application owns the
*data and semantics*.

- **Target framework:** `net10.0`
- **Platforms:** Linux, macOS, Windows (modern VT terminals — Windows Terminal / Win10 1809+ / xterm-class)
- **Package:** [`dcli`](https://www.nuget.org/packages/dcli) on NuGet · **License:** MPL-2.0

---

## Install

```bash
dotnet add package dcli --prerelease
```

> The current release line is a release candidate (`0.2.0-rc.x`), hence `--prerelease`. Drop the
> flag once a stable version ships.

A companion package, [`Dcli.Testing`](docs/testing.md), provides a headless harness so you can
unit-test terminal UIs without a real TTY.

## Quick start

```csharp
using System.Text;
using Dcli;

// StartAsync enters raw mode and starts the render loop. `await using` guarantees the
// terminal is restored on every exit path — including crashes and signals.
await using Terminal terminal = await Terminal.StartAsync(new TerminalOptions());

terminal.Status.SetRows(Line.FromText("Type a message and press Enter. Ctrl+C to quit."));
terminal.Scrollback.Append("Welcome to the echo demo.");

// Events flow out on a channel; drain them on your own loop. dcli never runs your code
// on its render thread.
await foreach (TerminalEvent evt in terminal.Events.ReadAllAsync())
{
    switch (evt)
    {
        case InputSubmitted(var text):
            terminal.Scrollback.Append(new LineBuilder().Dim("> ").Text(text).Build());
            terminal.Input.Clear();
            break;

        // In raw mode Ctrl+C does NOT raise SIGINT — it arrives here as a key event,
        // and your app decides what it means.
        case KeyPressed(var key)
            when key.Modifiers == Modifiers.Ctrl
              && key.Code.Kind == KeyCode.KeyCodeKind.UnicodeScalar
              && key.Code.RuneValue == new Rune('c'):
            return;
    }
}
```

Awaitable modal dialogs read like ordinary `async` calls:

```csharp
DialogResult<int> pick = await terminal.SelectAsync(
    new SelectRequest(
        [Line.FromText("Anthropic"), Line.FromText("OpenAI"), Line.FromText("Local")],
        "Choose a provider"));

if (pick.Outcome == DialogOutcome.Submitted)
    terminal.Scrollback.Append($"You picked option {pick.Value}.");
```

## The mental model in 30 seconds

| Concept | What it means |
| --- | --- |
| **Inline rendering** | No alternate screen. Output scrolls into the terminal's native history. |
| **Commit horizon** | Only a bounded window near the bottom is re-rendered; everything above is frozen, write-once, and owned by the terminal. |
| **Fixed region** | The pinned bottom stack: input editor + status bar, with a dialog slot above and an autocomplete dropdown below. |
| **Single-writer loop** | One actor thread owns all UI state and stdout. You post fire-and-forget commands in; events flow out on a channel. |
| **Mechanics vs. semantics** | dcli renders and routes keys. *You* own protocols, completion candidates, and what a "submit" means. |

Read [docs/concepts.md](docs/concepts.md) for the full picture.

## Documentation

| Guide | Covers |
| --- | --- |
| [Getting started](docs/getting-started.md) | Install, your first program, the lifecycle, terminal requirements |
| [Core concepts](docs/concepts.md) | Inline rendering, the commit horizon, the fixed region, the actor loop |
| [Styled text](docs/styled-text.md) | `Segment` / `Line` / `Style` / `Color` / `Format` / `LineBuilder` |
| [Scrollback](docs/scrollback.md) | `Append`, live blocks, one-way collapsibles |
| [The fixed region](docs/fixed-region.md) | Input editor, status bar, autocomplete dropdown |
| [Dialogs](docs/dialogs.md) | `SelectAsync` / `MultiSelectAsync` / `ChoiceAsync` / `InputAsync`, wizards |
| [Input & events](docs/events.md) | The event channel, key encoding, paste, resize |
| [Testing](docs/testing.md) | Faking `ITerminal`, the `HeadlessTerminal` harness, frame snapshots |
| [Architecture](docs/architecture.md) | The design decisions and why they're shaped the way they are |
| [API reference](docs/api-reference.md) | Every public type, at a glance |

## Samples

Runnable samples live under [`samples/`](samples):

```bash
dotnet run --project samples/Dcli.Demo            # self-driving tour of every surface
dotnet run --project samples/Dcli.Demo.DmonWizard # a multi-step wizard built on the dialogs
```

## Building from source

```bash
dotnet build                       # analyzers run as warnings-as-errors; must be clean
dotnet test                        # all green
dotnet format --verify-no-changes  # style gate
```

The repository uses [OpenSpec](openspec/) for spec-driven development; shipped changes (with their
design notes) are archived under `openspec/changes/archive/`.

## License

[MPL-2.0](LICENSE).
