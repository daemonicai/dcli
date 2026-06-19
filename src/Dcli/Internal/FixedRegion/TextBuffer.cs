using System.Globalization;
using System.Text;

namespace Dcli.Internal.FixedRegion;

/// <summary>
/// Owns the editable input text: caret position, multiline support, display-width-aware wrapping,
/// internal scroll, and history recall. All editing operations are synchronous; thread safety is
/// the caller's responsibility (the render-loop thread owns this in normal use).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Caret representation:</strong> the caret is stored as a UTF-16 char index into
/// <see cref="Text"/>. All public navigation/deletion methods operate in terms of
/// <em>grapheme clusters</em> (UAX #29 extended grapheme clusters) — one arrow key = one visual
/// character regardless of how many chars or Runes it occupies.
/// </para>
/// <para>
/// <strong>Grapheme clustering:</strong> uses <see cref="StringInfo.GetTextElementEnumerator(string)"/>
/// to enumerate extended grapheme clusters. Width of a cluster = sum of its scalars' display
/// widths via <see cref="DisplayWidth"/>.
/// </para>
/// <para>
/// <strong>Wrapping:</strong> each logical line (split on <c>\n</c>) is wrapped to visual rows
/// using <see cref="LineWrapper.Wrap"/>. The caret maps to a <c>(visualRow, visualCol)</c> by
/// counting cluster widths up to the caret index within its logical line, then offsetting by
/// the wrapped-row index of the preceding logical lines.
/// </para>
/// <para>
/// <strong>Internal scroll:</strong> <see cref="Render"/> accepts an <c>allottedHeight</c>. When
/// the total visual rows exceed it, the visible window is scrolled by the minimum amount needed
/// to keep the caret row in view (caret row always visible; scroll anchors to caret; top of
/// window moves as little as possible).
/// </para>
/// <para>
/// <strong>History recall:</strong> readline-style semantics. When the user navigates back into
/// history (<see cref="RecallPrevious"/>), the current in-progress buffer is stashed. Navigating
/// forward past the newest entry via <see cref="RecallNext"/> restores the stash. Calling
/// <see cref="AddToHistory"/> clears the navigation position and the stash.
/// </para>
/// </remarks>
internal sealed class TextBuffer
{
    // ── Text storage ────────────────────────────────────────────────────────────────────────

    private string _text = string.Empty;

    /// <summary>The raw text content including embedded newlines.</summary>
    internal string Text => _text;

    // ── Caret ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// UTF-16 char index of the caret within <see cref="Text"/>. Always in [0, Text.Length].
    /// </summary>
    internal int CaretIndex { get; private set; }

    /// <summary>
    /// Sets the caret index directly. For test use only — no validation beyond clamping.
    /// </summary>
    internal void SetCaretIndexForTest(int index)
    {
        CaretIndex = Math.Clamp(index, 0, _text.Length);
    }

    // ── History ─────────────────────────────────────────────────────────────────────────────

    private readonly List<string> _history = [];

    /// <summary>
    /// Index into _history of the currently recalled entry, or -1 when not in history mode.
    /// -1 = editing the live buffer.
    /// History navigation: 0 = oldest, _history.Count-1 = newest.
    /// </summary>
    private int _historyIndex = -1;

    /// <summary>
    /// The in-progress buffer stashed when the user first navigates back into history.
    /// Restored when navigating forward past the newest entry.
    /// </summary>
    private string? _historyStash;

    // ── Prompt ─────────────────────────────────────────────────────────────────────────────────

    private Line _prompt = new([]);

    /// <summary>
    /// Sets the prompt prefix rendered before the editable region on the first visual row.
    /// An empty line removes the prefix.
    /// </summary>
    internal void SetPrompt(Line prompt)
    {
        _prompt = prompt;
    }

    // ── Helpers: grapheme cluster iteration ─────────────────────────────────────────────────

