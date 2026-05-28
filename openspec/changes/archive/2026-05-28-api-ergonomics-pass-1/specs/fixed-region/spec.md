## MODIFIED Requirements

### Requirement: Awaitable modal dialogs

The library SHALL expose select, multi-select, input, and choice dialogs as awaitable operations that return a `DialogResult` whose outcome is `Submitted`, `Back`, or `Cancelled`.

`SelectRequest` and `ChoiceRequest` SHALL expose an opt-in `AllowBack` flag (default `false`). When `AllowBack` is `true`, the dialog SHALL produce `DialogOutcome.Back` if the user presses **Backspace** before moving the selection (i.e. before any `↑`/`↓` keystroke is consumed by the dialog). Once the selection has been moved, Backspace SHALL be a no-op for the remainder of that overlay session. When `AllowBack` is `false` (the default), Backspace SHALL have no effect on `SelectRequest`/`ChoiceRequest` overlays — preserving v1 behaviour.

`MultiSelectRequest` SHALL NOT expose `AllowBack` in this revision; its Back-semantics are deferred.

`InputRequest` SHALL NOT expose `AllowBack`; in an input dialog Backspace is an editing key.

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
