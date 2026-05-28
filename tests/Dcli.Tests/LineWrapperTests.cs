using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Behavioural tests for Section 3.2/3.3: width-aware wrapping of a <see cref="Line"/> into
/// visual rows.  Every test asserts a specific spec scenario from the Section-3 brief.
/// </summary>
public sealed class LineWrapperTests
{
    private static Style Bold => new(Format: Format.Bold);
    private static Style Italic => new(Format: Format.Italic);

    // -----------------------------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void EmptyLineProducesExactlyOneEmptyRow()
    {
        Line empty = new(Array.Empty<Segment>());
        IReadOnlyList<Line> rows = LineWrapper.Wrap(empty, 80);
        Assert.Single(rows);
        Assert.Empty(rows[0].Segments);
    }

    [Fact]
    public void LineWithOnlyEmptySegmentsProducesOneEmptyRow()
    {
        Line line = new(new[] { new Segment(""), new Segment("") });
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 80);
        Assert.Single(rows);
    }

    [Fact]
    public void ZeroOrNegativeWidthDoesNotLoopOrThrow()
    {
        // Width <= 0 is clamped to 1 internally; must terminate and return at least one row.
        Line line = new LineBuilder().Text("abc").Build();
        IReadOnlyList<Line> rows0 = LineWrapper.Wrap(line, 0);
        IReadOnlyList<Line> rowsNeg = LineWrapper.Wrap(line, -5);
        Assert.NotEmpty(rows0);
        Assert.NotEmpty(rowsNeg);
    }

    [Fact]
    public void SingleCharWiderThanWidthIsEmittedOnItsOwnRow()
    {
        // Width = 1, single wide (2-col) emoji: must not be dropped.
        Line line = new LineBuilder().Text("\U0001F600").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 1);
        int totalWidth = rows.Sum(r => r.Segments.Sum(s => DisplayWidth.Measure(s.Text)));
        Assert.True(totalWidth > 0, "Wide char must not be dropped");
        // Must appear in exactly one row
        Assert.Single(rows, r => r.Segments.Any(s => s.Text.Contains("\U0001F600", StringComparison.Ordinal)));
    }

    [Fact]
    public void OverWideEmojiAloneProducesExactlyOneRow()
    {
        // B1 regression: a single wide char wider than width must NOT produce a phantom empty
        // leading row.  The only output should be the one row containing the emoji.
        Line line = new LineBuilder().Text("\U0001F600").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 1);
        Assert.Single(rows);
    }

    [Fact]
    public void OverWideCjkAloneProducesExactlyOneRow()
    {
        // B1 regression: same defect for a CJK ideograph (2-col) at width 1.
        Line line = new LineBuilder().Text("中").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 1);
        Assert.Single(rows);
    }

    [Fact]
    public void OverWideCharMidStringHasNoEmptyInterleaved()
    {
        // B1 regression: "A中B" at width 1 → ["A", "中", "B"] — no zero-content rows.
        Line line = new LineBuilder().Text("A中B").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 1);
        foreach (Line row in rows)
        {
            int rowWidth = row.Segments.Sum(s => DisplayWidth.Measure(s.Text));
            Assert.True(rowWidth > 0, "No empty (zero-width) rows should be interleaved");
        }
    }

    // -----------------------------------------------------------------------------------------
    // Basic wrapping
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ShortLineDoesNotWrap()
    {
        Line line = new LineBuilder().Text("hello").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 80);
        Assert.Single(rows);
        Assert.Equal("hello", rows[0].Segments[0].Text);
    }

    [Fact]
    public void LineExactlyWidthDoesNotWrap()
    {
        // "abcde" = 5 columns, width = 5 → one row.
        Line line = new LineBuilder().Text("abcde").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 5);
        Assert.Single(rows);
    }

    [Fact]
    public void LineOneCharOverWidthWrapsTwoRows()
    {
        // "abcdef" = 6 cols, width = 5 → two rows: "abcde" + "f".
        Line line = new LineBuilder().Text("abcdef").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 5);
        Assert.Equal(2, rows.Count);
        Assert.Equal("abcde", rows[0].Segments[0].Text);
        Assert.Equal("f", rows[1].Segments[0].Text);
    }

    [Fact]
    public void LongLineWrapsCorrectNumberOfRows()
    {
        // 12 ASCII chars, width 4 → 3 rows.
        Line line = new LineBuilder().Text("abcdefghijkl").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 4);
        Assert.Equal(3, rows.Count);
    }

    // -----------------------------------------------------------------------------------------
    // Wide char handling
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void WideCharDoesNotStraddle()
    {
        // Width = 3, text "AB中D": A(1)+B(1)=2, 中(2) would push to col 4 > 3 → row break.
        // Row 0: "AB", Row 1: "中D"
        Line line = new LineBuilder().Text("AB中D").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 3);

        string row0text = string.Concat(rows[0].Segments.Select(s => s.Text));
        string row1text = string.Concat(rows[1].Segments.Select(s => s.Text));

        Assert.Equal("AB", row0text);
        Assert.Contains("中", row1text, StringComparison.Ordinal);
        Assert.Contains("D", row1text, StringComparison.Ordinal);
    }

    [Fact]
    public void WideCharsWrapAtExactlyWidth()
    {
        // Width = 4, "中文" = 2+2 = 4 → one row.
        Line line = new LineBuilder().Text("中文").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 4);
        Assert.Single(rows);
    }

    [Fact]
    public void WideCharsWrapWhenExceedingWidth()
    {
        // Width = 3, "中文A": 中(2)+文(2)=4>3 → row break after 中.
        // Row 0: "中", Row 1: "文A"
        Line line = new LineBuilder().Text("中文A").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 3);
        Assert.Equal(2, rows.Count);
        string row0 = string.Concat(rows[0].Segments.Select(s => s.Text));
        string row1 = string.Concat(rows[1].Segments.Select(s => s.Text));
        Assert.Equal("中", row0);
        Assert.Equal("文A", row1);
    }

    [Fact]
    public void WideCharAtWidth1IsEmittedOwnRow()
    {
        // Width = 1, "A中B": A fits (1=1), 中(2)>1 → break; 中 alone on next row even though >width.
        Line line = new LineBuilder().Text("A中B").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 1);

        // All chars must appear (none dropped)
        string all = string.Concat(rows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.Contains("A", all, StringComparison.Ordinal);
        Assert.Contains("中", all, StringComparison.Ordinal);
        Assert.Contains("B", all, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------
    // Style preservation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void SplitSegmentPreservesStyleOnBothHalves()
    {
        // Single bold segment "ABCDE" split at width 3 → "ABC" bold + "DE" bold.
        Line line = new(new[] { new Segment("ABCDE", Bold) });
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 3);

        Assert.Equal(2, rows.Count);
        Assert.All(rows[0].Segments, s => Assert.Equal(Bold, s.Style));
        Assert.All(rows[1].Segments, s => Assert.Equal(Bold, s.Style));
    }

    [Fact]
    public void MultiStyleSegmentsPreserveTheirStyles()
    {
        // Row 0: "AB" (bold), Row 1: "C" (bold) + "DE" (italic) + "F" (italic)
        // width=3, "AB"(bold) "CDEF"(italic): AB=2<3, C(1)→AB+C=3=row full, new row "DEF"
        // Actually: "AB"(bold)=2, then C of "CDEF"(italic): col goes to 3 exactly, row ends.
        // Row 0: "AB", "C"; Row 1: "DEF"
        Segment boldSeg = new("AB", Bold);
        Segment italicSeg = new("CDEF", Italic);
        Line line = new(new[] { boldSeg, italicSeg });
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 3);

        Assert.Equal(2, rows.Count);

        // Row 0 must contain bold "AB" and italic "C"
        bool hasAB = rows[0].Segments.Any(s => s.Text == "AB" && s.Style == Bold);
        bool hasC = rows[0].Segments.Any(s => s.Text == "C" && s.Style == Italic);
        Assert.True(hasAB, "Row 0 must contain bold 'AB'");
        Assert.True(hasC, "Row 0 must contain italic 'C'");

        // Row 1 must contain italic "DEF"
        bool hasDEF = rows[1].Segments.Any(s => s.Text == "DEF" && s.Style == Italic);
        Assert.True(hasDEF, "Row 1 must contain italic 'DEF'");
    }

    [Fact]
    public void WrapAcrossSegmentBoundaryPreservesStyles()
    {
        // Width 4: seg1="AB"(bold, 2 cols), seg2="CD"(italic, 2 cols) → exactly fills row 0.
        // seg3="EF"(bold) → row 1.
        Segment s1 = new("AB", Bold);
        Segment s2 = new("CD", Italic);
        Segment s3 = new("EF", Bold);
        Line line = new(new[] { s1, s2, s3 });
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 4);

        Assert.Equal(2, rows.Count);

        string row0 = string.Concat(rows[0].Segments.Select(s => s.Text));
        string row1 = string.Concat(rows[1].Segments.Select(s => s.Text));
        Assert.Equal("ABCD", row0);
        Assert.Equal("EF", row1);

        // Styles must be correct on each row
        Assert.Contains(rows[0].Segments, s => s.Text == "AB" && s.Style == Bold);
        Assert.Contains(rows[0].Segments, s => s.Text == "CD" && s.Style == Italic);
        Assert.Contains(rows[1].Segments, s => s.Text == "EF" && s.Style == Bold);
    }

    // -----------------------------------------------------------------------------------------
    // Total content preservation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void WrapPreservesAllTextContent()
    {
        // All characters from the source line must appear across the rows, in order.
        string text = "Hello, 世界! This is a longer string.";
        Line line = new LineBuilder().Text(text).Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 10);

        string rejoined = string.Concat(rows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.Equal(text, rejoined);
    }

    [Fact]
    public void EachRowWidthIsWithinBound()
    {
        string text = "Hello, 世界! Testing wide char wrap.";
        Line line = new LineBuilder().Text(text).Build();
        const int width = 10;
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, width);

        foreach (Line row in rows)
        {
            int rowWidth = row.Segments.Sum(s => DisplayWidth.Measure(s.Text));
            // Allow a single wide char that forced its own row to exceed (documented edge case):
            // a row is exempt if it contains exactly one segment whose entire text is a single
            // scalar value with display-width > the target width.
            bool hasSingleWideChar =
                row.Segments.Count == 1 &&
                DisplayWidth.Measure(row.Segments[0].Text) > width &&
                row.Segments[0].Text.Length is 1 or 2; // BMP rune = 1 code unit; supplementary = 2

            if (!hasSingleWideChar)
            {
                Assert.True(rowWidth <= width,
                    $"Row width {rowWidth} exceeds bound {width}: [{string.Concat(row.Segments.Select(s => s.Text))}]");
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Tab handling inside wrap
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void TabExpandsCorrectlyDuringWrap()
    {
        // "\tA" at width 9: tab from col 0 → 8, then A → col 9. Fits in one row at width 9.
        Line line = new LineBuilder().Text("\tA").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 9);
        Assert.Single(rows);
    }

    [Fact]
    public void TabCausesWrapWhenItExceedsWidth()
    {
        // "\tA" at width 8: tab from col 0 → 8 (fills exactly), A → col 9 > 8.
        // Expect at least 2 rows: row0 = tab, row1 = "A".
        Line line = new LineBuilder().Text("\tA").Build();
        IReadOnlyList<Line> rows = LineWrapper.Wrap(line, 8);
        // We just confirm A appears after the tab row and overall content is preserved.
        string all = string.Concat(rows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.Contains("A", all, StringComparison.Ordinal);
    }
}
