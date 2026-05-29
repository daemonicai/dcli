## Purpose

The `styled-text` capability defines the programmatic `Segment`/`Line`/`Style` primitive — with a `[Flags] Format` enum and a `LineBuilder` — shared across both the scrollback and fixed-region zones. There is no markup parser: styled text is built programmatically.
## Requirements
### Requirement: Programmatic styled-text model
The library SHALL represent styled text as `Segment` values (text plus a `Style`) composed into `Line` values, and SHALL NOT parse any markup string syntax. Printable text content — including markup-like characters such as `[bold]` — SHALL be preserved literally; only control and escape bytes are neutralized, as defined by the "Terminal-safe segment text" requirement.

#### Scenario: Segments retain their styles
- **WHEN** a caller composes a `Line` from multiple `Segment`s each carrying a distinct `Style`
- **THEN** each segment renders with its own style and no parsing step is performed

#### Scenario: Markup-like text is literal
- **WHEN** a segment's text contains markup-like characters such as `[bold]`
- **THEN** those characters render literally and are not interpreted as formatting

### Requirement: Formatting via a flags enum
Text attributes SHALL be expressed through a `[Flags]` `Format` enum (at minimum `None`, `Bold`, `Italic`, `Underline`, `Dim`, `Reverse`, `Strikethrough`) combinable with bitwise OR, rather than separate boolean fields.

#### Scenario: Combining attributes
- **WHEN** a `Style` specifies `Format.Bold | Format.Italic`
- **THEN** the segment renders with both bold and italic attributes

#### Scenario: No formatting
- **WHEN** a `Style` specifies `Format.None`
- **THEN** the segment renders with no attributes applied

### Requirement: Color model
`Style` SHALL support optional foreground and background colors expressible as named, 256-indexed, and 24-bit truecolor values.

#### Scenario: Truecolor foreground
- **WHEN** a `Style` sets a 24-bit RGB foreground
- **THEN** the rendered output carries that 24-bit color on a truecolor-capable terminal

#### Scenario: No color
- **WHEN** a `Style` leaves foreground and background unset
- **THEN** the segment renders using the terminal's default colors

### Requirement: Fluent line builder
The library SHALL provide a `LineBuilder` that composes a `Line` incrementally from styled fragments in append order.

#### Scenario: Building a line
- **WHEN** a caller chains builder calls adding styled fragments and then calls `Build()`
- **THEN** the returned `Line` contains the fragments as segments in the order they were added

### Requirement: Shared across both zones
The same styled-text primitives SHALL be usable both for scrollback content and for fixed-region components.

#### Scenario: Reuse in both zones
- **WHEN** a caller builds a status line and a scrollback line
- **THEN** both are expressed with the same `Segment`/`Line`/`Style` types

### Requirement: Convenience construction from plain strings

The library SHALL expose a `Line.FromText(string text, Style? style = null)` static factory and SHALL accept plain `string` values wherever a `Line` is currently required on consumer-facing construction surfaces, so label-only call sites do not have to lift through `LineBuilder`. The library SHALL NOT define an implicit conversion from `string` to `Line`; the factory and the overloads SHALL be the only seams.

The string-accepting overloads SHALL exist on:
- `IScrollback.Append(string text)` alongside `Append(Line line)`.
- `InputRequest` accepting a `string? Prompt` alongside its existing `Line? Prompt`.
- `SelectRequest` accepting `IReadOnlyList<string>` and `params string[]` items alongside its existing `Line` item list.
- `MultiSelectRequest` accepting `IReadOnlyList<string>` and `params string[]` items alongside its existing `Line` item list.
- `ChoiceRequest` accepting `IReadOnlyList<string>` and `params string[]` options alongside its existing `Line` option list.

Each overload SHALL be semantically equivalent to constructing the corresponding `Line` via `Line.FromText` with default style and passing it to the existing API.

#### Scenario: FromText produces an unstyled line

- **WHEN** a caller invokes `Line.FromText("hello")` with no style argument
- **THEN** the returned `Line` contains a single `Segment` whose text is `"hello"` and whose style is the default (no foreground/background, `Format.None`)

#### Scenario: FromText respects an explicit style

- **WHEN** a caller invokes `Line.FromText("err", new Style(Format: Format.Bold))`
- **THEN** the returned `Line` contains a single `Segment` whose text is `"err"` and whose style has `Format.Bold`

#### Scenario: String-accepting Scrollback.Append is equivalent to the Line form

- **WHEN** a caller invokes `terminal.Scrollback.Append("hello")`
- **THEN** the appended object is equal to what `terminal.Scrollback.Append(Line.FromText("hello"))` would have produced

