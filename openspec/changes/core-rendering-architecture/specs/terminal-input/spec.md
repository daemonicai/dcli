## ADDED Requirements

### Requirement: Raw-mode VT byte stream
The library SHALL place the terminal into raw mode and read a raw VT byte stream directly, rather than relying on `Console.ReadKey`. It SHALL target modern VT-capable terminals and SHALL NOT support the legacy non-VT console input path.

#### Scenario: Raw mode disables cooked behavior
- **WHEN** a session starts
- **THEN** canonical line buffering, local echo, and signal generation are disabled and input is delivered byte-wise

#### Scenario: Non-VT terminal is rejected
- **WHEN** the terminal is not VT-capable
- **THEN** the library fails with a clear error rather than rendering incorrectly

### Requirement: VT input parser and event model
The library SHALL parse the byte stream into input events: `KeyEvent` (with `KeyCode` being either `Char(Rune)` or a `Named` key, plus modifier flags), `PasteEvent` (a single string), and `ResizeEvent`. Each key SHALL carry a single `Rune`; grapheme clustering SHALL be handled at the editing layer, not in `KeyEvent`.

#### Scenario: Arrow key decoded
- **WHEN** the bytes `ESC [ A` are received
- **THEN** a `KeyEvent` with a `Named` code of `Up` is produced

#### Scenario: Unicode key decoded
- **WHEN** a multi-byte UTF-8 character is typed
- **THEN** a `KeyEvent` carrying `Char(Rune)` for that Unicode scalar is produced

#### Scenario: Paste delivered as one event
- **WHEN** pasted text arrives wrapped in bracketed-paste markers
- **THEN** a single `PasteEvent` carries the entire pasted text

### Requirement: Escape disambiguation by timed read
The parser SHALL distinguish a lone Escape key from the start of an escape sequence using a read timeout.

#### Scenario: Lone escape
- **WHEN** `ESC` is received and no further bytes arrive within the timeout
- **THEN** a `KeyEvent` with `Named` code `Escape` is produced

#### Scenario: Escape sequence
- **WHEN** `ESC` is immediately followed by `[ A`
- **THEN** the input is parsed as `Up` and not as `Escape`

### Requirement: Terminal-truth key reporting
Under VT encoding the library SHALL report the named key for control-character collisions (Tab, Enter, Backspace, Escape), SHALL treat Shift as implicit in printable runes, and SHALL NOT report Shift on Ctrl-letter combinations.

#### Scenario: Tab not Ctrl+I
- **WHEN** byte `0x09` is received
- **THEN** a `Tab` `KeyEvent` is produced rather than `Ctrl+I`

#### Scenario: Shift implicit in printable
- **WHEN** the character `A` is typed
- **THEN** the `KeyEvent` is `Char('A')` with no Shift modifier set

### Requirement: Ctrl+C surfaced, not acted upon
The library SHALL deliver Ctrl+C as a `KeyEvent` and SHALL NOT itself terminate or interrupt the process; interpreting Ctrl+C is the consumer's responsibility.

#### Scenario: Ctrl+C as a key event
- **WHEN** the user presses Ctrl+C
- **THEN** a `KeyEvent(Char('c'), Ctrl)` is delivered and the library does not terminate the process

### Requirement: Deferred input features
Mouse input, the Kitty keyboard protocol, and focus events SHALL be out of scope for v1, and the parser SHALL be structured so they can be added without redesign.

#### Scenario: Mouse reporting off by default
- **WHEN** a session starts
- **THEN** mouse reporting is not enabled
