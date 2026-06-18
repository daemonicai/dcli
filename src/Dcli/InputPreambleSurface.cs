using Dcli.Internal.RenderLoop;

namespace Dcli;

/// <summary>
/// Consumer-facing surface for setting the preamble rows rendered directly above the input editor.
/// </summary>
/// <remarks>
/// <para>
/// All methods post fire-and-forget <see cref="Internal.RenderLoop.ILoopCommand"/>s; they
/// return before the command is applied or a frame is painted.
/// </para>
/// <para>
/// <strong>Presentational only:</strong> the preamble is not part of the key-routing intercept
/// chain. Setting rows has no effect on keyboard input handling.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> all methods are safe to call from any thread.
/// </para>
/// </remarks>
public sealed class InputPreambleSurface : IInputPreamble
{
    private readonly LoopEngine _loop;

    internal InputPreambleSurface(LoopEngine loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
    }

    /// <summary>
    /// Replaces the preamble content with the given rows.
    /// An empty argument list clears the preamble (no rows rendered above the editor).
    /// </summary>
    public void SetRows(params Line[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _loop.Post(new SetInputPreambleCommand(rows));
    }

    /// <summary>
    /// Replaces the preamble content with the given rows.
    /// Passing an empty list clears the preamble.
    /// </summary>
    public void SetRows(IReadOnlyList<Line> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _loop.Post(new SetInputPreambleCommand([.. rows]));
    }

    // ── Command ────────────────────────────────────────────────────────────────

    private sealed class SetInputPreambleCommand : ILoopCommand
    {
        private readonly IReadOnlyList<Line> _rows;

        internal SetInputPreambleCommand(IReadOnlyList<Line> rows) => _rows = rows;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.FixedRegion.Preamble.Rows = _rows;
            model.MarkDirty();
        }
    }
}
