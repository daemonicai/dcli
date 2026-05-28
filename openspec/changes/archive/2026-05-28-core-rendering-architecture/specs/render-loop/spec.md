## ADDED Requirements

### Requirement: Single-writer actor loop
All mutable UI state, stdout, and the raw-mode session SHALL be owned by a single render loop running on one dedicated thread; all state mutations SHALL be applied on that thread via an inbound channel, requiring no locks on the render model.

#### Scenario: No interleaved output
- **WHEN** content commands and keystroke-driven updates arrive concurrently from different threads
- **THEN** output is never interleaved or corrupted because a single thread owns all writes to stdout

#### Scenario: Mutations applied on the loop thread
- **WHEN** any producer posts a mutation
- **THEN** the mutation is applied on the render-loop thread and the model needs no lock

### Requirement: Fire-and-forget command API
Public command methods SHALL enqueue a message and return before the corresponding frame is painted; value reads SHALL be served from a snapshot rather than a round-trip to the loop.

#### Scenario: Command returns before paint
- **WHEN** a caller invokes a scrollback or status command
- **THEN** the method enqueues the work and returns, and the effect appears on a subsequent frame

#### Scenario: Snapshot read
- **WHEN** a caller reads a value such as the terminal size
- **THEN** the value is returned from a snapshot without blocking on the loop

### Requirement: Event-driven coalesced frames
The loop SHALL be idle when no messages are pending, SHALL drain all currently-available messages before painting, and SHALL cap painting frequency with a minimum frame interval.

#### Scenario: Burst coalescing
- **WHEN** a burst of many append commands arrives
- **THEN** they are drained and reflected in a single painted frame

#### Scenario: Idle when quiet
- **WHEN** no messages are pending
- **THEN** the loop performs no work and issues no repaint

#### Scenario: Frame-rate cap
- **WHEN** messages arrive continuously faster than the minimum frame interval
- **THEN** paints occur no more often than once per minimum frame interval

### Requirement: Decoupled outbound event stream
Events from the library to the consumer SHALL be delivered on a separate channel that the consumer drains on its own thread, and the loop SHALL NOT execute consumer code.

#### Scenario: Slow consumer does not stall rendering
- **WHEN** the consumer's handling of an outbound event is slow
- **THEN** the render loop continues painting unaffected

#### Scenario: Input submission delivered
- **WHEN** the user submits the input line
- **THEN** an `InputSubmitted` event becomes available on the outbound stream

### Requirement: Lifecycle and guaranteed restore
Starting SHALL enter raw mode and run the loop in the background; disposal SHALL stop the loop and restore the terminal; restoration SHALL also occur on an unhandled exception and on termination signals.

#### Scenario: Dispose restores the terminal
- **WHEN** the `Terminal` is disposed
- **THEN** raw mode is undone and the cursor is shown

#### Scenario: Restore on crash or signal
- **WHEN** the process terminates via an unhandled exception or a termination signal (e.g., SIGINT, SIGTERM)
- **THEN** the terminal is restored so the user's shell is not left without echo

### Requirement: Substitutable façade for consumer testing
The public terminal façade SHALL be exposed as an interface (`ITerminal`, together with its `Scrollback` / `Input` / `Status` / `Autocomplete` sub-surfaces) that a consumer can replace with its own test double, and the library SHALL NOT require static or singleton state on any consumer-facing path. All consumer-facing event and dialog-result types (`KeyEvent`, `PasteEvent`, `ResizeEvent`, the `TerminalEvent` cases, `DialogResult<T>`, `DialogOutcome`) SHALL be publicly constructible so a test can synthesize them without a real terminal.

#### Scenario: Consumer fakes the façade
- **WHEN** a consumer depends on the terminal interface and supplies its own fake implementation in a test
- **THEN** the consumer's code compiles and runs against the fake with no real terminal and no static state to reset

#### Scenario: Synthesizing events and results in a test
- **WHEN** a test needs to simulate an input event or a dialog result (e.g., an arrow key press or a `Submitted` selection)
- **THEN** it can construct the corresponding public event/result type directly, without driving the parser or the render loop

#### Scenario: Command-side calls are observable
- **WHEN** consumer code issues scrollback, status, or dialog commands against a fake façade
- **THEN** the fake can record those calls for assertion
