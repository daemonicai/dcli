## ADDED Requirements

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
