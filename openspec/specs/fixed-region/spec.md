## Purpose

The `fixed-region` capability defines the pinned bottom component stack: an owned input editor, two mutually-exclusive overlays (a Dialog slot above the input, Autocomplete below), the reusable scrollable selection list, status lines, height budgeting, and intercept-chain key routing.

## Requirements

### Requirement: Bottom-pinned component stack
The fixed region SHALL be a contiguous, bottom-pinned stack of components — input, status, and overlays — with the live window rendered above it.

#### Scenario: Stays pinned while content streams
- **WHEN** scrollback content streams into the live window
- **THEN** the fixed region remains pinned at the bottom and is redrawn in place

### Requirement: Owned input editor

The library SHALL own an input editor supporting caret movement, multiline text, display-width-aware wrapping, history recall, and internal scrolling when its content exceeds its allotted height.

When an `InputRequest` is constructed with `IsSecret = true` and a non-empty `Default`, the editor SHALL render the seeded default as `'•'` repeated by the default's display width on every paint that occurs before the user's first edit. Once the user makes any edit (insert, delete, paste, history-recall), the editor SHALL fall back to the existing secret-render path (which masks the current buffer contents as bullets on each paint). The `Submit` outcome SHALL return the real string contents — either the unedited `Default` or the edited text — regardless of how it was rendered.

When `IsSecret = false`, the editor SHALL render the seeded default as plain text (unchanged from v1 behaviour).

#### Scenario: Wrapping tracks the caret

- **WHEN** input text exceeds the available width
- **THEN** the text wraps and the caret is positioned at the correct visual row and column

#### Scenario: History recall

- **WHEN** the user navigates input history
- **THEN** the buffer is replaced with the recalled entry

#### Scenario: Secret default is masked before first edit

- **WHEN** an `InputRequest` is shown with `IsSecret=true` and a non-empty `Default` and the user has not yet edited the buffer
- **THEN** the paint shows bullets (`•`) at every column the default occupies, never the default's clear-text content

#### Scenario: Secret default reveals real text on submit

- **WHEN** the user submits an `InputRequest` with `IsSecret=true` and a non-empty `Default` without editing the buffer
- **THEN** the awaited `DialogResult<string>.Value` is the real `Default` string (not bullets)

#### Scenario: Non-secret default renders as plain text

- **WHEN** an `InputRequest` is shown with `IsSecret=false` and a non-empty `Default`
- **THEN** the paint shows the default's clear-text content (no masking)

### Requirement: Fixed-region height budget
The fixed region SHALL have a `MaxHeight` equal to `clamp(appSet ?? 50% of rows, 8, rows)`, SHALL consume only the rows its content needs up to that cap, and SHALL scroll components internally beyond the cap.

#### Scenario: Default cap
- **WHEN** no `MaxHeight` is configured on a 24-row terminal
- **THEN** the cap is 12 rows

#### Scenario: Minimum floor
- **WHEN** 50% of the terminal height is below 8 rows and the terminal has at least 8 rows
- **THEN** the cap is 8 rows

#### Scenario: Tiny terminal
- **WHEN** the terminal has fewer than 8 rows
- **THEN** the cap is the terminal height

### Requirement: Mutually-exclusive overlays
At most one overlay SHALL be active at a time — a Dialog above the input or Autocomplete below it; opening a Dialog SHALL suppress any active Autocomplete, and Autocomplete SHALL NOT open while a Dialog is active.

#### Scenario: Dialog suppresses autocomplete
- **WHEN** a Dialog opens while Autocomplete is showing
- **THEN** the Autocomplete overlay is hidden

#### Scenario: Single active overlay
- **WHEN** a Dialog is active and an Autocomplete show is requested
- **THEN** the Autocomplete does not appear

### Requirement: Intercept-chain key routing
Keys SHALL be routed through an ordered chain with the active overlay first, then the input editor; each component either consumes a key or passes it to the next.

#### Scenario: Overlay consumes navigation keys
- **WHEN** Autocomplete is active and the up or down arrow is pressed
- **THEN** Autocomplete consumes the key and the input caret does not move

#### Scenario: Unhandled keys fall through
- **WHEN** Autocomplete is active and a printable key is pressed
- **THEN** the key falls through to the input editor and is inserted

#### Scenario: Modal dialog captures all keys
- **WHEN** a modal Dialog is active
- **THEN** it consumes every key and the input editor receives none

### Requirement: Single cursor placement
The hardware cursor SHALL park at the input caret normally and during Autocomplete, SHALL be hidden while a modal Dialog is active, and overlay selection SHALL be shown by styling rather than the cursor.

#### Scenario: Cursor stays in input during autocomplete
- **WHEN** the user arrows through Autocomplete candidates
- **THEN** the hardware cursor remains at the input caret and the selected candidate is shown highlighted

#### Scenario: Modal dialog hides the cursor
- **WHEN** a modal Dialog is active
- **THEN** the hardware cursor is hidden

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

### Requirement: Consumer-driven autocomplete
Autocomplete candidates SHALL be supplied by the consumer in response to input-change notifications; the library SHALL render and navigate them within the overlay's row cap and apply the accepted candidate's insert text to the input buffer.

#### Scenario: Render supplied candidates
- **WHEN** the consumer supplies candidates
- **THEN** they render below the input up to the overlay's maximum rows with internal scrolling

#### Scenario: Accept a candidate
- **WHEN** the user accepts a highlighted candidate
- **THEN** the candidate's insert text is applied to the input buffer
