---
layout: home
title: dcli
tagline: inline terminal rendering · .NET 10
lead: >-
  Styled output flows into the terminal's real scrollback, while a small
  interactive region — input line, status bar, dropdowns, dialogs — stays pinned
  at the bottom. It's the rendering model behind Claude-Code-style CLIs.
repo: daemonicai/dcli
nuget: dcli
ctas:
  - { label: "View on GitHub →", href: "https://github.com/daemonicai/dcli", primary: true }
  - { label: "Get on NuGet", href: "https://www.nuget.org/packages/dcli" }
features:
  - icon: "▮"
    title: Inline, not full-screen
    body: No alternate screen. Your program's output and the user's history stay in native scrollback, exactly where a CLI user expects them.
  - icon: "─"
    title: Commit horizon
    body: Only a bounded window near the bottom is re-rendered; everything above is frozen, write-once, and owned by the terminal.
  - icon: "▭"
    title: Fixed region
    body: A pinned bottom stack — input editor and status bar, with a dialog slot above and an autocomplete dropdown below.
  - icon: "↺"
    title: Single-writer loop
    body: One actor thread owns all UI state and stdout. You post fire-and-forget commands in; events flow out on a channel.
  - icon: "⌨"
    title: Awaitable dialogs
    body: SelectAsync / MultiSelectAsync / ChoiceAsync / InputAsync and wizards read like ordinary async calls.
  - icon: "🧪"
    title: Testable without a TTY
    body: A companion Dcli.Testing package provides a HeadlessTerminal harness for frame-snapshot tests.
---

## Install

```bash
dotnet add package dcli --prerelease
```

> The current line is a release candidate (`0.2.0-rc.x`); drop `--prerelease` once a stable version ships.

## Quick start

```csharp
using Dcli;

// StartAsync enters raw mode and starts the render loop. `await using`
// guarantees the terminal is restored on every exit path.
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
    }
}
```

## The mental model in 30 seconds

| Concept | What it means |
| --- | --- |
| **Inline rendering** | No alternate screen. Output scrolls into the terminal's native history. |
| **Commit horizon** | Only a bounded window near the bottom is re-rendered; everything above is frozen. |
| **Fixed region** | The pinned bottom stack: input editor + status bar, dialog slot, dropdown. |
| **Single-writer loop** | One actor thread owns all UI state and stdout. |
| **Mechanics vs. semantics** | dcli renders and routes keys. *You* own protocols and what "submit" means. |

- **Target framework:** `net10.0` · **License:** MPL-2.0
- **Platforms:** Linux, macOS, Windows (modern VT terminals)

Full guides — concepts, styled text, dialogs, events, testing, architecture, API
reference — are in the [repository docs](https://github.com/daemonicai/dcli/tree/main/docs).
