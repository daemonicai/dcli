## ADDED Requirements

### Requirement: Headless terminal over the real engine
The testing surface SHALL provide a `HeadlessTerminal` that implements the same public terminal interface as the production façade and runs the real render loop, layout, overlay routing, and scrollback model with no terminal, no raw-mode session, and no real stdout. It SHALL be constructed by substituting only the OS-facing edges (raw-mode session, input source, terminal-size source, output sink, clock); the load-bearing engine SHALL NOT be reimplemented or mocked.

#### Scenario: Drives the real engine without a terminal
- **WHEN** a test starts a `HeadlessTerminal`
- **THEN** the real render loop, layout, and scrollback run against in-memory edges, requiring no TTY, no raw mode, and no real stdout

#### Scenario: One engine, swapped edges
- **WHEN** the production terminal and the headless terminal are compared
- **THEN** they share a single render-loop / layout / scrollback implementation and differ only in the OS-facing edge implementations

### Requirement: Scripted input
The harness SHALL let a test inject input at both the byte level and the event level: raw bytes (exercising the real `VtInputParser`), synthesized key events, literal typed text, pasted text, and resize events.

#### Scenario: Feed raw bytes through the parser
- **WHEN** a test feeds a raw byte sequence (e.g., the escape sequence for an arrow key)
- **THEN** the real parser decodes it and the resulting event flows through the loop as if it had been read from a terminal

#### Scenario: Inject high-level input
- **WHEN** a test types text, sends a named key, pastes text, or triggers a resize
- **THEN** the corresponding input is delivered to the loop without going through the OS input path

### Requirement: Deterministic settling
The harness SHALL provide an awaitable settle operation that drains all currently-pending inbound work and produces exactly one coalesced frame while advancing no wall-clock time, so assertions are deterministic and require no sleeps.

#### Scenario: Settle produces a single frame
- **WHEN** a test posts several commands and/or input events and then awaits settle
- **THEN** all pending work is applied and exactly one frame is produced, with no real time elapsed

### Requirement: Virtual clock for cadence tests
The harness SHALL expose a controllable virtual clock so that time-dependent behaviour (frame-interval throttling, burst-coalescing windows, idle) can be tested by advancing time explicitly rather than by sleeping.

#### Scenario: Advance time to trigger a throttled paint
- **WHEN** a test advances the virtual clock past the minimum frame interval while a paint is pending
- **THEN** the throttled paint becomes due, with no real time elapsed

### Requirement: Structured frame snapshot
The harness SHALL expose the rendered result as a structured snapshot — the live-window visual rows as styled text runs, the fixed-region layout, the caret position, and the active overlay — suitable for golden-frame assertions. Assertions over raw output bytes SHALL be confined to a separate fidelity check and SHALL NOT be the primary observation surface.

#### Scenario: Assert a rendered frame logically
- **WHEN** a test reads the snapshot after settling
- **THEN** it can assert on the visual rows, their styles, the caret position, and the active overlay without parsing escape sequences

### Requirement: Independent, documented testing package
The harness SHALL ship as a separate package (`Dcli.Testing`) with a documented public API, and production code SHALL NOT depend on it. The structured snapshot SHALL be a testing-only contract that production behaviour and consumer semantics do not depend on.

#### Scenario: Production takes no dependency on the harness
- **WHEN** the production `dcli` package is built and consumed
- **THEN** it has no dependency on `Dcli.Testing`, and the harness can be versioned independently
