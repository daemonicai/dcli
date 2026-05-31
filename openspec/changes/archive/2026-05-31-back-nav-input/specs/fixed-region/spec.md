## MODIFIED Requirements

### Requirement: Awaitable modal dialogs

The library SHALL expose select, multi-select, input, and choice dialogs as awaitable operations that return a `DialogResult` whose outcome is `Submitted`, `Back`, or `Cancelled`.

Each dialog request type SHALL carry an optional **multi-line preamble** rendered top-to-bottom above the interactive widget within the overlay:

- `SelectRequest.Title` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the list items.
- `MultiSelectRequest.Title` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the list items.
- `ChoiceRequest.Prompt` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the options.
- `InputRequest.Prompt` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the input field.

Each request type SHALL expose backwards-compatible convenience constructors that accept a single `Line`, a single `string` (converted via `Line.FromText`), an `IReadOnlyList<Line>`, a `params Line[]`, an `IReadOnlyList<string>`, or a `params string[]` for the preamble. Single-`Line` and single-`string` forms SHALL be internally equivalent to passing a one-element list. When the preamble is `null` or empty, no preamble row SHALL be painted and the full overlay budget SHALL be available to the interactive widget.

`SelectRequest`, `ChoiceRequest`, `MultiSelectRequest`, and `InputRequest` SHALL each expose an opt-in `AllowBack` flag (default `false`, backward-compatible). When `AllowBack=false`, no key produces `Back` and existing v1 behaviour is preserved. When `AllowBack=true`:

- `SelectRequest` and `ChoiceRequest` SHALL produce `DialogOutcome.Back` when **Backspace** is pressed before the selection is moved (the binding introduced in `api-ergonomics-pass-1`), and SHALL additionally accept **`[`** as a secondary Back key with no movement-suppression.
- `MultiSelectRequest` SHALL produce `DialogOutcome.Back` when **`[`** is pressed at any time, regardless of whether items have been toggled. Multi-select SHALL NOT bind Backspace to `Back` — Space-toggle and Backspace interplay makes a Backspace-position heuristic unreliable, so a distinct key (`[`) is used instead.
- `InputRequest` SHALL produce `DialogOutcome.Back` when **Backspace** is pressed while the input field is currently empty (text length zero), regardless of edit history (typing then deleting back to empty SHALL still arm Back). Backspace with any text present SHALL delete the character before the caret as normal and SHALL NOT produce `Back`. Input SHALL NOT bind `[` to `Back` — `[` is a literal character users type into free-text fields (URLs, JSON, keys), so rebinding it would corrupt legitimate input.

For every request type, `DialogResult.Value` SHALL be `default` when the outcome is `Back`.

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

- **WHEN** a `SelectRequest`, `ChoiceRequest`, `MultiSelectRequest`, or `InputRequest` is constructed without setting `AllowBack`
- **THEN** Backspace and `[` have no effect on Back navigation and existing v1 behaviour is preserved (for `InputRequest`, Backspace on an empty field remains a no-op)

#### Scenario: AllowBack=true on MultiSelect produces Back via '['

- **WHEN** a `MultiSelectRequest` with `AllowBack=true` is shown and the user presses `[`
- **THEN** the awaited result is `DialogOutcome.Back`

#### Scenario: MultiSelect Back via '[' survives toggling

- **WHEN** the user toggles one or more items with Space and then presses `[` in a `MultiSelectRequest` with `AllowBack=true`
- **THEN** the awaited result is still `DialogOutcome.Back` (multi-select applies no movement-suppression to the `[` binding)

#### Scenario: Select and Choice accept '[' as a secondary Back key

- **WHEN** a `SelectRequest` or `ChoiceRequest` with `AllowBack=true` is shown and the user presses `[` before moving the selection
- **THEN** the awaited result is `DialogOutcome.Back`

#### Scenario: AllowBack=true on Input produces Back via Backspace-on-empty

- **WHEN** an `InputRequest` with `AllowBack=true` is shown with an empty field and the user presses Backspace
- **THEN** the awaited result is `DialogOutcome.Back` and `DialogResult.Value` is `default`

#### Scenario: Input Back arms after typing then deleting back to empty

- **WHEN** an `InputRequest` with `AllowBack=true` is shown, the user types text, deletes it all back to empty with Backspace, and then presses Backspace once more
- **THEN** the awaited result is `DialogOutcome.Back` (the trigger is current emptiness, not a pristine never-edited field)

#### Scenario: Input Backspace with text present deletes normally

- **WHEN** an `InputRequest` with `AllowBack=true` is shown with non-empty text and the user presses Backspace
- **THEN** the character before the caret is deleted and the dialog remains open — no `Back` is produced

#### Scenario: Input '[' is a literal character, not Back

- **WHEN** an `InputRequest` with `AllowBack=true` is shown and the user presses `[`
- **THEN** `[` is inserted into the buffer as ordinary text and the dialog remains open

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
