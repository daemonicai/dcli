## Purpose

The `inline-scrollback` capability defines the append-mostly scrollback buffer — line-objects, the commit horizon, one-way collapsibles, and the content-model-to-visual-rows render contract — so styled output flows into the terminal's native scrollback while a bounded live window remains re-renderable.
## Requirements
### Requirement: Inline rendering preserves native scrollback
The library SHALL render inline and SHALL NOT use the terminal's alternate screen buffer; committed output SHALL remain in the terminal's native scrollback.

#### Scenario: Committed output stays in terminal history
- **WHEN** content has been committed and the user scrolls the terminal back
- **THEN** the prior output is visible in the terminal's native scrollback

#### Scenario: Alternate screen is not used
- **WHEN** a rendering session starts
- **THEN** the alternate screen buffer is never entered

### Requirement: Bounded live window and commit horizon
The library SHALL maintain a live, re-renderable window no taller than `terminalRows − fixedRegionHeight`; content above that window SHALL be committed (frozen, write-once) into native scrollback and never rewritten.

#### Scenario: Overflow commits the top
- **WHEN** appended content would make the live window exceed its maximum height
- **THEN** the topmost live lines are emitted into native scrollback and frozen

#### Scenario: Frozen content is never rewritten
- **WHEN** a line has been committed past the horizon
- **THEN** the library issues no further writes that target that line

### Requirement: Flat line-object model
Scrollback SHALL be a flat, ordered list of line-objects, each being either a text block or a collapsible, and each able to render itself to visual rows for a given display width.

#### Scenario: Width-aware wrapping
- **WHEN** a line-object renders at a width narrower than its content
- **THEN** it wraps to multiple visual rows

#### Scenario: Display-width counting
- **WHEN** content contains wide (CJK) or zero-width characters
- **THEN** the visual row count is computed from display width, not code-unit length

### Requirement: Live streaming block
The library SHALL provide a live block that accepts appended text and whose content may be replaced wholesale before the block is committed.

#### Scenario: Streaming append
- **WHEN** text tokens are appended to a live block
- **THEN** they appear in the live window as they arrive

#### Scenario: Replace then commit
- **WHEN** a live block's content is replaced and the block is then committed
- **THEN** the replaced content is what freezes into native scrollback

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

### Requirement: Oversized expansion reprints into flow
WHEN expanding a collapsible would make it taller than the live window, the library SHALL reprint the expanded content as normal flowing output rather than keeping it re-renderable.

#### Scenario: Expansion larger than the live window
- **WHEN** an expanded collapsible would exceed the live window height
- **THEN** its content is emitted as flowing output and scrolls into native scrollback

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

