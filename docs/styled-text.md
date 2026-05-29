# Styled text

Everything dcli renders — scrollback lines, status rows, dialog items, autocomplete rows — is
built from one small set of immutable types. There is **no markup language**: a string like
`"[bold]"` is literal text, never a directive. You compose styling programmatically.

## The primitives

```
Segment   = a run of text + one Style
Line      = an ordered list of Segments (one logical line)
Style     = optional foreground Color + optional background Color + Format flags
Color     = 16 named ANSI | a 256-palette index | 24-bit RGB
Format    = [Flags]: Bold, Italic, Underline, Dim, Reverse, Strikethrough
```

### Segment

An immutable run of text with a single `Style` applied uniformly.

```csharp
var plain  = new Segment("hello");
var styled = new Segment("hello", new Style(Foreground: Color.Named(Color.AnsiColor.Cyan)));
```

Text is stored as provided and rendered as printable characters. Control and escape bytes are
neutralized at construction — see [Sanitize by default](#sanitize-by-default) below.

### Line

An immutable, ordered list of `Segment`s forming one logical line. Two `Line`s are equal when they
hold the same segments in the same order (structural equality — handy for golden-frame tests).

The quickest way to make a single-style line:

```csharp
Line label   = Line.FromText("plain text");
Line warning = Line.FromText("careful!", new Style(Foreground: Color.Named(Color.AnsiColor.Yellow)));
```

For multi-style lines, use [`LineBuilder`](#linebuilder).

> **There is no implicit `string` → `Line` conversion**, deliberately — it would let a bare string
> slip in where a styled line was meant, silently dropping styling. Call `Line.FromText(s)`
> explicitly. (Many APIs, like `Scrollback.Append` and the dialog requests, also accept raw
> strings directly as a convenience and convert for you.)

### Style

```csharp
public readonly record struct Style(
    Color? Foreground = null,   // null → terminal default
    Color? Background = null,   // null → terminal default
    Format Format = Format.None);
```

`default(Style)` (and `new Style()`) means "terminal defaults, no attributes". Because it's a
record struct, you can `with`-copy it:

```csharp
var baseStyle = new Style(Foreground: Color.Named(Color.AnsiColor.Green));
var emphatic  = baseStyle with { Format = Format.Bold };
```

### Format

A `[Flags]` enum — combine with `|`:

```csharp
var s = new Style(Format: Format.Bold | Format.Underline);
```

| Flag | Effect |
| --- | --- |
| `None` | no attributes |
| `Bold` | bold / increased intensity |
| `Italic` | italic |
| `Underline` | underline |
| `Dim` | dim / faint |
| `Reverse` | swap foreground and background |
| `Strikethrough` | crossed-out |

Actual rendering depends on the terminal; most honor bold/dim/underline/reverse, fewer honor
italic and strikethrough.

### Color

A discriminated value with three representations. Construct with a factory; never with `new`:

```csharp
Color red    = Color.Named(Color.AnsiColor.Red);   // one of 16 ANSI colors
Color grey   = Color.FromIndex(240);               // 256-color palette index 0–255
Color brand  = Color.FromRgb(0x7C, 0x3A, 0xED);    // 24-bit truecolor
```

Inspect via `Kind` and the matching accessor (`NamedValue` / `IndexValue` / `R`,`G`,`B`); reading
the wrong accessor for the stored kind throws:

```csharp
if (brand.Kind == Color.ColorKind.Rgb)
    Console.WriteLine($"#{brand.R:X2}{brand.G:X2}{brand.B:X2}");
```

The 16 `AnsiColor` names are `Black`, `Red`, `Green`, `Yellow`, `Blue`, `Magenta`, `Cyan`,
`White`, and their `Bright*` variants.

> **Truecolor fallback.** dcli detects terminal capabilities at startup; on a terminal without
> 24-bit color support, RGB colors are emitted in the best representation the terminal accepts.
> When in doubt, named colors are universally safe.

## LineBuilder

A fluent builder for multi-segment lines. Each method appends a segment and returns the builder;
`Build()` returns the finished `Line`. **Don't reuse a builder after calling `Build()`.**

```csharp
Line line = new LineBuilder()
    .Dim("✻ ")
    .Bold("Thinking")
    .Text(" — ")
    .Fg("42 tokens", Color.Named(Color.AnsiColor.Cyan))
    .Build();
```

| Method | Appends a segment that is… |
| --- | --- |
| `Text(s)` | unstyled (terminal defaults) |
| `Append(s, style)` | styled with an explicit `Style` |
| `Bold(s)` / `Italic(s)` / `Underline(s)` / `Dim(s)` / `Reverse(s)` / `Strikethrough(s)` | the corresponding `Format` |
| `Fg(s, color)` | the given foreground color |
| `Bg(s, color)` | the given background color |

The shortcut methods apply exactly one attribute. For combinations (e.g. bold **and** colored),
build the `Style` yourself and use `Append`:

```csharp
Line line = new LineBuilder()
    .Append("error", new Style(
        Foreground: Color.Named(Color.AnsiColor.BrightRed),
        Format: Format.Bold))
    .Text(": connection refused")
    .Build();
```

## Sanitize by default

dcli sanitizes text at `Segment` construction — you never need to pre-clean consumer text before
wrapping it in a `Segment` or passing it to `Line.FromText`/`LineBuilder`.

**Two byte classes are neutralized:**

| Class | Bytes | Default action |
| --- | --- | --- |
| Whitespace controls | `\t \n \v \f \r` | Replaced with a single space |
| Other C0/C1/DEL | `U+0000–U+0008`, `U+000E–U+001F`, `U+007F`, `U+0080–U+009F` | Stripped (removed) |

Ordinary printable text — including multi-byte UTF-8, CJK characters, emoji, and combining marks
— is completely unaffected.

### Raw (verbatim) escape hatch

When you deliberately need to emit pre-rendered ANSI sequences — for example, to relay a
hyperlink OSC sequence or a pre-colored block — use `Segment.Raw` or `LineBuilder.Raw`:

```csharp
// Segment: skip sanitization entirely for one run
var raw = Segment.Raw("\x1b[1mBold via raw ANSI\x1b[m");

// LineBuilder: mix sanitized and raw segments in one line
Line line = new LineBuilder()
    .Text("Status: ")
    .Raw("\x1b[32mOK\x1b[m")   // verbatim; caller owns terminal integrity
    .Build();
```

The caller is responsible for terminal integrity when using raw segments — an unclosed SGR or
half-written OSC sequence will corrupt the output.

### Debugging with DCLI_SANITIZE_MODE

Set the environment variable `DCLI_SANITIZE_MODE=replace` before launching your process to make
stripped bytes visible rather than silently removed:

```sh
DCLI_SANITIZE_MODE=replace dotnet run
```

In `replace` mode, each stripped C0/C1/DEL byte is substituted with its Unicode Control-Picture
glyph (e.g. `ESC` → `␛`, `NUL` → `␀`), making it easy to spot where unexpected control bytes
were injected into your text. The default is `strip`.

> **Why does this matter?** Without sanitization, a raw VT escape sequence embedded in consumer
> text could defeat the synchronized-output fence, reposition the cursor, or trigger an OSC
> handler — silently breaking the rendering invariants dcli relies on.

## See also

- [Scrollback](scrollback.md) — where most of your lines go.
- [The fixed region](fixed-region.md) — status rows and autocomplete display lines.
- [API reference](api-reference.md#styled-text) — exact signatures.
