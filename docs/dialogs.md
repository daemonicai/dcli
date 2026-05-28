# Dialogs

Dialogs are dcli's headline ergonomic: a modal prompt you **`await`**. Under the hood it's a pure
message to the render loop carrying a `TaskCompletionSource`; the loop drives the modal overlay and
completes your task on submit/cancel. On the outside it reads like an ordinary async call, so a
wizard is just a sequence of `await`s.

There are four:

| Method | Request | Result | Prompt the user to… |
| --- | --- | --- | --- |
| `SelectAsync` | `SelectRequest` | `DialogResult<int>` | pick one item from a list |
| `MultiSelectAsync` | `MultiSelectRequest` | `DialogResult<int[]>` | check any number of items |
| `ChoiceAsync` | `ChoiceRequest` | `DialogResult<int>` | pick one option (with an explanatory prompt) |
| `InputAsync` | `InputRequest` | `DialogResult<string>` | type free text (optionally masked) |

All four are on [`ITerminal`](api-reference.md#iterminal) and take an optional `CancellationToken`.

## The result shape

Every dialog returns a [`DialogResult<T>`](api-reference.md#dialogresultt): an `Outcome` plus a
`Value` that's meaningful only when `Outcome == Submitted`.

```csharp
public enum DialogOutcome { Submitted, Back, Cancelled }
public readonly record struct DialogResult<T>(DialogOutcome Outcome, T Value);
```

- **`Submitted`** — the user confirmed (Enter). `Value` holds the selection/text.
- **`Cancelled`** — the user pressed Escape, or the `CancellationToken` fired. `Value` is `default`.
- **`Back`** — only when the request opted into `AllowBack` and the user pressed Backspace to step
  back (for wizards). `Value` is `default`. See [Wizards](#building-a-wizard).

Always branch on `Outcome` before reading `Value`:

```csharp
DialogResult<int> r = await terminal.SelectAsync(req);
if (r.Outcome == DialogOutcome.Submitted)
    Use(r.Value);
```

## Select — pick one

```csharp
DialogResult<int> pick = await terminal.SelectAsync(
    new SelectRequest(
        [Line.FromText("Anthropic"), Line.FromText("OpenAI"), Line.FromText("Local")],
        "Choose a provider"));

if (pick.Outcome == DialogOutcome.Submitted)
    terminal.Scrollback.Append($"Provider #{pick.Value}");
```

`Value` is the **zero-based index** into the items. (Edge case: submitting an empty item list
returns `Value == -1`.)

### Items and titles: strings or styled lines

Items are `IReadOnlyList<Line>`. The title can be a `Line`, a list of lines (multi-line), or a
plain `string`. The handy combination is **`Line` items with a plain-string title**:

```csharp
var req = new SelectRequest(
    [
        new LineBuilder().Fg("C#",   Color.Named(Color.AnsiColor.Cyan)).Build(),
        new LineBuilder().Fg("Rust", Color.Named(Color.AnsiColor.Red)).Build(),
    ],
    "Pick a language");
```

For a fully plain list with no title, string items work directly:

```csharp
new SelectRequest(["C#", "Go", "Rust"]);                       // items only, all strings
```

Titles can be **multi-line** — pass a list of lines:

```csharp
new SelectRequest(
    [Line.FromText("C#"), Line.FromText("Go")],
    [Line.FromText("Pick a language"), new LineBuilder().Dim("↑/↓, Enter to confirm").Build()]);
```

> The convenience constructors don't cover *every* mix — notably **string items with a string
> title** has no overload. Use `Line` items (`Line.FromText(...)`) whenever you want a string title,
> as above.

## MultiSelect — check several

Space toggles items; Enter submits the checked set.

```csharp
DialogResult<int[]> picks = await terminal.MultiSelectAsync(
    new MultiSelectRequest(
        [Line.FromText("Logs"), Line.FromText("Metrics"), Line.FromText("Traces")],
        "Enable which signals?"));

if (picks.Outcome == DialogOutcome.Submitted)
    foreach (int i in picks.Value)   // checked indices, ascending
        Enable(i);
```

`Value` is an `int[]` of the checked indices in ascending order; an empty submission is an empty
array (still `Submitted`). `Cancelled` also yields an empty array.

## Choice — pick one, with a prompt

Semantically the same as `Select`, but the type name signals "mutually-exclusive options with an
explanation", and it carries a `Prompt` instead of a `Title`. Handy for confirmations:

```csharp
DialogResult<int> confirm = await terminal.ChoiceAsync(
    new ChoiceRequest(
        [Line.FromText("Yes"), Line.FromText("No")],
        [
            new LineBuilder().Bold("Overwrite the existing file?").Build(),
            new LineBuilder().Dim("This cannot be undone.").Build(),
        ]));

bool yes = confirm.Outcome == DialogOutcome.Submitted && confirm.Value == 0;
```

## Input — free text

```csharp
DialogResult<string> name = await terminal.InputAsync(
    new InputRequest(prompt: "What's your name?", Default: "ada"));

if (name.Outcome == DialogOutcome.Submitted)
    Greet(name.Value);
```

[`InputRequest`](api-reference.md#inputrequest) fields:

- `Prompt` — optional preamble (string, `Line`, or multi-line list).
- `Default` — pre-filled text; the caret starts at its end.
- `IsSecret` — when `true`, each character renders as a bullet (`•`). **The returned `Value`
  always carries the real, unmasked text** — masking is display-only.

```csharp
var key = await terminal.InputAsync(new InputRequest(prompt: "API key:", IsSecret: true));
```

> The `params` constructors of `InputRequest` (multi-line prompt shorthand) can't also set
> `Default`/`IsSecret`. When you need those, use the `IReadOnlyList<…>`/single-line overloads, as
> above.

## Cancellation

Pass a `CancellationToken` to close the dialog programmatically (e.g. a timeout, or because a
background event made the prompt moot). When the token fires, the dialog closes and the result is
`Cancelled`. If the token is already cancelled when you call, it returns `Cancelled` immediately
without opening anything.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
DialogResult<int> r = await terminal.SelectAsync(req, cts.Token);
```

## One dialog at a time

Only one modal overlay can be open at once (it shares the single overlay slot with autocomplete —
opening a dialog suppresses autocomplete). **Awaiting two dialogs concurrently is a programming
error**: the second `…Async` call faults its task with `InvalidOperationException("A dialog is
already active.")`. Await them in sequence instead — which is the natural shape for a wizard anyway.

## Building a wizard

A multi-step wizard is just sequential `await`s — dcli owns the slot and key routing; *you* own the
step graph and what each answer means ([mechanics vs. semantics](concepts.md#mechanics-vs-semantics--the-dcliconsumer-boundary)).

```csharp
// ModelsFor returns IReadOnlyList<Line>, e.g. names.Select(Line.FromText).ToList().
async Task<Config?> RunWizardAsync(ITerminal t, CancellationToken ct)
{
    var provider = await t.SelectAsync(
        new SelectRequest(
            [Line.FromText("Anthropic"), Line.FromText("OpenAI")], "Provider", allowBack: false), ct);
    if (provider.Outcome != DialogOutcome.Submitted) return null;

    var model = await t.SelectAsync(
        new SelectRequest(ModelsFor(provider.Value), "Model", allowBack: true), ct);
    if (model.Outcome == DialogOutcome.Back)      return await RunWizardAsync(t, ct); // step back
    if (model.Outcome != DialogOutcome.Submitted) return null;

    var key = await t.InputAsync(new InputRequest(prompt: "API key:", IsSecret: true), ct);
    if (key.Outcome != DialogOutcome.Submitted) return null;

    return new Config(provider.Value, model.Value, key.Value);
}
```

**`AllowBack`** (on `SelectRequest` and `ChoiceRequest`) is what makes back-navigation possible:
when `true`, pressing Backspace before moving the cursor (and before typing any filter text) closes
the dialog with `Outcome == Back`, so your wizard can pop to the previous step. It defaults to
`false`, so existing prompts are unaffected.

The [`Dcli.Demo.DmonWizard`](../samples/Dcli.Demo.DmonWizard) sample is a complete worked example
of this pattern.

## See also

- [The fixed region](fixed-region.md) — the overlay slot dialogs render into.
- [Testing](testing.md) — driving dialogs from a headless test.
- [API reference](api-reference.md#dialogs).
