using System.Runtime.Versioning;
using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Windows;

/// <summary>
/// Windows implementation of <see cref="ITerminalSizeSource"/>.
/// </summary>
/// <remarks>
/// §14.1 implements and verifies the Windows runtime path. This compile-only stub
/// returns the 80×24 safe default so the cross-platform build stays green.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTerminalSizeSource : ITerminalSizeSource
{
    /// <inheritdoc/>
    public (int Columns, int Rows) GetSize() => (80, 24);
}
