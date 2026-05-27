using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dcli.Terminal.Posix;

namespace Dcli.Terminal.Input;

/// <summary>
/// POSIX implementation of <see cref="IInputByteSource"/>.
/// Reads from stdin (fd 0) using <c>read(2)</c> directly, bypassing the managed
/// <see cref="System.Console"/> stream which misinterprets a 0-byte <c>VMIN=0/VTIME&gt;0</c>
/// timeout return as end-of-stream.
/// <para>
/// EINTR is retried transparently. Any other negative return is propagated as-is so the
/// reader thread can surface it.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class PosixInputByteSource : IInputByteSource
{
    // fd 0 = STDIN_FILENO (same constant value on both macOS and Linux).
    private const int _stdinFd = 0;

    /// <inheritdoc/>
    public unsafe int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        fixed (byte* ptr = buffer)
        {
            nint result;
            do
            {
                result = PosixReadInterop.read(_stdinFd, ptr, (nuint)buffer.Length);
            }
            while (result < 0 && Marshal.GetLastPInvokeError() == PosixReadInterop.EINTR);

            return (int)result;
        }
    }
}
