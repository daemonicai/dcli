using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.FixedRegion;

/// <summary>
/// Composes the fixed region (input editor + status line) into paint-state, enforcing the
/// <c>MaxHeight</c> budget and setting <see cref="RenderModel.FixedRegionRows"/>,
/// <see cref="RenderModel.CaretPosition"/>, and <see cref="RenderModel.IsCursorVisible"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Height budget:</strong>
/// <c>cap = (rows &lt; 8) ? rows : clamp(appSet ?? rows/2, 8, rows)</c>
/// where <c>appSet</c> is <see cref="RenderModel.MaxFixedHeight"/>.
/// The fixed region consumes only the rows its content needs, up to the cap; it is never
/// padded to the cap.
/// </para>
/// <para>
/// <strong>Component stack (bottom-to-top in the frame, index 0 = topmost rendered first):</strong>
/// <list type="number">
///   <item>Input editor rows (top of fixed region, internally scrolled if needed).</item>
///   <item>Status rows (bottom of fixed region, always sacred).</item>
/// </list>
/// §11 overlays slot between the input and status; that wiring is deferred to §11.
/// </para>
/// <para>
/// <strong>Priority order when budget is tight (Decision 8):</strong>
/// <list type="number">
///   <item>Status is sacred — always fully shown.</item>
///   <item>Caret line is always visible — input scrolls internally via
///     <see cref="TextBuffer.Render(int, int?)"/>.</item>
///   <item>Any remaining rows go to the overlay (§11, not here).</item>
/// </list>
/// </para>
/// <para>
/// <strong>Degenerate case (status alone ≥ cap):</strong>
/// If <c>statusRows.Count ≥ cap</c>, there is no room for the input editor.
/// Status is still shown in full (clamped to the terminal height so we never exceed
/// <see cref="RenderModel.Rows"/>). The input is given 0 allotted rows and renders its
/// single caret row at minimum — a single caret-line row is appended above the status only
/// if <c>cap &gt; statusRows.Count</c> would produce a non-negative allotment; otherwise
/// the caret is notionally "behind" the status and <see cref="RenderModel.IsCursorVisible"/>
/// is set to <see langword="false"/> to avoid confusing the painter.
/// Rule: when <c>statusRows.Count &gt;= cap</c>, the fixed region = status rows only and
/// the caret is hidden.
/// </para>
/// <para>
/// <strong>Composition happens before scrollback PrePaint:</strong>
/// <see cref="RenderModel.FixedRegionRows"/> must be populated before §9's PrePaint so that
/// the live-window budget (<c>rows − fixedRegionRows.Count</c>) is correct.
/// </para>
/// </remarks>
internal sealed class FixedRegionComposer
{
    private readonly StatusLine _status;

    internal FixedRegionComposer(TextBuffer editor, StatusLine status)
    {
        Editor = editor;
        _status = status;
    }

    /// <summary>
    /// The input editor. The loop thread routes editing <see cref="KeyEvent"/>s directly to
    /// this object and then calls <see cref="Compose"/> to recompose the fixed region.
    /// </summary>
    internal TextBuffer Editor { get; }

    /// <summary>
    /// Computes the cap for the fixed region given the terminal height and the optional
    /// consumer-supplied <paramref name="appSet"/> value.
    /// </summary>
    /// <param name="rows">Terminal height in rows.</param>
    /// <param name="appSet">
    /// Consumer-supplied cap from <see cref="TerminalOptions.MaxFixedHeight"/>.
    /// <see langword="null"/> means "use 50% of rows".
    /// </param>
    /// <returns>
    /// The effective cap in rows:
    /// <list type="bullet">
    ///   <item><c>rows</c> when <c>rows &lt; 8</c> (tiny terminal: floor yields to terminal height).</item>
    ///   <item><c>clamp(appSet ?? rows/2, 8, rows)</c> otherwise.</item>
    /// </list>
    /// </returns>
    internal static int ComputeCap(int rows, int? appSet)
    {
        if (rows < 8)
            return rows;

        int desired = appSet ?? rows / 2;
        return Math.Clamp(desired, 8, rows);
    }

    /// <summary>
    /// Composes the fixed region into the render model's paint-state fields.
    /// Must be called <em>before</em> <c>ScrollbackModel.PrePaint</c> so that
    /// <see cref="RenderModel.FixedRegionRows"/> is set when the live-window budget is computed.
    /// </summary>
    internal void Compose(RenderModel model)
    {
        int width = model.Columns;
        int rows = model.Rows;
        int cap = ComputeCap(rows, model.MaxFixedHeight);

        IReadOnlyList<Line> statusRows = _status.Rows;
        int statusCount = statusRows.Count;

        // Degenerate: status alone fills or exceeds the cap — no room for the editor.
        // Status is shown in full (clamped to terminal height); caret is hidden.
        if (statusCount >= cap)
        {
            // Clamp status to terminal rows so we never write off-screen.
            IReadOnlyList<Line> visibleStatus = statusCount > rows
                ? statusRows.Take(rows).ToList()
                : statusRows;

            model.FixedRegionRows = visibleStatus;
            model.EditorCaretLocal = null;
            model.CaretPosition = null;
            model.IsCursorVisible = false;
            return;
        }

        // Normal path: input gets inputAllot rows; status is always shown beneath it.
        int inputAllot = cap - statusCount;

        RenderResult editorResult = Editor.Render(width, inputAllot);
        IReadOnlyList<Line> inputRows = editorResult.VisibleRows;
        (int editorCaretRow, int editorCaretCol) = editorResult.CaretPosition;

        // Stack: input rows on top, status rows at bottom.
        List<Line> fixedRows = new(inputRows.Count + statusCount);
        fixedRows.AddRange(inputRows);
        fixedRows.AddRange(statusRows);

        model.FixedRegionRows = fixedRows;

        // CaretPosition is relative to the full frame (LiveWindowRows ++ FixedRegionRows).
        // LiveWindowRows.Count is not yet known here (scrollback PrePaint runs after us).
        // Record the editor-local (row, col); LoopEngine offsets by LiveWindowRows.Count
        // after PrePaint to produce the final frame-relative position.
        model.EditorCaretLocal = (editorCaretRow, editorCaretCol);
        model.IsCursorVisible = true;
    }
}
