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
