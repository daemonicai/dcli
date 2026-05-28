using System.Runtime.Versioning;
using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.Windows;

/// <summary>
/// Queries the terminal size via <see cref="Console.WindowWidth"/> / <see cref="Console.WindowHeight"/>.
/// Returns an 80×24 fallback when the query fails (e.g. when stdout is not a console).
/// </summary>
/// <remarks>
/// §14.1 implements and verifies full Windows runtime behaviour. The fallback ensures the
/// cross-platform build stays green in environments without an attached console.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTerminalSizeSource : ITerminalSizeSource
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
