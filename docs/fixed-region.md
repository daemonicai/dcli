# The fixed region

The **fixed region** is the interactive stack pinned at the bottom of the terminal. dcli repaints
it every frame. From bottom to top it is:

```
   ┌─ overlay slot (above input): a modal dialog, when one is open
   ├─ autocomplete dropdown (below input): when shown
   │  > the input editor line_
   └─ the status bar (one or more rows)
```

The input editor and status bar are always present; the dialog slot and autocomplete dropdown are
two **mutually-exclusive** overlays (only one shows at a time). Its total height is bounded by
`TerminalOptions.MaxFixedHeight` (default ≈ 50% of the terminal, minimum 8 rows). Growing the fixed
region [commits](concepts.md#the-commit-horizon) whatever scrolls above it.

This page covers the input editor, the status bar, and autocomplete. Modal dialogs have their own
guide: [Dialogs](dialogs.md).

## The input editor

dcli owns a single-line input editor: it handles typing, the caret, Backspace/Delete, Home/End,
left/right movement, and so on. You don't render it — you observe it and occasionally drive it.

### Observing input

The editor emits events on `terminal.Events`:

- `InputChanged(string Text)` — the buffer changed (a character typed or deleted).
- `InputSubmitted(string Text)` — the user pressed Enter; carries the line as submitted.

```csharp
await foreach (TerminalEvent evt in terminal.Events.ReadAllAsync())
{
    switch (evt)
    {
        case InputSubmitted(var text):
            Handle(text);
            terminal.Input.Clear();   // editors don't auto-clear on submit; you decide
            break;

        case InputChanged(var text):
            terminal.Autocomplete.Show(ComputeCandidates(text));
            break;
    }
}
```

Note that submission does **not** clear the buffer — that's a semantic choice, so it's yours. Call
`Input.Clear()` (or `SetText`) when you want it cleared.

### Driving input programmatically

`terminal.Input` is an [`IInput`](api-reference.md#iinput):

| Method | Effect |
| --- | --- |
| `SetText(string)` | Replace the whole buffer; move the caret to the end. |
| `Clear()` | Empty the buffer; move the caret to position 0. |

Both are **programmatic** edits and deliberately do **not** emit `InputChanged` — that event is
reserved for user-driven edits, so you won't get an echo loop when you set the text yourself.

```csharp
terminal.Input.SetText("/help");  // pre-fill, e.g. from history
terminal.Input.Clear();           // after handling a submission
```

## The status bar

`terminal.Status` is an [`IStatus`](api-reference.md#istatus). Set one or more rows; they render at
the very bottom and are **never truncated** by the height budget.

```csharp
terminal.Status.SetRows(
    new LineBuilder().Bold("connected").Text("  ·  ").Dim("12 turns").Build());

// multiple rows:
terminal.Status.SetRows(
    Line.FromText("model: claude-opus-4-8"),
    Line.FromText("tokens: 1,204 / 200,000"));

terminal.Status.SetRows();  // empty call clears the status bar
```

`SetRows` has a `params Line[]` overload and an `IReadOnlyList<Line>` overload. Each call
**replaces** the entire status content (it's not additive).

## Autocomplete

The autocomplete dropdown appears below the input. dcli renders it and handles its navigation
(↑/↓ to move, Enter/Tab to accept, Esc to dismiss). **You** supply the candidates — typically in
response to an `InputChanged` event.

```csharp
AutocompleteCandidate[] candidates =
[
    new("/help",  new LineBuilder().Bold("/help").Dim("  show commands").Build()),
    new("/clear", new LineBuilder().Bold("/clear").Dim("  clear scrollback").Build()),
];

terminal.Autocomplete.Show(candidates);
// ...
terminal.Autocomplete.Hide();
```

[`AutocompleteCandidate`](api-reference.md#autocompletecandidate) has two parts:

- `Display` — the styled `Line` shown in the dropdown row.
- `InsertText` — the text applied to the buffer **when the candidate is accepted**. Acceptance is
  a **whole-buffer replace**: the entire input is replaced with `InsertText` and the caret moves to
  the end. (Replacing only the typed prefix is not supported in this release.)

[`IAutocomplete`](api-reference.md#iautocomplete) behavior:

| Method | Notes |
| --- | --- |
| `Show(IReadOnlyList<AutocompleteCandidate>)` | No-op while a modal dialog is open (a dialog wins the single overlay slot). |
| `Hide()` | No-op unless the active overlay is the autocomplete. |

### The round-trip pattern

dcli never calls back into your code to *ask* for completions — that would run your code on the
render thread, which is forbidden. Instead it's a round-trip you drive:

```
user types ──▶ InputChanged event ──▶ you compute candidates ──▶ Autocomplete.Show
```

When the user accepts a candidate, dcli applies the `InsertText` and emits `InputChanged` again,
so your loop sees the new buffer naturally. If you want the dropdown to vanish after acceptance,
call `Hide()`.

## Height budget

The whole fixed region is capped at `MaxFixedHeight` rows. When content would exceed it, dialogs
and dropdowns scroll internally; the status bar is always shown in full. Remember the trade-off: a
tall fixed region shrinks the live window and **commits** scrollback above it
([commit horizon](concepts.md#the-commit-horizon)).

## See also

- [Dialogs](dialogs.md) — the modal overlay that shares the slot with autocomplete.
- [Input & events](events.md) — the full event stream and key encoding.
- [API reference](api-reference.md#fixed-region-surfaces).
