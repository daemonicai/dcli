namespace Dcli;

/// <summary>
/// Discriminated union of all events the library emits to the consumer.
/// Delivered on the outbound <see cref="System.Threading.Channels.Channel{T}"/> that
/// the consumer drains on its own thread. The loop never executes consumer code.
/// </summary>
/// <remarks>
/// All subtypes are publicly constructible so consumer tests can synthesize events
/// without a real terminal (Decision 12 — tier A testability).
/// </remarks>
public abstract record TerminalEvent;

/// <summary>
/// The user submitted the current input line (e.g. pressed Enter).
/// The full text of the submitted line is captured at the moment of submission.
/// </summary>
/// <param name="Text">The submitted text.</param>
public sealed record InputSubmitted(string Text) : TerminalEvent;

/// <summary>
/// The input buffer changed (e.g. a character was typed or deleted).
/// </summary>
/// <param name="Text">The current contents of the input buffer after the change.</param>
public sealed record InputChanged(string Text) : TerminalEvent;

/// <summary>
/// A key press that the fixed-region input editor did not consume was forwarded to
/// the consumer. Allows the consumer to handle application-level shortcuts.
/// </summary>
/// <param name="Key">The key event as decoded by the VT input parser.</param>
public sealed record KeyPressed(KeyEvent Key) : TerminalEvent;

/// <summary>
/// The terminal was resized. The new dimensions reflect the first authoritative size
/// measurement after the resize signal was received.
/// </summary>
/// <param name="Columns">New terminal width in columns.</param>
/// <param name="Rows">New terminal height in rows.</param>
public sealed record Resized(int Columns, int Rows) : TerminalEvent;
