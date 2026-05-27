namespace Dcli;

/// <summary>
/// Thrown when dcli detects that the current terminal does not support the VT escape sequences
/// required for operation.
/// <para>
/// This exception is thrown by <c>TerminalCapabilityDetector.EnsureVtCapable</c> before
/// raw mode is entered, so the terminal is always in a known good state when the exception
/// propagates.
/// </para>
/// <para>
/// Typical causes:
/// <list type="bullet">
///   <item>The process is not attached to a tty (stdout is redirected to a file or pipe).</item>
///   <item><c>TERM=dumb</c> or <c>TERM</c> is empty, indicating a terminal that does not support
///     VT control sequences.</item>
///   <item>Running on an unsupported platform (not Linux, macOS, or Windows).</item>
/// </list>
/// </para>
/// </summary>
public sealed class TerminalNotSupportedException : Exception
{
    /// <summary>
    /// Initialises a new instance of <see cref="TerminalNotSupportedException"/> with no message.
    /// </summary>
    public TerminalNotSupportedException()
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="TerminalNotSupportedException"/> with a
    /// descriptive message.
    /// </summary>
    /// <param name="message">Human-readable description of why the terminal is not supported.</param>
    public TerminalNotSupportedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="TerminalNotSupportedException"/> with a message
    /// and an inner exception.
    /// </summary>
    /// <param name="message">Human-readable description of why the terminal is not supported.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public TerminalNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
