using Dcli.Internal.RenderLoop;

namespace Dcli.Internal.FixedRegion;

/// <summary>
/// Composes the fixed region (input editor + optional overlay + status line) into paint-state,
/// enforcing the <c>MaxHeight</c> budget and setting <see cref="RenderModel.FixedRegionRows"/>,
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
///   <item>Overlay rows (above input, when <see cref="IOverlay.Placement"/> is <c>AboveInput</c>).</item>
///   <item>Input editor rows (internally scrolled if needed).</item>
///   <item>Overlay rows (below input, when <see cref="IOverlay.Placement"/> is <c>BelowInput</c>).</item>
///   <item>Status rows (always sacred — always at the very bottom).</item>
/// </list>
/// </para>
/// <para>
/// <strong>Priority order when budget is tight (Decision 8):</strong>
/// <list type="number">
///   <item>Status is sacred — always fully shown.</item>
///   <item>Caret line is always visible — input scrolls internally via
///     <see cref="TextBuffer.Render(int, int?)"/>.</item>
///   <item>The overlay absorbs the squeeze — capped at the remainder of the budget
///     (up to <c>DefaultOverlayMaxRows</c>). In the common 1–2 row input case the
///     overlay gets the bulk of the budget.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Degenerate case (status alone ≥ cap):</strong>
/// If <c>statusRows.Count ≥ cap</c>, there is no room for the input editor or any overlay.
/// Status is shown in full (clamped to terminal height); the caret is hidden.
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
    /// The status line component. Exposed so façade commands can set
    /// <see cref="StatusLine.Rows"/> on the loop thread via <c>model.FixedRegion.Status</c>.
    /// </summary>
    internal StatusLine Status => _status;

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

    // Maximum rows the overlay may occupy when no consumer-supplied limit exists.
    // §12 will make this configurable via TerminalOptions.
    private const int _defaultOverlayMaxRows = 10;

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

        // Degenerate: status alone fills or exceeds the cap — no room for the editor or overlays.
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

        // Normal path.
        // budget = rows available for input + overlay (status is sacred, already accounted for).
        int budget = cap - statusCount; // > 0 here

        // Input keeps the caret visible: allot the full budget so the editor can scroll
        // internally to expose the caret line. Renders exactly min(natural, budget) rows.
        RenderResult editorResult = Editor.Render(width, budget);
        IReadOnlyList<Line> inputRows = editorResult.VisibleRows;
        (int editorCaretRow, int editorCaretCol) = editorResult.CaretPosition;

        // Overlay absorbs the squeeze: whatever is left of the budget, capped at DefaultOverlayMaxRows.
        // Do NOT read overlay.MaxRows back after writing it — compute overlayCap from budget and
        // inputRows.Count to prevent stale values from blocking re-expansion when the budget grows.
        IOverlay? overlay = model.ActiveOverlay;
        IReadOnlyList<Line> overlayRows = [];
        if (overlay is not null)
        {
            int overlayCap = Math.Clamp(budget - inputRows.Count, 0, _defaultOverlayMaxRows);
            overlay.MaxRows = overlayCap; // sets the viewport for this frame (clamped to ≥1 by ScrollableList)
            // Only render when the budget permits at least one row; otherwise the overlay is
            // effectively squeezed to zero and we skip rendering to honour the cap proof.
            if (overlayCap > 0)
                overlayRows = overlay.Render(width); // ≤ overlayCap rows
        }

        // Assemble: [overlay if AboveInput] [input] [overlay if BelowInput] [status].
        // Proof that total ≤ cap: overlayRows.Count ≤ budget − inputRows.Count,
        // so overlayRows.Count + inputRows.Count ≤ budget = cap − statusCount,
        // thus total = overlayRows.Count + inputRows.Count + statusCount ≤ cap.
        int aboveCount = (overlay?.Placement == OverlayPlacement.AboveInput) ? overlayRows.Count : 0;

        List<Line> fixedRows = new(overlayRows.Count + inputRows.Count + statusCount);
        if (overlay?.Placement == OverlayPlacement.AboveInput)
            fixedRows.AddRange(overlayRows);
        fixedRows.AddRange(inputRows);
        if (overlay?.Placement == OverlayPlacement.BelowInput)
            fixedRows.AddRange(overlayRows);
        fixedRows.AddRange(statusRows);

        model.FixedRegionRows = fixedRows;

        // Cursor placement (priority order):
        //   1. HidesCursor (modal list dialog) → hidden.
        //   2. Overlay supplies CaretInOverlay AND overlay rows were actually rendered → cursor
        //      at the overlay's caret (input dialog case).
        //   3. Normal → cursor at the main editor caret.
        // EditorCaretLocal is relative to the START of the fixed region; the above-input overlay
        // rows are folded in here so the loop's existing CaretPosition finalisation is correct.
        if (overlay is not null && overlay.HidesCursor)
        {
            model.EditorCaretLocal = null;
            model.CaretPosition = null;
            model.IsCursorVisible = false;
        }
        else if (overlay is not null &&
                 overlayRows.Count > 0 &&
                 overlay.CaretInOverlay is { } overlayCaret)
        {
            // The overlay owns the cursor (e.g. InputDialog). overlayStartRow is 0 for
            // AboveInput overlays and inputRows.Count for BelowInput overlays.
            int overlayStartRow = overlay.Placement == OverlayPlacement.AboveInput
                ? 0
                : inputRows.Count;
            model.EditorCaretLocal = (overlayStartRow + overlayCaret.Row, overlayCaret.Col);
            model.IsCursorVisible = true;
        }
        else
        {
            // aboveCount offsets the caret row by any rows inserted above the input.
            model.EditorCaretLocal = (aboveCount + editorCaretRow, editorCaretCol);
            model.IsCursorVisible = true;
        }
    }
}
