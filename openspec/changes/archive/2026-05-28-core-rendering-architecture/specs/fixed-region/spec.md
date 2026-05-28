## ADDED Requirements

### Requirement: Bottom-pinned component stack
The fixed region SHALL be a contiguous, bottom-pinned stack of components — input, status, and overlays — with the live window rendered above it.

#### Scenario: Stays pinned while content streams
- **WHEN** scrollback content streams into the live window
- **THEN** the fixed region remains pinned at the bottom and is redrawn in place

### Requirement: Owned input editor
The library SHALL own an input editor supporting caret movement, multiline text, display-width-aware wrapping, history recall, and internal scrolling when its content exceeds its allotted height.

#### Scenario: Wrapping tracks the caret
- **WHEN** input text exceeds the available width
- **THEN** the text wraps and the caret is positioned at the correct visual row and column

#### Scenario: History recall
- **WHEN** the user navigates input history
- **THEN** the buffer is replaced with the recalled entry

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

### Requirement: Consumer-driven autocomplete
Autocomplete candidates SHALL be supplied by the consumer in response to input-change notifications; the library SHALL render and navigate them within the overlay's row cap and apply the accepted candidate's insert text to the input buffer.

#### Scenario: Render supplied candidates
- **WHEN** the consumer supplies candidates
- **THEN** they render below the input up to the overlay's maximum rows with internal scrolling

#### Scenario: Accept a candidate
- **WHEN** the user accepts a highlighted candidate
- **THEN** the candidate's insert text is applied to the input buffer
