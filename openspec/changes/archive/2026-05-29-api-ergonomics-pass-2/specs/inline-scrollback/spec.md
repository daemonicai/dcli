## MODIFIED Requirements

### Requirement: One-way collapsible
A collapsible line-object SHALL begin collapsed (showing a summary) and SHALL expand at most once; it SHALL NOT re-collapse. The library SHALL allow a caller holding the collapsible handle to **incrementally append** lines to the collapsible's hidden-line set via `AppendLine(Line)` and a `AppendLine(string)` shorthand (the string form wrapped through `Line.FromText` with default style). An incremental append SHALL be honored only while the block is still collapsed and live: once the block has been expanded, or has frozen collapsed past the commit horizon, a further `AppendLine` SHALL be a no-op (mirroring the past-horizon no-op of `Expand`). When honored before expansion, the appended line SHALL become part of the hidden set that a subsequent `Expand` reveals.

#### Scenario: Expand reveals hidden lines
- **WHEN** a collapsed collapsible is expanded
- **THEN** its hidden lines become visible and remain visible

#### Scenario: Cannot re-collapse
- **WHEN** a collapse is requested on a collapsible
- **THEN** the request has no effect and the collapsible stays expanded once expanded

#### Scenario: Freezes collapsed at the horizon
- **WHEN** a still-collapsed collapsible commits past the horizon before being expanded
- **THEN** it freezes in the collapsed state and can no longer be expanded

#### Scenario: Append grows the hidden set before expansion
- **WHEN** a caller calls `AppendLine` on a still-collapsed, still-live collapsible and then expands it
- **THEN** the expanded content includes the appended line in append order after the originally-supplied hidden lines

#### Scenario: Append after expansion is a no-op
- **WHEN** a caller calls `AppendLine` on a collapsible that has already been expanded
- **THEN** the call has no effect and the revealed content is unchanged

#### Scenario: Append after horizon-freeze is a no-op
- **WHEN** a caller calls `AppendLine` on a collapsible that has frozen collapsed past the commit horizon
- **THEN** the call has no effect

### Requirement: Scrollback command surface
The library SHALL expose commands to append a line, append a rule/separator, append a line to a collapsible's hidden set, begin a live block, and begin a collapsible. The append-a-rule command SHALL be `IScrollback.AppendRule()`, producing a width-aware horizontal-rule line-object that spans the live-window content width at render time; it SHALL be posted as a fire-and-forget loop command like `Append`.

#### Scenario: Append a line
- **WHEN** a caller appends a styled line
- **THEN** the line is enqueued for rendering into the live window

#### Scenario: Append a rule
- **WHEN** a caller calls `AppendRule`
- **THEN** a width-aware rule line-object is enqueued and rendered as a horizontal separator spanning the live-window content width

#### Scenario: Begin a collapsible
- **WHEN** a caller begins a collapsible with a summary line
- **THEN** a collapsible line-object is created in the collapsed state showing that summary
