using System.Text;

namespace Dcli.Internal.RenderLoop;

/// <summary>
/// The real <see cref="IOutputSink"/> implementation that emits VT escape sequences for one frame.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Thread discipline:</strong> <see cref="Paint"/> is called exclusively on the render loop
/// thread. The injected writer must not be written from any other thread.
/// </para>
/// <para>
/// <strong>Cross-frame cursor accounting (8.1):</strong> between frames the hardware cursor
/// rests at the <em>caret row</em> when the cursor is visible and a caret position is set
/// (because <see cref="ParkCursor"/> moves it there), or on the last row of the re-render region
/// when the cursor is hidden or no caret position is provided. Each frame:
/// (1) moves up to the anchor (top of the previous region) using the stored resting row,
/// (2) emits any newly-committed rows with a trailing newline so they become frozen native-scrollback,
/// (3) clears from that point to end-of-screen (<c>ESC[0J</c>) to erase the stale region,
/// (4) repaints the current region.
/// Committed rows are never touched by subsequent frames — the anchor descends by the committed
/// count, keeping frozen content strictly above.
/// </para>
/// <para>
/// <strong>Synchronized-output fence (8.2):</strong> each frame is wrapped in
/// <c>ESC[?2026h</c> / <c>ESC[?2026l</c>. Terminals that do not recognise the DEC private
/// mode silently ignore the sequence — this is the "no-op safely where unsupported" guarantee;
/// no capability detection is needed.
/// </para>
/// <para>
/// <strong>Cursor parking/hiding (8.3):</strong> at end of frame the hardware cursor is either
/// positioned at the model caret (normal/autocomplete case) and shown, or hidden
/// (<c>ESC[?25l</c>) when <see cref="RenderModel.IsCursorVisible"/> is <see langword="false"/>
/// (modal-dialog case).
/// </para>
/// <para>
/// <strong>8.4 diff-reconciler seam:</strong> the region repaint is isolated in
/// <see cref="RepaintRegion"/>. A future per-line diff reconciler would replace that method,
/// comparing against a retained previous-frame buffer to rewrite only changed rows. v1 is a
/// full clear-then-repaint within the synchronized fence.
/// </para>
/// </remarks>
internal sealed class VtFrameRenderer : IOutputSink
{
    // VT escape-sequence constants.
    private const string _syncStart = "\x1b[?2026h";  // synchronized output: begin
    private const string _syncEnd = "\x1b[?2026l";    // synchronized output: end
    private const string _eraseEol = "\x1b[K";        // clear to end-of-line
    private const string _eraseToEos = "\x1b[0J";     // erase from cursor to end-of-screen
    private const string _cursorHide = "\x1b[?25l";   // DECTCEM hide
    private const string _cursorShow = "\x1b[?25h";   // DECTCEM show

    private readonly TextWriter _writer;
    private readonly StringBuilder _buf = new(capacity: 4096);

    // ── Cross-frame painter state ─────────────────────────────────────────────
    // After each Paint the cursor rests at _lastRestingRow (0-based, relative to the
    // region top). When the cursor is visible and a caret position is set, ParkCursor
    // moves it to the caret row, so _lastRestingRow == caretRow. Otherwise the cursor
    // rests on the last row of the region (_lastPaintedHeight - 1).
    // On the next frame we move _lastRestingRow rows up to reach the anchor.

    private int _lastPaintedHeight;  // rows in the previous frame's re-render region
    private int _lastRestingRow;     // 0-based row within the previous region where the cursor rests
    private bool _isFirstFrame = true; // true until the first Paint call completes

