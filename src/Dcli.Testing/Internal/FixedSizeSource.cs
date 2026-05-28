using Dcli.Internal.RenderLoop;

namespace Dcli.Testing.Internal;

/// <summary>
/// A <see cref="ITerminalSizeSource"/> that returns a configurable fixed size.
/// Thread-safe: <see cref="SetSize"/> may be called concurrently with <see cref="GetSize"/>.
/// </summary>
internal sealed class FixedSizeSource : ITerminalSizeSource
{
    private volatile int _columns;
    private volatile int _rows;

    internal FixedSizeSource(int columns, int rows)
    {
        _columns = columns;
        _rows = rows;
    }

    /// <inheritdoc/>
    public (int Columns, int Rows) GetSize() => (_columns, _rows);

    /// <summary>Updates the reported size. Safe to call from any thread.</summary>
    internal void SetSize(int columns, int rows)
    {
        _columns = columns;
        _rows = rows;
    }
}
