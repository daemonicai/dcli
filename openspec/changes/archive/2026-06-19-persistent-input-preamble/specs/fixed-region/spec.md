## MODIFIED Requirements

### Requirement: Bottom-pinned component stack
The fixed region SHALL be a contiguous, bottom-pinned stack of components — a persistent input preamble, the input editor, status, and overlays — with the live window rendered above it. When set, the persistent input preamble SHALL render directly above the input editor band; the status band SHALL remain the bottommost, sacred band.

#### Scenario: Stays pinned while content streams
- **WHEN** scrollback content streams into the live window
- **THEN** the fixed region remains pinned at the bottom and is redrawn in place

#### Scenario: Preamble sits directly above the input editor
- **WHEN** a persistent input preamble is set and the base input editor is active
- **THEN** the preamble rows render immediately above the input editor band and below the live window

## ADDED Requirements

### Requirement: Persistent input preamble
The library SHALL expose a persistent input-preamble surface — `ITerminal.InputPreamble` — through which the consumer sets a sequence of styled `Line` rows pinned directly above the base input editor. The surface SHALL mirror the status surface: it SHALL provide `SetRows(params Line[])` and `SetRows(IReadOnlyList<Line>)` overloads that replace the preamble content, and an empty argument SHALL clear it. Updates SHALL be applied on the render-loop thread and SHALL persist across renders and successive input submissions until changed or cleared.

When the preamble is `null` or empty, no preamble row SHALL be painted and the freed rows SHALL return to the fixed-region budget. The preamble SHALL participate in the fixed-region height budget: under budget pressure the preamble SHALL truncate before the input editor loses its last usable row, and the status band SHALL remain sacred (never squeezed).

The preamble SHALL be presentational only — it SHALL NOT participate in the intercept chain and SHALL NOT consume keys; all keys SHALL reach the active overlay or the input editor as before, and the hardware cursor SHALL continue to park at the input caret.

#### Scenario: Preamble renders above the input editor
- **WHEN** the consumer sets a two-line preamble via `ITerminal.InputPreamble.SetRows(...)`
- **THEN** both rows paint top-to-bottom immediately above the input editor on the next frame

#### Scenario: Preamble persists across input submissions
- **WHEN** a preamble is set once and the user submits several inputs in succession
- **THEN** the preamble remains rendered above the editor across each turn without being re-set

#### Scenario: Empty preamble clears it
- **WHEN** `SetRows` is called with an empty argument list
- **THEN** no preamble rows are painted and the freed rows return to the fixed-region budget

#### Scenario: Preamble truncates under budget pressure
- **WHEN** the preamble row count plus the input editor's minimum height plus the status rows exceeds the fixed-region cap
- **THEN** the preamble truncates first, the input editor retains at least one usable row, and the status rows remain fully rendered

#### Scenario: Preamble does not intercept keys
- **WHEN** keys are routed while a preamble is set and no overlay is active
- **THEN** the preamble consumes no keys and every key reaches the input editor