#### Scenario: String-accepting dialog requests are equivalent to the Line form

- **WHEN** a caller constructs a `SelectRequest` (or `MultiSelectRequest` or `ChoiceRequest`) with `params string[]` or `IReadOnlyList<string>` items
- **THEN** the resulting items are equal to those produced by mapping each string through `Line.FromText` with default style

#### Scenario: InputRequest accepts a string prompt

- **WHEN** a caller constructs an `InputRequest` with a `string` prompt
- **THEN** the resulting request's effective prompt is equal to `Line.FromText(prompt)` with default style

#### Scenario: No implicit conversion is defined

- **WHEN** a consumer attempts to pass a plain `string` to an API that takes only `Line` (with no string overload added by this change)
- **THEN** the code SHALL fail to compile — there SHALL be no implicit `string`→`Line` conversion defined anywhere in the public surface

### Requirement: Terminal-safe segment text
A `Segment` constructed through any ordinary public path (its constructor, `Line.FromText`, `LineBuilder`, the string-accepting `*Request` prompt/option overloads, and the scrollback/status surfaces) SHALL hold terminal-safe text: control and escape bytes SHALL be neutralized at construction so that no such byte can reach the terminal output stream through that segment. The neutralization SHALL be applied to two byte classes:

- **Control/escape bytes** — C0 controls excluding the whitespace controls (`U+0000`–`U+0008`, `U+000E`–`U+001F`, which includes `ESC` `U+001B`), `DEL` (`U+007F`), and C1 controls (`U+0080`–`U+009F`) — SHALL be neutralized according to the configured sanitization mode (see "Configurable sanitization mode").
- **Whitespace controls** — TAB (`U+0009`), LF (`U+000A`), VT (`U+000B`), FF (`U+000C`), and CR (`U+000D`) — SHALL always be replaced by a single space (`U+0020`), regardless of mode.

Because neutralization occurs at construction, the stored text, the text measured by display-width/wrapping, and the text emitted to the terminal SHALL be identical. `DisplayWidth` and `Line` equality SHALL operate on the neutralized text. The stored text of a sanitized `Segment` SHALL be idempotent under re-sanitization.

#### Scenario: Escape byte is stripped by default
- **WHEN** a caller constructs a `Segment` (or a `Line` via `Line.FromText`) from text containing `"[2J"` and the sanitization mode is the default
- **THEN** the resulting segment's stored text contains no `ESC` byte and no `[2J`-bearing escape, and emitting the segment writes no `ESC` byte to the terminal output

#### Scenario: Newline becomes a single space
- **WHEN** a caller constructs a `Segment` from text `"line1\nline2"`
- **THEN** the resulting segment's stored text is `"line1 line2"`

#### Scenario: Tab becomes a single space
- **WHEN** a caller constructs a `Segment` from text `"a\tb"`
- **THEN** the resulting segment's stored text is `"a b"`

#### Scenario: Printable text is unchanged
- **WHEN** a caller constructs a `Segment` from text containing only printable characters (including markup-like `"[bold]"` and wide/emoji scalars)
- **THEN** the stored text equals the input text unchanged

#### Scenario: Width and stored text stay consistent
- **WHEN** a `Segment`'s input text contains control bytes that are neutralized
- **THEN** the display width measured for the segment equals the display width of its stored (neutralized) text

#### Scenario: The synchronized-output fence cannot be defeated by consumer text
- **WHEN** a frame is rendered whose content segments were constructed from consumer text containing `"[?2026l"`
- **THEN** the emitted frame's only synchronized-output mode-reset sequence is the one the renderer itself emits to close the fence, and the consumer-supplied reset never reaches the output

### Requirement: Raw (trusted) segment escape hatch
The library SHALL provide a `Segment.Raw(string text, Style style = default)` factory and a corresponding `LineBuilder.Raw(string text, Style style = default)` method that construct a segment whose text is stored and emitted **verbatim**, bypassing sanitization. This SHALL be the only construction path that allows control or escape bytes to reach the terminal output. A raw segment SHALL be distinguishable from a sanitized segment in value equality: a `Segment.Raw(t, s)` SHALL NOT be equal to a `new Segment(t, s)` even when their text and style are identical.

#### Scenario: Raw text is emitted verbatim
- **WHEN** a caller constructs a segment via `Segment.Raw("[31mred[0m")` and it is rendered
- **THEN** the exact byte sequence including the `ESC` bytes is emitted to the terminal output unchanged

#### Scenario: Raw and sanitized segments are not equal
- **WHEN** a caller compares `Segment.Raw("hi")` with `new Segment("hi")`
- **THEN** the two segments are not equal

