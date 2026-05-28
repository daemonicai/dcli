## Purpose

The `styled-text` capability defines the programmatic `Segment`/`Line`/`Style` primitive — with a `[Flags] Format` enum and a `LineBuilder` — shared across both the scrollback and fixed-region zones. There is no markup parser: styled text is built programmatically.

## Requirements

### Requirement: Programmatic styled-text model
The library SHALL represent styled text as `Segment` values (text plus a `Style`) composed into `Line` values, and SHALL NOT parse any markup string syntax.

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