    /// <summary>
    /// Enumerates grapheme clusters in <paramref name="text"/> as (startCharIndex, clusterString)
    /// pairs. Uses <see cref="StringInfo.GetTextElementEnumerator(string)"/> for UAX #29 clusters.
    /// </summary>
    private static IEnumerable<(int Start, string Cluster)> EnumerateClusters(string text)
    {
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return (enumerator.ElementIndex, enumerator.GetTextElement());
        }
    }

    /// <summary>
    /// Returns the display width of a grapheme cluster string: sum of each scalar's
    /// <see cref="DisplayWidth.Measure(Rune)"/> value. Tabs are measured column-relative.
    /// </summary>
    private static int ClusterWidth(string cluster, int column)
    {
        int w = 0;
        foreach (Rune rune in cluster.EnumerateRunes())
        {
            int runeW = DisplayWidth.MeasureWithColumn(rune, column + w);
            w += runeW;
        }
        return w;
    }

    // ── Edit operations ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a single Unicode scalar at the caret, advancing the caret past it.
    /// </summary>
    internal void Insert(Rune rune)
    {
        string s = rune.ToString();
        _text = _text[..CaretIndex] + s + _text[CaretIndex..];
        CaretIndex += s.Length;
    }

    /// <summary>
    /// Inserts a string (e.g., a paste payload) at the caret, advancing the caret past it.
    /// </summary>
    internal void Insert(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = _text[..CaretIndex] + text + _text[CaretIndex..];
        CaretIndex += text.Length;
    }

    /// <summary>
    /// Inserts a newline at the caret (multiline support).
    /// </summary>
    internal void InsertNewline()
    {
        _text = _text[..CaretIndex] + "\n" + _text[CaretIndex..];
        CaretIndex += 1;
    }

    /// <summary>
    /// Deletes the grapheme cluster immediately before the caret (backspace semantics).
    /// No-op when the caret is at position 0.
    /// </summary>
    internal void Backspace()
    {
        if (CaretIndex == 0)
            return;

        (int clusterStart, int clusterLength) = FindClusterBefore(CaretIndex);
        _text = _text[..clusterStart] + _text[(clusterStart + clusterLength)..];
        CaretIndex = clusterStart;
    }

    /// <summary>
    /// Deletes the grapheme cluster immediately after the caret (delete/forward-delete semantics).
    /// No-op when the caret is at the end of the text.
    /// </summary>
    internal void Delete()
    {
        if (CaretIndex >= _text.Length)
            return;

        string cluster = FindClusterAt(CaretIndex);
        _text = _text[..CaretIndex] + _text[(CaretIndex + cluster.Length)..];
        // Caret stays at same position.
    }

    /// <summary>
    /// Replaces the entire buffer with <paramref name="newText"/> and moves the caret to the end.
    /// Used by history recall and test helpers.
    /// </summary>
    internal void SetText(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        _text = newText;
        CaretIndex = _text.Length;
    }

    /// <summary>Clears the buffer and resets the caret to 0.</summary>
    internal void Clear()
    {
        _text = string.Empty;
        CaretIndex = 0;
    }

    // ── Caret navigation ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the caret one grapheme cluster to the left.
    /// When the caret is at the start of a logical line (after a newline), moves to the end of
    /// the previous logical line (i.e., crosses logical-line boundaries).
    /// No-op at position 0.
    /// </summary>
    internal void MoveLeft()
    {
        if (CaretIndex == 0)
            return;

        (int clusterStart, _) = FindClusterBefore(CaretIndex);
        CaretIndex = clusterStart;
    }

    /// <summary>
    /// Moves the caret one grapheme cluster to the right.
    /// Crosses logical-line boundaries.
    /// No-op at the end of the text.
    /// </summary>
    internal void MoveRight()
    {
        if (CaretIndex >= _text.Length)
            return;

        string cluster = FindClusterAt(CaretIndex);
        CaretIndex += cluster.Length;
    }

    /// <summary>
    /// Moves the caret to the start of the current visual row.
    /// <para>
    /// Convention: Home navigates to the start of the <em>visual row</em> the caret occupies,
    /// not the logical line. This matches readline/terminal editor behaviour.
    /// </para>
    /// </summary>
    /// <param name="width">Terminal width used for wrapping.</param>
    internal void MoveHome(int width)
    {
        VisualPosition vp = ComputeVisualPosition(width);
        // Find the char index at the start of the current visual row.
        CaretIndex = vp.CurrentVisualRowStartCharIndex;
    }

    /// <summary>
    /// Moves the caret to the end of the current visual row.
    /// <para>
    /// Convention: End navigates to the end of the <em>visual row</em> the caret occupies.
    /// </para>
    /// </summary>
    /// <param name="width">Terminal width used for wrapping.</param>
    internal void MoveEnd(int width)
    {
        VisualPosition vp = ComputeVisualPosition(width);
        CaretIndex = vp.CurrentVisualRowEndCharIndex;
    }

    /// <summary>
    /// Moves the caret up one visual row, maintaining the current visual column where possible.
    /// Moves to position 0 when already on the first visual row.
    /// </summary>
    /// <param name="width">Terminal width used for wrapping.</param>
    internal void MoveUp(int width)
    {
        VisualPosition vp = ComputeVisualPosition(width);
        if (vp.VisualRow == 0)
        {
            CaretIndex = 0;
            return;
        }

        // Target: row above, same visual column.
        int targetRow = vp.VisualRow - 1;
        int targetCol = vp.VisualCol;
        CaretIndex = FindCaretIndexForVisualPosition(targetRow, targetCol, width);
    }

    /// <summary>
    /// Moves the caret down one visual row, maintaining the current visual column where possible.
    /// Moves to the end of text when already on the last visual row.
    /// </summary>
    /// <param name="width">Terminal width used for wrapping.</param>
    internal void MoveDown(int width)
    {
        List<VisualRowInfo> rows = BuildVisualRows(width);
        VisualPosition vp = ComputeVisualPositionFromRows(rows);

        if (vp.VisualRow >= rows.Count - 1)
        {
            CaretIndex = _text.Length;
            return;
        }

        int targetRow = vp.VisualRow + 1;
        int targetCol = vp.VisualCol;
        CaretIndex = FindCaretIndexForVisualPosition(targetRow, targetCol, width);
    }

    // ── History ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="entry"/> to history. Resets history navigation position and clears
    /// any stash. Duplicate consecutive entries are not added.
    /// </summary>
    internal void AddToHistory(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_history.Count == 0 || _history[^1] != entry)
            _history.Add(entry);
        _historyIndex = -1;
        _historyStash = null;
    }

    /// <summary>
    /// Recalls the previous (older) history entry, replacing the buffer.
    /// On first call, stashes the current in-progress buffer for later restore.
    /// No-op when already at the oldest entry.
    /// </summary>
    internal void RecallPrevious()
    {
        if (_history.Count == 0)
            return;

        if (_historyIndex == -1)
        {
            // First navigation into history: stash the live buffer.
            _historyStash = _text;
            _historyIndex = _history.Count - 1;
        }
        else if (_historyIndex > 0)
        {
            _historyIndex--;
        }
        else
        {
            // Already at oldest; no-op.
            return;
        }

        SetText(_history[_historyIndex]);
    }

    /// <summary>
    /// Recalls the next (newer) history entry, replacing the buffer.
    /// When navigating past the newest entry, restores the stashed in-progress buffer.
    /// No-op when not in history navigation mode.
    /// </summary>
    internal void RecallNext()
    {
        if (_historyIndex == -1)
            return;

        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            SetText(_history[_historyIndex]);
        }
        else
        {
            // Past the newest: restore stash.
            _historyIndex = -1;
            string stash = _historyStash ?? string.Empty;
            _historyStash = null;
            SetText(stash);
        }
    }

    // ── Render ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the editor content at the given <paramref name="width"/>, applying an optional
    /// height constraint. Returns the visible visual rows and the caret's position within them.
    /// </summary>
    /// <param name="width">Terminal width in columns (must be >= 1).</param>
    /// <param name="allottedHeight">
    /// Maximum number of rows the editor may occupy. When <see langword="null"/> or larger than
    /// the total visual rows, all rows are returned (no scrolling). When smaller, the visible
    /// window is scrolled by the minimum amount needed to keep the caret row in view.
    /// </param>
    /// <returns>
    /// The visible rows as <see cref="Line"/> values (ready to paint) and the caret's
    /// <c>(visualRow, visualCol)</c> relative to the <em>visible</em> window (0-based).
    /// </returns>
    internal RenderResult Render(int width, int? allottedHeight = null)
    {
        if (width < 1)
            width = 1;

        List<VisualRowInfo> allRows = BuildVisualRows(width);
        VisualPosition vp = ComputeVisualPositionFromRows(allRows);

        int totalRows = allRows.Count;
        int visibleStart; // first visible row index (0-based into allRows)
        int visibleEnd;   // exclusive

        if (allottedHeight is null || allottedHeight >= totalRows)
        {
            visibleStart = 0;
            visibleEnd = totalRows;
        }
        else
        {
            int h = Math.Max(1, allottedHeight.Value);
            // Scroll minimally to keep caret row in view.
            // Preferred: keep caret as close to center as practical, but minimum movement.
            // The window is [visibleStart, visibleStart+h). Caret row must be within it.
            // Minimum movement: if caret is already visible in whatever window fits, preserve
            // the current top; otherwise snap to bring caret into view.
            // Since we rebuild from scratch each render, we use: top = clamp(caret - h + 1, 0, max).
            // This keeps caret at the bottom of the window when scrolling down, and at the top when
            // scrolling up — i.e., the caret is always visible and the window moves as little as
            // possible. This is the standard "scroll just enough" anchoring.
            visibleStart = Math.Clamp(vp.VisualRow - h + 1, 0, totalRows - h);
            visibleEnd = visibleStart + h;
        }

        // Build the visible row lines.
        Line[] visibleLines = new Line[visibleEnd - visibleStart];
        for (int i = visibleStart; i < visibleEnd; i++)
        {
            visibleLines[i - visibleStart] = allRows[i].Line;
        }

        int caretVisualRow = vp.VisualRow - visibleStart;
        int caretVisualCol = vp.VisualCol;

        return new RenderResult(visibleLines, (caretVisualRow, caretVisualCol));
    }

    // ── Visual row construction ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A single visual row: the <see cref="Line"/> to paint and the range of the source text
    /// this row covers (as char indices into <see cref="Text"/>).
    /// </summary>
    private sealed class VisualRowInfo
    {
        internal Line Line { get; }

        /// <summary>Char index of the first character of this visual row in <see cref="Text"/>.</summary>
        internal int StartCharIndex { get; }

        /// <summary>Char index one past the last character of this visual row in <see cref="Text"/>.</summary>
        internal int EndCharIndex { get; }

        internal VisualRowInfo(Line line, int startCharIndex, int endCharIndex)
        {
            Line = line;
            StartCharIndex = startCharIndex;
            EndCharIndex = endCharIndex;
        }
    }

    /// <summary>
    /// Computes all visual rows for the current text at the given width.
    /// Each logical line (delimited by <c>\n</c>) is independently wrapped.
    /// When a prompt prefix is set, its width is subtracted from the first row's available width
    /// and its segments are prepended to the first visual row's <see cref="Line"/>.
    /// </summary>
    private List<VisualRowInfo> BuildVisualRows(int width)
    {
        if (width < 1)
            width = 1;

        int promptWidth = MeasureLineWidth(_prompt);

        // Split text into logical lines on \n and wrap each independently.
        List<VisualRowInfo> result = [];

        // Edge case: empty text → one empty row (caret can sit here).
        if (_text.Length == 0)
        {
            Line emptyRow = promptWidth > 0 ? new Line([.. _prompt.Segments]) : new Line([]);
            result.Add(new VisualRowInfo(emptyRow, 0, 0));
            return result;
        }

        int searchFrom = 0;
        while (true)
        {
            int newlinePos = _text.IndexOf('\n', searchFrom);

            // Extract the logical line (the text before the next newline, or to end of string).
            string logicalLine = newlinePos == -1
                ? _text[searchFrom..]
                : _text[searchFrom..newlinePos];

            // On the very first row ever produced, reduce available width by the prompt.
            bool isFirstLogicalLine = result.Count == 0;
            int effectiveWidth = (isFirstLogicalLine && promptWidth > 0)
                ? Math.Max(1, width - promptWidth)
                : width;

            // Wrap the logical line into visual rows.
            Line sourceLine = new([new Segment(logicalLine)]);
            IReadOnlyList<Line> wrappedRows = LineWrapper.Wrap(sourceLine, effectiveWidth);

            // Assign char ranges: each wrapped row covers a contiguous span of logicalLine.
            int charInLogical = 0;
            bool firstWrappedRow = true;
            foreach (Line wRow in wrappedRows)
            {
                int rowChars = wRow.Segments.Sum(s => s.Text.Length);
                int rowStart = searchFrom + charInLogical;

                // Prepend prompt segments to the very first visual row.
                Line paintRow = (isFirstLogicalLine && firstWrappedRow && promptWidth > 0)
                    ? new Line([.. _prompt.Segments, .. wRow.Segments])
                    : wRow;

                result.Add(new VisualRowInfo(paintRow, rowStart, rowStart + rowChars));
                charInLogical += rowChars;
                firstWrappedRow = false;
            }

            if (newlinePos == -1)
                break;

            // Advance past the newline character. If this puts us at or past the end,
            // the next iteration processes an empty logical line "" which produces one empty
            // row — correct behaviour for a trailing newline (caret can sit there).
            searchFrom = newlinePos + 1;
        }

        return result;
    }

    // ── Caret↔Visual position mapping ───────────────────────────────────────────────────────

    /// <summary>
    /// Computed visual position of the caret: which visual row and column.
    /// Also carries helper values for Home/End navigation.
    /// </summary>
    private readonly struct VisualPosition
    {
        /// <summary>Zero-based visual row index (across all logical lines).</summary>
        internal int VisualRow { get; init; }

        /// <summary>Zero-based visual column (display columns from left edge).</summary>
        internal int VisualCol { get; init; }

        /// <summary>
        /// Char index of the first character on the current visual row (for Home).
        /// </summary>
        internal int CurrentVisualRowStartCharIndex { get; init; }

        /// <summary>
        /// Char index one past the last character on the current visual row (for End).
        /// </summary>
        internal int CurrentVisualRowEndCharIndex { get; init; }
    }

    private VisualPosition ComputeVisualPosition(int width)
    {
        List<VisualRowInfo> rows = BuildVisualRows(width);
        return ComputeVisualPositionFromRows(rows);
    }

    /// <summary>
    /// Returns whether the caret is on the first visual row and whether it is on the last,
    /// given the terminal <paramref name="width"/>. Used by key routing to decide when Up/Down
    /// should trigger history navigation instead of intra-buffer cursor movement.
    /// </summary>
    internal (bool IsFirst, bool IsLast) GetCaretVisualRowBounds(int width)
    {
        List<VisualRowInfo> rows = BuildVisualRows(width);
        VisualPosition vp = ComputeVisualPositionFromRows(rows);
        return (vp.VisualRow == 0, vp.VisualRow >= rows.Count - 1);
    }

    private VisualPosition ComputeVisualPositionFromRows(List<VisualRowInfo> rows)
    {
        // Find which visual row contains the caret.
        // The caret sits at CaretIndex. A visual row owns char range [StartCharIndex, EndCharIndex).
        // Special cases:
        //   - Caret at end of text: belongs to the last row.
        //   - Caret exactly at a non-newline wrap boundary (end of one visual row / start of next):
        //     belongs to the NEXT row (the caret is at the start of the new visual row, col 0).
        //     Exception: if the wrap boundary is a newline char, the caret belongs to THIS row
        //     (the caret is positioned before the newline, which is not displayed).

        int caretIdx = CaretIndex;

        for (int i = 0; i < rows.Count; i++)
        {
            VisualRowInfo row = rows[i];
            bool isLastRow = i == rows.Count - 1;

            // The caret belongs to this row if:
            //   caretIdx is within [StartCharIndex, EndCharIndex), OR
            //   this is the last row and caretIdx == EndCharIndex (end of text).
            bool inRange = caretIdx >= row.StartCharIndex &&
                           (caretIdx < row.EndCharIndex || (isLastRow && caretIdx == row.EndCharIndex));

            // Also: if caretIdx == row.EndCharIndex and this row ends at a newline position,
            // the caret belongs to this row (before the newline, which is not displayed).
            // The next row starts at EndCharIndex + 1.
            if (!inRange && !isLastRow && caretIdx == row.EndCharIndex)
            {
                // EndCharIndex is either the newline position within the logical line or the
                // wrap boundary. If the char at EndCharIndex in _text is '\n', the caret is
                // at the end of this logical line (before the newline).
                if (row.EndCharIndex < _text.Length && _text[row.EndCharIndex] == '\n')
                    inRange = true;
            }

            if (inRange)
            {
                // Compute visual column: measure display width of text from row.StartCharIndex
                // to caretIdx, using grapheme clusters and DisplayWidth.
                string beforeCaret = _text[row.StartCharIndex..caretIdx];
                int visualCol = MeasureDisplayWidth(beforeCaret);

                // On the first visual row, offset the column by the prompt width.
                if (i == 0)
                    visualCol += MeasureLineWidth(_prompt);

                return new VisualPosition
                {
                    VisualRow = i,
                    VisualCol = visualCol,
                    CurrentVisualRowStartCharIndex = row.StartCharIndex,
                    CurrentVisualRowEndCharIndex = row.EndCharIndex,
                };
            }
        }

        // Fallback: caret is at end of last row.
        if (rows.Count > 0)
        {
            VisualRowInfo last = rows[^1];
            string beforeCaret = _text[last.StartCharIndex..Math.Min(caretIdx, _text.Length)];
            int visualCol = MeasureDisplayWidth(beforeCaret);
            // If the last row is also row 0, offset by prompt width.
            if (rows.Count == 1)
                visualCol += MeasureLineWidth(_prompt);
            return new VisualPosition
            {
                VisualRow = rows.Count - 1,
                VisualCol = visualCol,
                CurrentVisualRowStartCharIndex = last.StartCharIndex,
                CurrentVisualRowEndCharIndex = last.EndCharIndex,
            };
        }

        return new VisualPosition { VisualRow = 0, VisualCol = 0 };
    }

    /// <summary>
    /// Finds the caret char index that corresponds to the given <paramref name="targetRow"/> and
    /// <paramref name="targetCol"/> (display column), at the given <paramref name="width"/>.
    /// If the target row is shorter than <paramref name="targetCol"/>, snaps to end of that row.
    /// </summary>
    private int FindCaretIndexForVisualPosition(int targetRow, int targetCol, int width)
    {
        List<VisualRowInfo> rows = BuildVisualRows(width);
        if (targetRow >= rows.Count)
            return _text.Length;
        if (targetRow < 0)
            return 0;

        VisualRowInfo row = rows[targetRow];
        string rowText = _text[row.StartCharIndex..row.EndCharIndex];

        // Walk grapheme clusters until we reach or pass targetCol.
        int col = 0;
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(rowText);
        int charOffset = row.StartCharIndex;

        while (enumerator.MoveNext())
        {
            string cluster = enumerator.GetTextElement();
            int clusterW = ClusterWidth(cluster, col);
            if (col + clusterW > targetCol)
                break; // caret stays before this cluster
            col += clusterW;
            charOffset += cluster.Length;
        }

        return charOffset;
    }

    // ── Helpers: cluster finding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the grapheme cluster immediately before <paramref name="charIndex"/> in
    /// <see cref="Text"/>. Returns (clusterStartCharIndex, clusterLength).
    /// </summary>
    private (int Start, int Length) FindClusterBefore(int charIndex)
    {
        if (charIndex <= 0)
            return (0, 0);

        // We need to enumerate clusters up to charIndex and return the last one.
        // StringInfo enumerates forward; we walk the prefix and take the last cluster's info.
        string prefix = _text[..charIndex];
        int lastStart = 0;
        string lastCluster = string.Empty;

        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(prefix);
        while (enumerator.MoveNext())
        {
            lastStart = enumerator.ElementIndex;
            lastCluster = enumerator.GetTextElement();
        }

        return (lastStart, lastCluster.Length);
    }

    /// <summary>
    /// Returns the grapheme cluster string starting at <paramref name="charIndex"/> in
    /// <see cref="Text"/>.
    /// </summary>
    private string FindClusterAt(int charIndex)
    {
        if (charIndex >= _text.Length)
            return string.Empty;

        // Enumerate clusters in the suffix starting at charIndex; return the first one.
        string suffix = _text[charIndex..];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(suffix);
        if (enumerator.MoveNext())
            return enumerator.GetTextElement();

        return string.Empty;
    }

    /// <summary>
    /// Measures the display width of a string by summing per-rune widths (column-aware for tabs).
    /// </summary>
    private static int MeasureDisplayWidth(string text)
    {
        int col = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            col += DisplayWidth.MeasureWithColumn(rune, col);
        }
        return col;
    }

    /// <summary>
    /// Measures the display width of a <see cref="Line"/> by summing each segment's rune widths.
    /// </summary>
    private static int MeasureLineWidth(Line line)
    {
        int w = 0;
        foreach (Segment seg in line.Segments)
            foreach (Rune r in seg.Text.EnumerateRunes())
                w += DisplayWidth.Measure(r);
        return w;
    }
}

// ── Public result types ──────────────────────────────────────────────────────────────────────

/// <summary>
/// The result of <see cref="TextBuffer.Render"/>: the visible rows to paint and the caret's
/// visual position within them.
/// </summary>
internal sealed class RenderResult
{
    /// <summary>The visible visual rows (ready to paint), possibly a subset of all rows.</summary>
    internal IReadOnlyList<Line> VisibleRows { get; }

    /// <summary>
    /// Caret position within the visible window: (row, col), both zero-based.
    /// Row is relative to the first visible row; col is in display columns.
    /// </summary>
    internal (int Row, int Col) CaretPosition { get; }

    internal RenderResult(IReadOnlyList<Line> visibleRows, (int Row, int Col) caretPosition)
    {
        VisibleRows = visibleRows;
        CaretPosition = caretPosition;
    }
}
