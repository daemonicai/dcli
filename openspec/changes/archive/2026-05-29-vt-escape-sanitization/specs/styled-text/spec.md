## MODIFIED Requirements

### Requirement: Programmatic styled-text model
The library SHALL represent styled text as `Segment` values (text plus a `Style`) composed into `Line` values, and SHALL NOT parse any markup string syntax. Printable text content — including markup-like characters such as `[bold]` — SHALL be preserved literally; only control and escape bytes are neutralized, as defined by the "Terminal-safe segment text" requirement.

#### Scenario: Segments retain their styles
- **WHEN** a caller composes a `Line` from multiple `Segment`s each carrying a distinct `Style`
- **THEN** each segment renders with its own style and no parsing step is performed

#### Scenario: Markup-like text is literal
- **WHEN** a segment's text contains markup-like characters such as `[bold]`
- **THEN** those characters render literally and are not interpreted as formatting

## ADDED Requirements

### Requirement: Terminal-safe segment text
A `Segment` constructed through any ordinary public path (its constructor, `Line.FromText`, `LineBuilder`, the string-accepting `*Request` prompt/option overloads, and the scrollback/status surfaces) SHALL hold terminal-safe text: control and escape bytes SHALL be neutralized at construction so that no such byte can reach the terminal output stream through that segment. The neutralization SHALL be applied to two byte classes:

- **Control/escape bytes** — C0 controls excluding the whitespace controls (`U+0000`–`U+0008`, `U+000E`–`U+001F`, which includes `ESC` `U+001B`), `DEL` (`U+007F`), and C1 controls (`U+0080`–`U+009F`) — SHALL be neutralized according to the configured sanitization mode (see "Configurable sanitization mode").
- **Whitespace controls** — TAB (`U+0009`), LF (`U+000A`), VT (`U+000B`), FF (`U+000C`), and CR (`U+000D`) — SHALL always be replaced by a single space (`U+0020`), regardless of mode.

Because neutralization occurs at construction, the stored text, the text measured by display-width/wrapping, and the text emitted to the terminal SHALL be identical. `DisplayWidth` and `Line` equality SHALL operate on the neutralized text. The stored text of a sanitized `Segment` SHALL be idempotent under re-sanitization.

#### Scenario: Escape byte is stripped by default
- **WHEN** a caller constructs a `Segment` (or a `Line` via `Line.FromText`) from text containing `"[2J"` and the sanitization mode is the default
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
- **WHEN** a frame is rendered whose content segments were constructed from consumer text containing `"[?2026l"`
- **THEN** the emitted frame's only synchronized-output mode-reset sequence is the one the renderer itself emits to close the fence, and the consumer-supplied reset never reaches the output

### Requirement: Raw (trusted) segment escape hatch
The library SHALL provide a `Segment.Raw(string text, Style style = default)` factory and a corresponding `LineBuilder.Raw(string text, Style style = default)` method that construct a segment whose text is stored and emitted **verbatim**, bypassing sanitization. This SHALL be the only construction path that allows control or escape bytes to reach the terminal output. A raw segment SHALL be distinguishable from a sanitized segment in value equality: a `Segment.Raw(t, s)` SHALL NOT be equal to a `new Segment(t, s)` even when their text and style are identical.

#### Scenario: Raw text is emitted verbatim
- **WHEN** a caller constructs a segment via `Segment.Raw("[31mred[0m")` and it is rendered
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
- **WHEN** the mode is `strip` and a segment is constructed from `"ab"` (BEL)
- **THEN** the stored text is `"ab"`

#### Scenario: Replace mode shows a visible glyph
- **WHEN** the mode is `replace` and a segment is constructed from `"ab"` (ESC)
- **THEN** the stored text is `"a␛b"` (ESC shown as `U+241B`) and its display width is 3

#### Scenario: Unrecognized mode falls back to strip
- **WHEN** `DCLI_SANITIZE_MODE` is set to an unrecognized value
- **THEN** segments are sanitized as if the mode were `strip`
