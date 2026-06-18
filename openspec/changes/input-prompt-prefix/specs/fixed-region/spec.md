## ADDED Requirements

### Requirement: Input editor prompt prefix
The owned input editor SHALL support an optional, consumer-set prompt prefix rendered immediately before the editable region on the editor's first visual row. The persistent input surface (`ITerminal.Input`) SHALL expose `SetPrompt(Line)` and `SetPrompt(string)` (the latter converted via `Line.FromText`); an empty or `null` prompt SHALL render no prefix, which is the default (unchanged v1 behaviour). `SetPrompt` SHALL NOT emit `InputChanged`. The prompt SHALL persist across renders, submissions, history recall, and buffer `Clear` until changed.

The caret SHALL be positioned immediately after the prefix while it is on the first row, accounting for the prefix's display width; the first row's text capacity SHALL be the available width reduced by the prefix width, and wrapping on the first row SHALL be computed against that reduced width. Wrapped continuation rows SHALL begin at column 0 and SHALL NOT repeat the prefix.

The prompt prefix SHALL be presentational chrome: it SHALL NOT be part of the editor buffer, SHALL NOT be returned by `Submit`, SHALL NOT appear in `InputChanged` payloads, and SHALL NOT be stored in input history.

#### Scenario: Prompt renders before the editable text
- **WHEN** the consumer calls `ITerminal.Input.SetPrompt("❯ ")` and the user types `hello`
- **THEN** the editor's first row paints `❯ hello` with the prompt in its configured style

#### Scenario: Caret sits after the prefix
- **WHEN** a prompt is set and the buffer is empty
- **THEN** the hardware cursor parks at the column immediately after the prefix, not at column 0

#### Scenario: Empty prompt renders no prefix
- **WHEN** no prompt is set (or `SetPrompt` is called with an empty/`null` value)
- **THEN** the editor renders exactly as v1 with the editable text starting at column 0

#### Scenario: Prompt persists across submissions
- **WHEN** a prompt is set once and the user submits several inputs in succession
- **THEN** the prefix remains rendered on each new input line without being re-set

#### Scenario: Prompt is not part of submitted text
- **WHEN** a prompt is set and the user types `hello` and submits
- **THEN** the submitted value is `hello` (the prefix is not included), and input history stores only `hello`

#### Scenario: First-row wrapping accounts for the prefix width
- **WHEN** a prompt is set and the user types text that exceeds `width - promptWidth` on the first row
- **THEN** the text wraps at the reduced first-row width and the continuation row begins at column 0
