# API reference

Every public type in the `Dcli` namespace, plus the `Dcli.Testing` harness. Signatures are exact;
this is a quick reference, not a substitute for the per-topic guides linked throughout.

- Package `dcli` → namespace `Dcli`
- Package `Dcli.Testing` → namespace `Dcli.Testing`

## Contents

- [Entry point](#entry-point) · [Terminal](#terminal) · [ITerminal](#iterminal) · [TerminalOptions](#terminaloptions) · [TerminalNotSupportedException](#terminalnotsupportedexception)
- [Scrollback](#scrollback-surfaces) · [IScrollback](#iscrollback) · [ILiveBlock](#iliveblock) · [ICollapsible](#icollapsible)
- [Fixed-region surfaces](#fixed-region-surfaces) · [IInput](#iinput) · [IStatus](#istatus) · [IAutocomplete](#iautocomplete) · [AutocompleteCandidate](#autocompletecandidate)
- [Dialogs](#dialogs) · [DialogResult\<T>](#dialogresultt) · [DialogOutcome](#dialogoutcome) · request types
- [Styled text](#styled-text) · [Line](#line) · [Segment](#segment) · [Style](#style) · [Color](#color) · [Format](#format) · [LineBuilder](#linebuilder)
- [Events & input](#events--input) · [TerminalEvent](#terminalevent) · [InputEvent](#inputevent) · [KeyEvent](#keyevent) · [KeyCode](#keycode) · [NamedKey](#namedkey) · [Modifiers](#modifiers) · [PasteEvent](#pasteevent)
- [Testing harness (Dcli.Testing)](#testing-harness-dclitesting)

---

## Entry point

### Terminal

`public sealed class Terminal : ITerminal` — the lifecycle handle.

```csharp
static Task<Terminal> StartAsync(TerminalOptions options, CancellationToken cancellationToken = default);
ValueTask DisposeAsync();
```

`StartAsync` enters raw mode and starts the render loop; throws [`TerminalNotSupportedException`](#terminalnotsupportedexception)
when the environment isn't VT-capable. `DisposeAsync` stops the loop, joins threads, and restores
the terminal (idempotent; rethrows a captured loop-thread fault after restoring). Implements all of
[`ITerminal`](#iterminal). See [Getting started](getting-started.md).

### ITerminal

`public interface ITerminal : IAsyncDisposable` — the abstraction to depend on.

```csharp
IScrollback   Scrollback   { get; }
IInput        Input        { get; }
IStatus       Status       { get; }
IAutocomplete Autocomplete { get; }
ChannelReader<TerminalEvent> Events { get; }

(int Columns, int Rows) GetTerminalSize();

Task<DialogResult<int>>   SelectAsync(SelectRequest req, CancellationToken cancellationToken = default);
Task<DialogResult<int[]>> MultiSelectAsync(MultiSelectRequest req, CancellationToken cancellationToken = default);
Task<DialogResult<int>>   ChoiceAsync(ChoiceRequest req, CancellationToken cancellationToken = default);
Task<DialogResult<string>> InputAsync(InputRequest req, CancellationToken cancellationToken = default);
```

> Note: the current terminal size is read via the `GetTerminalSize()` **method**, not a property.

### TerminalOptions

`public sealed class TerminalOptions`

```csharp
int? MaxFixedHeight     { get; init; }       // null → ~50% of terminal height, min 8 rows
int  MinFrameIntervalMs { get; init; } = 16; // ≈ 60 fps ceiling
```

### TerminalNotSupportedException

`public sealed class TerminalNotSupportedException : Exception` — thrown by `Terminal.StartAsync`
when stdout/stdin isn't a tty, `TERM=dumb`, or the platform isn't VT-capable. Standard three
constructors (`()`, `(string)`, `(string, Exception)`).

---

## Scrollback surfaces

See [Scrollback](scrollback.md).

### IScrollback

```csharp
void          Append(Line line);
void          Append(string text);                                       // = Append(Line.FromText(text))
ILiveBlock    BeginLive();
ICollapsible  BeginCollapsible(Line summary, IReadOnlyList<Line> hiddenLines);
```

### ILiveBlock

A handle to an in-progress mutable block. All methods are fire-and-forget.

```csharp
void AppendText(string text);                 // append to buffer; no-op after SetContent/Commit
void SetContent(IReadOnlyList<Line> lines);   // replace wholesale; AppendText becomes a no-op
void Commit();                                // freeze; flows into native scrollback next paint
```

### ICollapsible

```csharp
void Expand();   // one-way, idempotent; reprints if already past the commit horizon
```

---

## Fixed-region surfaces

See [The fixed region](fixed-region.md).

### IInput

```csharp
void SetText(string text);   // replace buffer, caret to end; does NOT emit InputChanged
void Clear();                // empty buffer, caret to 0;   does NOT emit InputChanged
```

### IStatus

```csharp
void SetRows(params Line[] rows);          // replaces all rows; empty clears
void SetRows(IReadOnlyList<Line> rows);    // replaces all rows; empty clears
```

The status bar is never truncated by the height budget.

### IAutocomplete

```csharp
void Show(IReadOnlyList<AutocompleteCandidate> candidates);  // no-op while a modal dialog is open
void Hide();                                                 // no-op unless autocomplete is active
```

### AutocompleteCandidate

`public sealed record AutocompleteCandidate(string InsertText, Line Display)`

- `Display` — the styled row shown in the dropdown.
- `InsertText` — applied on accept via **whole-buffer replace** (caret to end).

---

## Dialogs

All four dialog methods are on [`ITerminal`](#iterminal). See [Dialogs](dialogs.md).

### DialogResult\<T>

`public readonly record struct DialogResult<T>(DialogOutcome Outcome, T Value)`

`Value` is meaningful only when `Outcome == Submitted`. `T` is `int` (Select/Choice), `int[]`
(MultiSelect), or `string` (Input).

### DialogOutcome

`public enum DialogOutcome { Submitted, Back, Cancelled }`

`Back` is produced only by `SelectAsync`/`ChoiceAsync` when the request sets `AllowBack = true`.

### SelectRequest

`public sealed record SelectRequest(IReadOnlyList<Line> Items, IReadOnlyList<Line>? Title = null, bool AllowBack = false)`

Convenience constructors accept a single `Line?`/`string?` title, a `params Line[]`/`params string[]`
multi-line title, and plain-string item lists (each converted via `Line.FromText`). Submitting an
empty item list returns `Value == -1`.

### MultiSelectRequest

`public sealed record MultiSelectRequest(IReadOnlyList<Line> Items, IReadOnlyList<Line>? Title = null)`

Same family of string/`Line`/`params` convenience constructors. Result is checked indices, ascending.

### ChoiceRequest

`public sealed record ChoiceRequest(IReadOnlyList<Line> Options, IReadOnlyList<Line>? Prompt = null, bool AllowBack = false)`

Semantically equivalent to `SelectRequest`; carries a `Prompt` instead of a `Title`.

### InputRequest

`public sealed record InputRequest(IReadOnlyList<Line>? Prompt = null, string? Default = null, bool IsSecret = false)`

- `Default` — pre-filled text; caret starts at its end.
- `IsSecret` — renders each char as `•`; the returned `Value` is always the real text.
- The `params Line[]` / `params string[]` prompt constructors **cannot** also set `Default`/`IsSecret`
  — use the single-line or `IReadOnlyList<…>` overloads when you need those.

---

## Styled text

See [Styled text](styled-text.md).

### Line

`public sealed record Line(IReadOnlyList<Segment> Segments)`

```csharp
Line(IEnumerable<Segment> segments);                          // copies into a read-only list
static Line FromText(string text, Style? style = null);       // single-segment line
```

Structural (sequence) equality over segments. No implicit `string` → `Line` conversion.

### Segment

`public record Segment(string Text, Style Style = default)` — text is **sanitized at construction**: whitespace controls (`\t \n \v \f \r`) become a single space; other C0/C1/DEL bytes are stripped (or replaced with visible glyphs under `DCLI_SANITIZE_MODE=replace`). Bracket and markup characters are treated literally — never interpreted as directives. See [Sanitize by default](styled-text.md#sanitize-by-default).

`static Segment Raw(string text)` — skips sanitization entirely; the caller is responsible for terminal integrity.

### Style

`public readonly record struct Style(Color? Foreground = null, Color? Background = null, Format Format = Format.None)`

`default(Style)` = terminal defaults, no attributes.

### Color

`public readonly record struct Color` — construct via factories only:

```csharp
static Color Named(Color.AnsiColor color);   // 16 ANSI colors
static Color FromIndex(byte index);          // 256-palette index 0–255
static Color FromRgb(byte r, byte g, byte b);// 24-bit truecolor

Color.ColorKind Kind { get; }                // Named | Indexed | Rgb
Color.AnsiColor NamedValue { get; }          // valid when Kind == Named (else throws)
byte IndexValue { get; }                     // valid when Kind == Indexed (else throws)
byte R { get; }  byte G { get; }  byte B { get; } // valid when Kind == Rgb (else throws)
```

`enum AnsiColor` — `Black, Red, Green, Yellow, Blue, Magenta, Cyan, White` and `Bright*` (0–15).

### Format

`[Flags] public enum Format` — `None=0, Bold=1, Italic=2, Underline=4, Dim=8, Reverse=16, Strikethrough=32`.

### LineBuilder

`public sealed class LineBuilder` — fluent; each method returns `this`. Don't reuse after `Build()`.

```csharp
LineBuilder Text(string s);
LineBuilder Append(string s, Style style = default);
LineBuilder Bold(string s);  Italic(string s);  Underline(string s);
LineBuilder Dim(string s);   Reverse(string s);  Strikethrough(string s);
LineBuilder Fg(string s, Color foreground);
LineBuilder Bg(string s, Color background);
LineBuilder Raw(string s);                        // verbatim; skips sanitization
Line        Build();
```

---

## Events & input

See [Input & events](events.md).

### TerminalEvent

`public abstract record TerminalEvent` — the outbound stream (`ITerminal.Events`):

```csharp
sealed record InputSubmitted(string Text)        : TerminalEvent;
sealed record InputChanged(string Text)          : TerminalEvent;
sealed record KeyPressed(KeyEvent Key)           : TerminalEvent;
sealed record Resized(int Columns, int Rows)     : TerminalEvent;
```

### InputEvent

`public abstract record InputEvent` — the low-level VT-pipeline vocabulary:

```csharp
sealed record KeyEvent(KeyCode Code, Modifiers Modifiers) : InputEvent;
sealed record PasteEvent(string Text)                     : InputEvent;
sealed record ResizeEvent(int Columns, int Rows)          : InputEvent;
```

### KeyEvent

`public sealed record KeyEvent(KeyCode Code, Modifiers Modifiers)`.

### KeyCode

`public readonly record struct KeyCode` — a printable scalar or a named key:

```csharp
static KeyCode FromRune(System.Text.Rune rune);   // KeyCodeKind.UnicodeScalar
static KeyCode Named(NamedKey key);                // KeyCodeKind.Named

KeyCode.KeyCodeKind Kind { get; }                  // UnicodeScalar | Named
System.Text.Rune RuneValue { get; }                // valid when UnicodeScalar (else throws)
NamedKey NamedValue { get; }                       // valid when Named (else throws)
```

### NamedKey

`public enum NamedKey` — `Enter, Tab, Backspace, Escape, Up, Down, Right, Left, Home, End, PageUp,
PageDown, Insert, Delete, F1`…`F12, BackTab`.

### Modifiers

`[Flags] public enum Modifiers` — `None=0, Ctrl=1, Alt=2, Shift=4`. `Shift` appears only on named
keys; it's never reported together with `Ctrl`.

### PasteEvent

`public sealed record PasteEvent(string Text)` — full UTF-8-decoded bracketed-paste body.

---

## Testing harness (`Dcli.Testing`)

See [Testing](testing.md).

### HeadlessTerminal

`public sealed class HeadlessTerminal : IAsyncDisposable`

```csharp
static Task<HeadlessTerminal> StartAsync(HeadlessTerminalOptions? options = null,
                                         CancellationToken cancellationToken = default);

ITerminal     Terminal { get; }   // the real façade — drive it as production does
VirtualClock  Clock    { get; }
FrameSnapshot Snapshot { get; }   // immutable; empty before first paint

void Feed(ReadOnlySpan<byte> bytes);   // raw bytes through the real VT parser
void SendKey(KeyEvent key);            // inject one key event (use for named keys)
void Type(string text);                // type printables rune-by-rune
void Paste(string text);               // inject a bracketed-paste block
void Resize(int columns, int rows);    // fire a resize via the SIGWINCH path
Task SettleAsync(CancellationToken cancellationToken = default);
ValueTask DisposeAsync();
```

### HeadlessTerminalOptions

`public sealed class HeadlessTerminalOptions`

```csharp
int InitialColumns       { get; init; } = 80;
int InitialRows          { get; init; } = 24;
TimeSpan MinFrameInterval{ get; init; } = TimeSpan.Zero;  // non-zero for cadence tests
int? MaxFixedHeight      { get; init; }
VirtualClock? Clock      { get; init; }                   // share one clock across harnesses
```

### VirtualClock

`public sealed class VirtualClock : IClock`

```csharp
VirtualClock(TimeSpan? initial = null);   // defaults to TimeSpan.Zero
TimeSpan Now { get; }
void Advance(TimeSpan by);                // completes waiters whose deadline has passed
```

### FrameSnapshot

`public sealed record FrameSnapshot` (all members `required init`):

```csharp
IReadOnlyList<Line> LiveWindowRows     { get; init; }
IReadOnlyList<Line> FixedRegionRows    { get; init; }
(int Row, int Col)? Caret              { get; init; }   // null when hidden / modal
bool                IsCursorVisible    { get; init; }
(int Columns, int Rows) Size           { get; init; }
IReadOnlyList<Line> NewlyCommittedRows { get; init; }
OverlayDescriptor   Overlay            { get; init; }
```

### OverlayDescriptor

`public sealed record OverlayDescriptor`

```csharp
OverlayKind Kind            { get; init; }       // None | Autocomplete | Dialog | Input
int         SelectedIndex   { get; init; } = -1; // list overlays; -1 when empty/N/A
int         VisibleRowCount { get; init; }
string?     InputText       { get; init; }       // Input overlay, when not secret
bool        IsSecret        { get; init; }
```

`public enum OverlayKind { None, Autocomplete, Dialog, Input }`

### FrameSnapshotPrinter

`public static class FrameSnapshotPrinter`

```csharp
static string PrettyPrint(FrameSnapshot snapshot);   // stable, style-stripped, ASCII-bordered
```

Use for golden-frame assertions — asserts structure, not raw ANSI.
