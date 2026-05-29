## ADDED Requirements

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
