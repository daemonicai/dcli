using System.Runtime.Versioning;

namespace Dcli.Terminal.Input;

/// <summary>
/// Windows implementation of <see cref="IInputByteSource"/>.
/// </summary>
/// <remarks>
/// §14.1 implements and verifies the Windows runtime path. This compile-only stub
/// keeps the cross-platform build green on all CI agents.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsInputByteSource : IInputByteSource
{
    /// <inheritdoc/>
    public int Read(Span<byte> buffer)
    {
        // TODO (§14.1): implement using ReadFile / WaitForSingleObject with a timeout
        // so the read loop mirrors the POSIX VMIN=0/VTIME=1 contract (0 = timeout, keep going).
        throw new NotImplementedException("Windows input byte source is implemented in §14.1.");
    }
}
