namespace Dcli.Internal.Input;

/// <summary>
/// Abstracts the OS-level byte source for the §6 input reader thread.
/// <para>
/// The contract mirrors <c>VMIN=0/VTIME=1</c> POSIX <c>read(2)</c>: a call may return
/// anywhere from 0 to <c>buffer.Length</c> bytes. A return value of <strong>0</strong>
/// means "timed out with no data available" — it is <em>not</em> end-of-stream.
/// A negative return value signals an unrecoverable read error.
/// </para>
/// <para>
/// Keeping this interface internal lets §15 promote it (or an equivalent) to a public
/// <c>Dcli.Testing</c> surface with full XML docs without breaking §6.
/// </para>
/// </summary>
internal interface IInputByteSource
{
    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length bytes from the input source.
    /// </summary>
    /// <param name="buffer">Buffer to fill.</param>
    /// <returns>
    /// Number of bytes read (≥ 0). Zero means the timed read expired with no data; the
    /// caller should keep the read loop running. A negative value is a fatal read error.
    /// </returns>
    int Read(Span<byte> buffer);
}
