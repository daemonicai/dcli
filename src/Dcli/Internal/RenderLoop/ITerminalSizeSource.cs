namespace Dcli.Internal.RenderLoop;

/// <summary>
/// Supplies the initial terminal dimensions for the render model's volatile size snapshot.
/// </summary>
/// <remarks>
/// §13 will wire a real SIGWINCH-aware implementation. Tests inject a fixed-size stub.
/// The loop reads the initial size once at start-up and thereafter receives updates via
/// resize messages on the inbound channel.
/// </remarks>
internal interface ITerminalSizeSource
{
    /// <summary>Returns the current terminal size as (columns, rows).</summary>
    (int Columns, int Rows) GetSize();
}
