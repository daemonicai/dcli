using System.Runtime.Versioning;
using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Posix;

/// <summary>
/// Queries the terminal size via <see cref="Console.WindowWidth"/> / <see cref="Console.WindowHeight"/>,
/// which delegate to the runtime's native shim (<c>libSystem.Native</c>) rather than a hand-rolled
/// <c>ioctl(TIOCGWINSZ)</c> P/Invoke.
/// Returns an 80×24 fallback when the query fails (e.g. in CI without a tty).
/// </summary>
/// <remarks>
/// The hand-rolled ioctl P/Invoke caused an AV on AArch64 Darwin because <c>ioctl(2)</c> is
/// variadic and the AArch64 ABI passes variadic args on the stack; the runtime's shim is
/// non-variadic and has no such problem. See commit message for details.
/// </remarks>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class PosixTerminalSizeSource : ITerminalSizeSource
{
    private const int _fallbackColumns = 80;
    private const int _fallbackRows = 24;

    /// <inheritdoc/>
    public (int Columns, int Rows) GetSize()
    {
        int cols, rows;
        try { cols = Console.WindowWidth; } catch (IOException) { cols = 0; }
        try { rows = Console.WindowHeight; } catch (IOException) { rows = 0; }

        if (cols > 0 && rows > 0)
            return (cols, rows);

        return (_fallbackColumns, _fallbackRows);
    }
}
