# dcli documentation

dcli is an inline terminal-rendering library for .NET. These guides explain the model, walk
through each surface, and document the full public API.

New here? Read [Core concepts](concepts.md) first, then [Getting started](getting-started.md).

## Guides

| Guide | Covers |
| --- | --- |
| [Getting started](getting-started.md) | Install, your first program, the lifecycle, terminal requirements |
| [Core concepts](concepts.md) | Inline rendering, the commit horizon, the fixed region, the actor loop, the mechanics/semantics boundary |
| [Styled text](styled-text.md) | `Segment` / `Line` / `Style` / `Color` / `Format` / `LineBuilder` |
| [Scrollback](scrollback.md) | `Append`, live (streaming) blocks, one-way collapsibles |
| [The fixed region](fixed-region.md) | The input editor, status bar, and autocomplete dropdown |
| [Dialogs](dialogs.md) | `SelectAsync` / `MultiSelectAsync` / `ChoiceAsync` / `InputAsync`, building wizards |
| [Input & events](events.md) | The event channel, key encoding, modifiers, paste, resize |
| [Testing](testing.md) | Faking `ITerminal`, the `HeadlessTerminal` harness, frame snapshots |
| [Architecture](architecture.md) | The design decisions and the reasoning behind them |
| [API reference](api-reference.md) | Every public type, at a glance |

## At a glance

```csharp
using Dcli;

await using Terminal terminal = await Terminal.StartAsync(new TerminalOptions());

terminal.Scrollback.Append("Hello from dcli.");
terminal.Status.SetRows(Line.FromText("ready"));

await foreach (TerminalEvent evt in terminal.Events.ReadAllAsync())
{
    if (evt is InputSubmitted(var text))
    {
        terminal.Scrollback.Append($"> {text}");
        terminal.Input.Clear();
    }
}
```