    /// <summary>
    /// Initialises the renderer with the given output destination.
    /// </summary>
    /// <param name="writer">
    /// The byte/text destination. Use <see cref="System.IO.StringWriter"/> for tests;
    /// use a <see cref="System.IO.StreamWriter"/> around stdout for production.
    /// </param>
    internal VtFrameRenderer(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <inheritdoc/>
    public void Paint(RenderModel model)
    {
        _buf.Clear();

        // ── Open synchronized-output fence (8.2) ─────────────────────────────
        _buf.Append(_syncStart);

        if (_isFirstFrame)
        {
            // First frame: paint at the current cursor position — no move-up needed.
            EmitCommittedRows(model.NewlyCommittedRows);
            RepaintRegion(model);
        }
        else
        {
            // ── Move to anchor (top of previous re-render region) ─────────────
            // The cursor rests at _lastRestingRow (the caret row when the cursor was
            // visible and positioned, or the last row otherwise). Moving up by exactly
            // _lastRestingRow rows reaches the anchor without intruding into frozen
            // committed content above it.
            int stepsUp = _lastRestingRow;
            if (stepsUp > 0)
            {
                _buf.Append("\x1b[");
                _buf.Append(stepsUp);
                _buf.Append('A'); // CUU: cursor up
            }
            // Return to column 1.
            _buf.Append('\r');

            // ── Emit newly-committed rows ─────────────────────────────────────
            // These print at the anchor and scroll up into native terminal history.
            // After emission the anchor has descended by the committed-row count,
            // so subsequent frames never touch them again.
            EmitCommittedRows(model.NewlyCommittedRows);

            // ── Clear old re-render region ────────────────────────────────────
            // Erase from the new anchor position (just below committed rows) to
            // end-of-screen, removing the stale previous frame content.
            _buf.Append(_eraseToEos);

            // ── Repaint current region ────────────────────────────────────────
            // 8.4 seam: RepaintRegion() is the isolated site where a future per-line
            // diff reconciler would compare against the previous frame buffer and only
            // rewrite changed rows. v1 is a full clear-then-repaint; the clear above
            // already happened, so RepaintRegion just writes the new rows.
            RepaintRegion(model);
        }

        // ── Cursor parking / hiding (8.3) ────────────────────────────────────
        int totalRows = model.LiveWindowRows.Count + model.FixedRegionRows.Count;
        ParkCursor(model, totalRows);

        // ── Close synchronized-output fence (8.2) ────────────────────────────
        _buf.Append(_syncEnd);

        // ── Flush to destination — one flush per frame ───────────────────────
        _writer.Write(_buf);
        _writer.Flush();

        // ── Update painter state for next frame ───────────────────────────────
        // _lastRestingRow is the 0-based row within this frame's region where the
        // cursor now rests. ParkCursor moves it to the caret row when the cursor is
        // visible and CaretPosition is set; otherwise it stays on the last row.
        _lastPaintedHeight = totalRows;
        _lastRestingRow = (model.IsCursorVisible && model.CaretPosition.HasValue)
            ? model.CaretPosition.Value.Row
            : (totalRows - 1);
        _isFirstFrame = false;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Emits the rows that have just been committed to native terminal scrollback.
    /// Each row is styled, followed by <c>ESC[K</c> (erase-to-EOL) and a trailing
    /// <c>\n</c> so the terminal advances the line into its scrollback history.
    /// After this method returns the cursor is at column 1 of the new anchor row.
    /// </summary>
    private void EmitCommittedRows(IReadOnlyList<Line> committed)
    {
        foreach (Line line in committed)
        {
            EmitLine(line);
            _buf.Append(_eraseEol);
            _buf.Append('\n'); // advance into native scrollback — this row is now frozen
        }
    }

    /// <summary>
    /// Repaints the bounded re-render region: live-window rows then fixed-region rows.
    /// The final row of the frame is emitted without a trailing newline. After this
    /// method returns, <see cref="ParkCursor"/> moves the cursor to the caret row
    /// (when visible and set) or leaves it on the last row (when hidden or no caret).
    /// </summary>
    /// <remarks>
    /// <strong>8.4 diff-reconciler seam:</strong> this method owns the repaint. A future
    /// implementation would accept a retained previous-frame snapshot and only emit rows
    /// that differ, rather than the current full-clear-then-repaint approach.
    /// </remarks>
    private void RepaintRegion(RenderModel model)
    {
        bool liveIsFrameFinalSection = model.FixedRegionRows.Count == 0;
        EmitRows(model.LiveWindowRows, isFrameFinalSection: liveIsFrameFinalSection);
        EmitRows(model.FixedRegionRows, isFrameFinalSection: true);
    }

    /// <summary>
    /// Positions the hardware cursor at the model caret (visible case) or hides it
    /// (modal-dialog / cursor-hidden case). Called after all content rows are emitted,
    /// with the cursor resting on the last row of the frame.
    /// </summary>
    private void ParkCursor(RenderModel model, int totalRows)
    {
        if (!model.IsCursorVisible)
        {
            _buf.Append(_cursorHide);
            return;
        }

        _buf.Append(_cursorShow);
        if (model.CaretPosition.HasValue)
        {
            // Position the cursor: row 0 is the first row of the re-render region.
            // The cursor currently rests on the last row (index totalRows-1).
            (int row, int col) = model.CaretPosition.Value;
            int stepsUp = totalRows - 1 - row;
            if (stepsUp > 0)
            {
                _buf.Append("\x1b[");
                _buf.Append(stepsUp);
                _buf.Append('A'); // CUU: cursor up
            }
            // Move to column (CHA: ESC[nG, 1-based).
            _buf.Append("\x1b[");
            _buf.Append(col + 1);
            _buf.Append('G');
        }
    }

    /// <summary>
    /// Emits a sequence of pre-wrapped rows. Each row ends with <c>ESC[K</c> (erase-to-EOL)
    /// to wipe stale trailing cells. Every row except the very last row of the entire frame
    /// is followed by a newline; the final row of the frame is emitted with no trailing
    /// newline so the cursor initially rests on the last row. <see cref="ParkCursor"/>
    /// then repositions to the caret row (when visible and set), making
    /// <c>stepsUp = totalRows - 1 - caretRow</c> correct for that repositioning.
    /// </summary>
    /// <param name="rows">The rows to emit.</param>
    /// <param name="isFrameFinalSection">
    /// <see langword="true"/> when this is the last section of the frame (the one whose
    /// last element is the overall last row). When <see langword="true"/>, the trailing
    /// newline is suppressed on the final row of <paramref name="rows"/>.
    /// </param>
    private void EmitRows(IReadOnlyList<Line> rows, bool isFrameFinalSection)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            EmitLine(rows[i]);
            _buf.Append(_eraseEol);
            bool isFinalRowOfFrame = isFrameFinalSection && i == rows.Count - 1;
            if (!isFinalRowOfFrame)
                _buf.Append('\n');
        }
    }

    /// <summary>
    /// Emits all segments of a single <see cref="Line"/> with SGR styling.
    /// Resets SGR after each styled segment so attributes do not bleed.
    /// An unstyled segment (default <see cref="Style"/>) emits no SGR at all.
    /// </summary>
    private void EmitLine(Line line)
    {
        foreach (Segment segment in line.Segments)
        {
            bool hasStyle = SgrTranslator.AppendOpenSgr(segment.Style, _buf);
            _buf.Append(segment.Text);
            if (hasStyle)
                _buf.Append(SgrTranslator.Reset);
        }
    }

    /// <summary>
    /// The number of rows painted in the most recent <see cref="Paint"/> call.
    /// Zero before the first paint.
    /// </summary>
    internal int LastPaintedHeight => _lastPaintedHeight;
}
