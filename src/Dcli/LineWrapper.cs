using System.Text;

namespace Dcli;

/// <summary>
/// Hard cell-wrap of a <see cref="Line"/> into visual rows at a given terminal width.
/// <para>
/// This is a pure rendering primitive: it takes a logical <see cref="Line"/> (ordered styled
/// <see cref="Segment"/>s) and returns a list of visual row <see cref="Line"/>s, each of which
/// occupies at most <c>width</c> display columns when painted.
/// </para>
/// <para>
/// Rules:
/// <list type="bullet">
///   <item>Hard character/cell wrap — NOT word-wrap. Breaks occur at column boundaries only.</item>
///   <item>A wide (2-column) scalar that would straddle the row boundary moves wholly to the next
///     row; the trailing single column of the current row is left empty (not padded — padding is
///     the painter's responsibility).</item>
///   <item>Segment <see cref="Style"/> is preserved on both halves when a segment is split.</item>
///   <item>An empty <see cref="Line"/> (no segments, or only empty-text segments) produces
///     exactly one empty row.</item>
///   <item><c>width</c> &lt;= 0: treated as width 1 so callers never loop forever.
///     Wide chars that exceed even width 1 are emitted on their own row rather than dropped.</item>
///   <item>A single scalar wider than <c>width</c> (e.g., a 2-column emoji at
///     width 1) is emitted on its own row rather than being dropped.</item>
/// </list>
/// </para>
/// </summary>
internal static class LineWrapper
{
    /// <summary>
    /// Wraps <paramref name="line"/> into visual rows of at most <paramref name="width"/>
    /// display columns.
    /// </summary>
    /// <param name="line">The logical line to wrap.</param>
    /// <param name="width">
    /// Target display width in columns. Values &lt;= 0 are clamped to 1 internally.
    /// </param>
    /// <returns>
    /// One or more <see cref="Line"/> values representing the visual rows. Never empty.
    /// </returns>
    internal static IReadOnlyList<Line> Wrap(Line line, int width)
    {
        // Clamp width so callers don't need to guard.
        if (width <= 0)
            width = 1;

        // Fast path: no segments or all-empty text.
        bool anyContent = false;
        foreach (Segment seg in line.Segments)
        {
            if (seg.Text.Length > 0)
            {
                anyContent = true;
                break;
            }
        }
        if (!anyContent)
            return [new Line(Array.Empty<Segment>())];

        List<Line> rows = [];
        List<Segment> current = [];
        int col = 0; // current column within the row being built

        void FlushRow()
        {
            rows.Add(new Line(current.ToArray()));
            current.Clear();
            col = 0;
        }

        void AppendToRow(string text, Style style)
        {
            if (text.Length > 0)
                current.Add(new Segment(text, style));
        }

        foreach (Segment segment in line.Segments)
        {
            Style style = segment.Style;
            string text = segment.Text;

            if (text.Length == 0)
                continue;

            // Walk rune-by-rune through the segment text.
            // We track the UTF-16 char-index position so we can slice the string.
            int segStart = 0; // start of the pending fragment within 'text' (char index)
            int charIdx = 0;  // current char index

            foreach (Rune rune in text.EnumerateRunes())
            {
                int runeWidth = DisplayWidth.MeasureWithColumn(rune, col);

                if (col + runeWidth > width && col > 0)
                {
                    // Flush pending fragment before the break.
                    string pending = text[segStart..charIdx];
                    AppendToRow(pending, style);
                    FlushRow();
                    segStart = charIdx;
                    // col is now 0 after FlushRow.
                }

                // Advance past this rune.
                charIdx += rune.Utf16SequenceLength;
                col += runeWidth;
            }

            // Flush any remaining text in the segment.
            string tail = text[segStart..];
            AppendToRow(tail, style);
        }

        // Flush the last row (may be empty if everything was exact multiples).
        rows.Add(new Line(current.ToArray()));

        return rows;
    }
}
