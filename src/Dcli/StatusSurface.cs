using Dcli.Internal.RenderLoop;

namespace Dcli;

/// <summary>
/// Consumer-facing surface for setting the status bar rows at the bottom of the fixed region.
/// </summary>
/// <remarks>
/// <para>
/// All methods post fire-and-forget <see cref="Internal.RenderLoop.ILoopCommand"/>s; they
/// return before the command is applied or a frame is painted.
/// </para>
/// <para>
/// <strong>Sacred status:</strong> the status bar is always fully rendered regardless of height
/// budget. When the terminal is very short and the budget is exhausted by the status rows, the
/// input editor is squeezed or hidden; the status is never truncated.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> all methods are safe to call from any thread.
/// </para>
/// </remarks>
public sealed class StatusSurface : IStatus
{
    private readonly LoopEngine _loop;

    internal StatusSurface(LoopEngine loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
    }

    /// <summary>
    /// Replaces the status bar content with the given rows.
    /// An empty argument list clears the status bar (no rows rendered).
    /// </summary>
    public void SetRows(params Line[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _loop.Post(new SetStatusCommand(rows));
    }

    /// <summary>
    /// Replaces the status bar content with the given rows.
    /// Passing an empty list clears the status bar.
    /// </summary>
    public void SetRows(IReadOnlyList<Line> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _loop.Post(new SetStatusCommand([.. rows]));
    }

    // ── Command ────────────────────────────────────────────────────────────────

    private sealed class SetStatusCommand : ILoopCommand
    {
        private readonly IReadOnlyList<Line> _rows;

        internal SetStatusCommand(IReadOnlyList<Line> rows) => _rows = rows;

        void ILoopCommand.Apply(RenderModel model)
        {
            model.FixedRegion.Status.Rows = _rows;
            model.MarkDirty();
        }
    }
}
