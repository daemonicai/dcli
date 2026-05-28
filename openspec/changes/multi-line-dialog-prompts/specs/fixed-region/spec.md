## MODIFIED Requirements

### Requirement: Awaitable modal dialogs

The library SHALL expose select, multi-select, input, and choice dialogs as awaitable operations that return a `DialogResult` whose outcome is `Submitted`, `Back`, or `Cancelled`.

Each dialog request type SHALL carry an optional **multi-line preamble** rendered top-to-bottom above the interactive widget within the overlay:

- `SelectRequest.Title` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the list items.
- `MultiSelectRequest.Title` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the list items.
- `ChoiceRequest.Prompt` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the options.
- `InputRequest.Prompt` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the input field.

Each request type SHALL expose backwards-compatible convenience constructors that accept a single `Line`, a single `string` (converted via `Line.FromText`), an `IReadOnlyList<Line>`, a `params Line[]`, an `IReadOnlyList<string>`, or a `params string[]` for the preamble. Single-`Line` and single-`string` forms SHALL be internally equivalent to passing a one-element list. When the preamble is `null` or empty, no preamble row SHALL be painted and the full overlay budget SHALL be available to the interactive widget.

`SelectRequest` and `ChoiceRequest` SHALL continue to expose the opt-in `AllowBack` flag (default `false`) introduced in `api-ergonomics-pass-1`. `MultiSelectRequest` SHALL continue to omit `AllowBack`. `InputRequest` SHALL continue to omit `AllowBack`.

#### Scenario: Select submitted

- **WHEN** the user highlights an item in a select dialog and presses Enter
- **THEN** the awaited result is `Submitted` carrying the chosen index

#### Scenario: Cancelled by escape

- **WHEN** the user presses Escape in a dialog
- **THEN** the awaited result is `Cancelled`

#### Scenario: Multi-select toggling

- **WHEN** the user presses space on items in a multi-select dialog
- **THEN** those items toggle in the returned selection set

#### Scenario: Cancellation token closes the dialog

- **WHEN** the `CancellationToken` passed to a dialog is cancelled
- **THEN** the overlay closes and the awaited result is `Cancelled`

#### Scenario: AllowBack=true on Select produces Back

- **WHEN** a `SelectRequest` with `AllowBack=true` is shown and the user presses Backspace before moving the selection
- **THEN** the awaited result is `DialogOutcome.Back`

#### Scenario: AllowBack=true on Choice produces Back

- **WHEN** a `ChoiceRequest` with `AllowBack=true` is shown and the user presses Backspace before moving the selection
- **THEN** the awaited result is `DialogOutcome.Back`

#### Scenario: AllowBack=true is suppressed after movement

- **WHEN** the user presses `↓` (consumed by the dialog) and then presses Backspace in a `SelectRequest` with `AllowBack=true`
- **THEN** Backspace is ignored for the rest of that overlay session — neither `Back` nor `Cancelled` is produced

#### Scenario: AllowBack=false is the default

- **WHEN** a `SelectRequest` or `ChoiceRequest` is constructed without setting `AllowBack`
- **THEN** Backspace has no effect on the dialog and existing v1 behaviour is preserved

#### Scenario: Multi-line preamble renders all lines above the widget

- **WHEN** a dialog request is constructed with a preamble containing multiple `Line`s
- **THEN** the overlay paints each preamble line in order, top-to-bottom, immediately above the interactive widget (list / options / input field)

#### Scenario: Single-Line preamble constructor still works

- **WHEN** a dialog request is constructed via the single-`Line` convenience constructor (e.g. `new ChoiceRequest(options, prompt: someLine)`)
- **THEN** the overlay paints exactly one preamble row, semantically identical to passing a one-element list

#### Scenario: Single-string preamble constructor still works

- **WHEN** a dialog request is constructed via the single-`string` convenience constructor (e.g. `new ChoiceRequest(options, prompt: "Permission:")`)
- **THEN** the string is wrapped via `Line.FromText` and a single preamble row is painted with the default style

#### Scenario: Null or empty preamble paints no preamble row

- **WHEN** a dialog request is constructed with a `null` or empty-list preamble
- **THEN** no preamble rows are painted and the full overlay budget is available to the interactive widget

#### Scenario: Multi-line preamble truncates when over budget

- **WHEN** a preamble's line count plus the interactive widget's minimum height exceeds the overlay's available rows
- **THEN** the preamble truncates per the existing overlay budget arithmetic (the same behaviour live-blocks have shipped since v1); the interactive widget remains usable
