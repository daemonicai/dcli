# dcli

**dcli** is a C#/.NET (`net10.0`) inline terminal-rendering library. Styled output flows
into the terminal's native scrollback — not an alternate screen — while a small interactive
region (input editor, status bar, and dropdown overlays) stays pinned at the bottom. The
design is modelled on the rendering style used by Claude Code and similar AI-driven CLI tools.

dcli is a mechanics-and-rendering library. It owns the raw-mode session, the render loop,
and the widget layer. The calling application owns data, semantics, and any application
protocol.

## Requirements

- .NET 10.0 or later
- A modern VT-capable terminal:
  - **macOS**: Terminal.app, iTerm2, Ghostty
  - **Windows**: Windows Terminal (legacy `conhost.exe` is not supported)
  - **Linux**: xterm, xterm-256color, or any xterm-class terminal emulator
- The process must be attached to a real tty (not redirected to a file or pipe)

## Quick start

```csharp
using Dcli;

// Enter raw mode and start the render loop.
await using Terminal t = await Terminal.StartAsync(new TerminalOptions());

// Append a styled line to the scrollback.
Line greeting = new LineBuilder()
    .Bold("dcli")
    .Text(" — inline terminal rendering")
    .Build();
t.Scrollback.Append(greeting);

// Set a status bar message.
t.Status.SetRows(new LineBuilder().Dim("Press Enter to continue…").Build());

// Ask the user to pick from a list (blocks until the user submits or cancels).
DialogResult<int> result = await t.SelectAsync(
    new SelectRequest(
        Items: ["Option A", "Option B", "Option C"]
            .Select(s => new LineBuilder().Text(s).Build())
            .ToList()),
    cancellationToken: default);

if (result.Outcome == DialogOutcome.Submitted)
{
    t.Scrollback.Append(new LineBuilder()
        .Text($"Selected index: {result.Value}")
        .Build());
}

// DisposeAsync stops the loop and restores the terminal.
```

Terminal restore is guaranteed on every exit path: normal disposal, unhandled exceptions,
and OS signals (SIGTERM/SIGINT on POSIX, process-exit events on Windows).

## Public surface

### Entry point

| Type | Purpose |
|------|---------|
| `Terminal` | Concrete lifecycle handle; obtain via `Terminal.StartAsync(...)`. |
| `ITerminal` | Interface the application should depend on (allows headless fakes in tests). |
| `TerminalOptions` | Configuration passed to `StartAsync` (max fixed-region height, frame interval). |
| `TerminalNotSupportedException` | Thrown by `StartAsync` when the environment is not VT-capable. |

### Sub-surfaces (accessed via `ITerminal`)

| Property | Interface | Purpose |
|----------|-----------|---------|
| `Scrollback` | `IScrollback` | Append lines, live blocks, and collapsibles. |
| `Input` | `IInput` | Programmatic text control of the input editor. |
| `Status` | `IStatus` | Set the status bar rows at the bottom of the fixed region. |
| `Autocomplete` | `IAutocomplete` | Show and hide the completion dropdown overlay. |

### Dialogs (methods on `ITerminal`)

| Method | Returns | Description |
|--------|---------|-------------|
| `SelectAsync` | `DialogResult<int>` | Single-select list; result is the zero-based selected index. |
| `MultiSelectAsync` | `DialogResult<int[]>` | Multi-select list; result is the checked indices in ascending order. |
| `ChoiceAsync` | `DialogResult<int>` | Single-select with an explanatory prompt row. |
| `InputAsync` | `DialogResult<string>` | Free-text entry dialog, with optional masking for secrets. |

All dialogs return `DialogOutcome.Submitted` on confirmation or `DialogOutcome.Cancelled`
on Escape or cancellation-token cancellation.

### Events

Drain `ITerminal.Events` (`ChannelReader<TerminalEvent>`) on a background task. Event types:

| Type | Description |
|------|-------------|
| `InputSubmitted` | User pressed Enter; carries the submitted text. |
| `InputChanged` | Input buffer changed; carries the current text. |
| `KeyPressed` | A key not consumed by the editor was forwarded to the consumer. |
| `Resized` | Terminal was resized; carries the new column and row counts. |

### Styled-text primitives

| Type | Description |
|------|-------------|
| `Line` | Immutable ordered list of `Segment`s forming one logical line. |
| `Segment` | Immutable run of literal text with a single `Style` applied. |
| `Style` | Optional foreground/background `Color` plus `Format` flags. |
| `Color` | Discriminated color: 16 named ANSI, 256-palette index, or 24-bit RGB. |
| `Format` | `[Flags]` enum: `Bold`, `Italic`, `Underline`, `Dim`, `Reverse`, `Strikethrough`. |
| `LineBuilder` | Fluent builder for constructing a `Line` from styled fragments. |

### Scrollback handle types

| Type | Description |
|------|-------------|
| `ILiveBlock` | Handle for an in-progress mutable scrollback block (`BeginLive`). |
| `ICollapsible` | Handle for a collapsible block that can be expanded once (`BeginCollapsible`). |

### Input event types

| Type | Description |
|------|-------------|
| `KeyEvent` | A decoded key press: a `KeyCode` (Unicode scalar or `NamedKey`) plus `Modifiers`. |
| `PasteEvent` | A bracketed-paste block decoded from UTF-8. |
| `ResizeEvent` | A terminal resize (not emitted to the consumer; use the `Resized` terminal event). |
| `NamedKey` | Enum of non-printable keys: arrows, F-keys, Tab, Enter, Escape, etc. |
| `Modifiers` | Flags enum: `Ctrl`, `Alt`, `Shift`. |
| `AutocompleteCandidate` | A completion candidate with display `Line` and `InsertText` to apply on accept. |

## Testing

`Dcli.Testing` — a headless harness that runs the full library without a real terminal —
is shipping as a separate NuGet package in the next release.

## License

[Mozilla Public License 2.0](https://github.com/daemonicai/dcli/blob/main/LICENSE)