#### Scenario: Raw is the only verbatim path
- **WHEN** consumer text containing escape bytes is supplied to any non-`Raw` construction path
- **THEN** those bytes are neutralized, and only text supplied via `Segment.Raw` / `LineBuilder.Raw` reaches the terminal output unaltered

### Requirement: Configurable sanitization mode
The transform applied to neutralized control/escape bytes SHALL be configurable via the `DCLI_SANITIZE_MODE` environment variable, read once per process. The recognized values (case-insensitive) SHALL be `strip` and `replace`; an unset, empty, or unrecognized value SHALL select `strip`. In `strip` mode each neutralized control/escape byte SHALL be removed. In `replace` mode each neutralized control/escape byte SHALL be replaced by a single visible width-1 glyph: a C0 control by its Unicode Control Picture (`U+2400`–`U+241F`), `DEL` by `U+2421`, and a C1 control by `U+FFFD`. The whitespace-control-to-space rule SHALL be unaffected by the mode.

#### Scenario: Strip mode removes the byte
- **WHEN** the mode is `strip` and a segment is constructed from `"ab"` (BEL)
- **THEN** the stored text is `"ab"`

#### Scenario: Replace mode shows a visible glyph
- **WHEN** the mode is `replace` and a segment is constructed from `"ab"` (ESC)
- **THEN** the stored text is `"a␛b"` (ESC shown as `U+241B`) and its display width is 3

#### Scenario: Unrecognized mode falls back to strip
- **WHEN** `DCLI_SANITIZE_MODE` is set to an unrecognized value
- **THEN** segments are sanitized as if the mode were `strip`

### Requirement: Single-style line shorthand factories

The library SHALL expose single-style `Line` static factories that build a one-`Segment` line from a plain `string`, so single-style label call sites do not have to lift through `LineBuilder`. The factories SHALL be:

- `Line.Bold(string text)` — a single segment whose style has `Format.Bold`.
- `Line.Dim(string text)` — a single segment whose style has `Format.Dim`.
- `Line.Fg(string text, Color foreground)` — a single segment whose style sets the given foreground color.
- `Line.Bg(string text, Color background)` — a single segment whose style sets the given background color.

Each factory SHALL be semantically equivalent to `Line.FromText(text, style)` with the corresponding single `Format`/`Color` set, and its segment SHALL be constructed through the ordinary sanitizing path (control and escape bytes neutralized exactly as for `Line.FromText`, per the "Terminal-safe segment text" requirement). The library SHALL NOT add `Italic`, `Underline`, `Reverse`, or `Strikethrough` single-style line factories in this change, and SHALL NOT add a `Line.Raw` factory — `Segment.Raw` and `LineBuilder.Raw` SHALL remain the only verbatim (unsanitized) construction seams. No implicit `string`→`Line` conversion SHALL be defined.

#### Scenario: Bold produces a single bold segment

- **WHEN** a caller invokes `Line.Bold("hi")`
- **THEN** the returned `Line` contains a single `Segment` whose text is `"hi"` and whose style has `Format.Bold` and no other attributes

#### Scenario: Dim produces a single dim segment

- **WHEN** a caller invokes `Line.Dim("note")`
- **THEN** the returned `Line` contains a single `Segment` whose text is `"note"` and whose style has `Format.Dim`

#### Scenario: Fg produces a single foreground-colored segment

- **WHEN** a caller invokes `Line.Fg("err", Color.Named(Color.AnsiColor.Red))`
- **THEN** the returned `Line` contains a single `Segment` whose text is `"err"` and whose style sets that foreground color and `Format.None`

#### Scenario: Bg produces a single background-colored segment

- **WHEN** a caller invokes `Line.Bg("sel", Color.Named(Color.AnsiColor.Blue))`
- **THEN** the returned `Line` contains a single `Segment` whose text is `"sel"` and whose style sets that background color

#### Scenario: Shorthand factories are equivalent to FromText with a style

- **WHEN** a caller invokes `Line.Bold("x")`
- **THEN** the result is equal to `Line.FromText("x", new Style(Format: Format.Bold))`

#### Scenario: Shorthand factories sanitize control bytes

- **WHEN** a caller invokes `Line.Bold("ab")` (text containing an `ESC` byte) under the default sanitization mode
- **THEN** the resulting segment's stored text contains no `ESC` byte, exactly as `Line.FromText` would have neutralized it

#### Scenario: No Raw line shorthand is defined

- **WHEN** a consumer needs a verbatim (unsanitized) single-segment line
- **THEN** there SHALL be no `Line.Raw` factory; the consumer SHALL use `new LineBuilder().Raw(text).Build()` or `Segment.Raw`, which remain the only verbatim seams

