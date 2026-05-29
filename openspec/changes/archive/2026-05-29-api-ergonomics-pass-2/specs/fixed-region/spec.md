## MODIFIED Requirements

### Requirement: Owned input editor

The library SHALL own an input editor supporting caret movement, multiline text, display-width-aware wrapping, history recall, paste insertion, and internal scrolling when its content exceeds its allotted height.

A `PasteEvent` delivered to the active input surface SHALL have its entire text inserted at the caret as a single edit, with the same display-width-aware and multiline-aware semantics as typed character insertion (the caret advances past the inserted text and wrapping is recomputed). Paste SHALL be routed through the intercept chain like other input: while a modal Dialog or `InputDialog` is active it is consumed by that overlay's editor; otherwise it is applied to the base input editor.

When an `InputRequest` is constructed with `IsSecret = true` and a non-empty `Default`, the editor SHALL render the seeded default as `'•'` repeated by the default's display width on every paint that occurs before the user's first edit. Any edit — insert, delete, **paste**, or history-recall — SHALL count as the user's first edit, after which the editor SHALL fall back to the existing secret-render path (which masks the current buffer contents as bullets on each paint). The `Submit` outcome SHALL return the real string contents — either the unedited `Default` or the edited text — regardless of how it was rendered.

When `IsSecret = false`, the editor SHALL render the seeded default as plain text (unchanged from v1 behaviour).

#### Scenario: Wrapping tracks the caret

- **WHEN** input text exceeds the available width
- **THEN** the text wraps and the caret is positioned at the correct visual row and column

#### Scenario: History recall

- **WHEN** the user navigates input history
- **THEN** the buffer is replaced with the recalled entry

#### Scenario: Paste inserts text at the caret

- **WHEN** a `PasteEvent` is delivered while the input editor is focused
- **THEN** the event's entire text is inserted at the caret position and the caret advances to the end of the inserted text

#### Scenario: Paste into a multiline buffer wraps correctly

- **WHEN** a `PasteEvent` whose text exceeds the available width is delivered
- **THEN** the inserted text wraps display-width-aware across rows and the caret is positioned at the correct visual row and column

#### Scenario: Paste counts as a first edit for a secret default

- **WHEN** an `InputRequest` with `IsSecret=true` and a non-empty `Default` is shown and the user's first interaction is a `PasteEvent`
- **THEN** the editor switches from default-masking to buffer-masking on the next paint (the seeded default is no longer the rendered content) and `Submit` returns the real edited buffer text

#### Scenario: Secret default is masked before first edit

- **WHEN** an `InputRequest` is shown with `IsSecret=true` and a non-empty `Default` and the user has not yet edited the buffer
- **THEN** the paint shows bullets (`•`) at every column the default occupies, never the default's clear-text content

#### Scenario: Secret default reveals real text on submit

- **WHEN** the user submits an `InputRequest` with `IsSecret=true` and a non-empty `Default` without editing the buffer
- **THEN** the awaited `DialogResult<string>.Value` is the real `Default` string (not bullets)

#### Scenario: Non-secret default renders as plain text

- **WHEN** an `InputRequest` is shown with `IsSecret=false` and a non-empty `Default`
- **THEN** the paint shows the default's clear-text content (no masking)

### Requirement: Awaitable modal dialogs

The library SHALL expose select, multi-select, input, and choice dialogs as awaitable operations that return a `DialogResult` whose outcome is `Submitted`, `Back`, or `Cancelled`.

Each dialog request type SHALL carry an optional **multi-line preamble** rendered top-to-bottom above the interactive widget within the overlay:

- `SelectRequest.Title` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the list items.
- `MultiSelectRequest.Title` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the list items.
- `ChoiceRequest.Prompt` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the options.
- `InputRequest.Prompt` SHALL be typed `IReadOnlyList<Line>?` and SHALL render as a sequence of styled rows above the input field.

Each request type SHALL expose backwards-compatible convenience constructors that accept a single `Line`, a single `string` (converted via `Line.FromText`), an `IReadOnlyList<Line>`, a `params Line[]`, an `IReadOnlyList<string>`, or a `params string[]` for the preamble. Single-`Line` and single-`string` forms SHALL be internally equivalent to passing a one-element list. When the preamble is `null` or empty, no preamble row SHALL be painted and the full overlay budget SHALL be available to the interactive widget.

`SelectRequest`, `ChoiceRequest`, and `MultiSelectRequest` SHALL each expose an opt-in `AllowBack` flag (default `false`, backward-compatible). When `AllowBack=false`, no key produces `Back` and existing v1 behaviour is preserved. When `AllowBack=true`:

- `SelectRequest` and `ChoiceRequest` SHALL produce `DialogOutcome.Back` when **Backspace** is pressed before the selection is moved (the binding introduced in `api-ergonomics-pass-1`), and SHALL additionally accept **`[`** as a secondary Back key with no movement-suppression.
- `MultiSelectRequest` SHALL produce `DialogOutcome.Back` when **`[`** is pressed at any time, regardless of whether items have been toggled. Multi-select SHALL NOT bind Backspace to `Back` — Space-toggle and Backspace interplay makes a Backspace-position heuristic unreliable, so a distinct key (`[`) is used instead.

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

- **WHEN** a `SelectRequest`, `ChoiceRequest`, or `MultiSelectRequest` is constructed without setting `AllowBack`
- **THEN** Backspace and `[` have no effect on the dialog and existing v1 behaviour is preserved

#### Scenario: AllowBack=true on MultiSelect produces Back via '['

- **WHEN** a `MultiSelectRequest` with `AllowBack=true` is shown and the user presses `[`
- **THEN** the awaited result is `DialogOutcome.Back`

#### Scenario: MultiSelect Back via '[' survives toggling

- **WHEN** the user toggles one or more items with Space and then presses `[` in a `MultiSelectRequest` with `AllowBack=true`
- **THEN** the awaited result is still `DialogOutcome.Back` (multi-select applies no movement-suppression to the `[` binding)

#### Scenario: Select and Choice accept '[' as a secondary Back key

- **WHEN** a `SelectRequest` or `ChoiceRequest` with `AllowBack=true` is shown and the user presses `[` before moving the selection
- **THEN** the awaited result is `DialogOutcome.Back`

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
