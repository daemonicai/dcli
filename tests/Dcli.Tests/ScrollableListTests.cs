using Dcli.Internal.FixedRegion;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Unit tests for <see cref="ScrollableList"/> — task 11.1.
/// </summary>
public sealed class ScrollableListTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a plain-text item <see cref="Line"/>.</summary>
    private static Line Item(string text) =>
        new LineBuilder().Text(text).Build();

    /// <summary>Creates a list of plain-text items.</summary>
    private static List<Line> Items(params string[] texts) =>
        texts.Select(Item).ToList();

    /// <summary>
    /// Returns true when every segment in <paramref name="line"/> has <see cref="Format.Reverse"/>
    /// set.
    /// </summary>
    private static bool AllReversed(Line line) =>
        line.Segments.Count > 0 &&
        line.Segments.All(s => (s.Style.Format & Format.Reverse) == Format.Reverse);

    /// <summary>
    /// Returns true when NO segment in <paramref name="line"/> has <see cref="Format.Reverse"/>
    /// set.
    /// </summary>
    private static bool NoneReversed(Line line) =>
        line.Segments.All(s => (s.Style.Format & Format.Reverse) == Format.None);

    /// <summary>
    /// Measures the total display width of all segments in a row.
    /// </summary>
    private static int RowDisplayWidth(Line line) =>
        line.Segments.Sum(s => DisplayWidth.Measure(s.Text));

    /// <summary>Returns true if any segment text in the line contains <paramref name="substring"/>.</summary>
    private static bool AnySegmentContains(Line line, string substring) =>
        line.Segments.Any(s => s.Text.Contains(substring, StringComparison.Ordinal));

    // ── Initial state ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InitiallyEmptySelectedIndexIsMinusOne()
    {
        ScrollableList list = new();
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void AfterSetItemsNonEmptySelectedIndexIsZero()
    {
        ScrollableList list = new();
        list.SetItems(Items("a", "b", "c"));
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void AfterSetItemsEmptySelectedIndexIsMinusOne()
    {
        ScrollableList list = new();
        list.SetItems(Items());
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void DefaultMaxRowsIsTen()
    {
        ScrollableList list = new();
        Assert.Equal(10, list.MaxRows);
    }

    [Fact]
    public void MaxRowsBelowOneTreatedAsOne()
    {
        ScrollableList list = new();
        list.MaxRows = 0;
        Assert.Equal(1, list.MaxRows);
        list.MaxRows = -5;
        Assert.Equal(1, list.MaxRows);
    }

    // ── MoveDown / MoveUp ────────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveDownAdvancesSelection()
    {
        ScrollableList list = new();
        list.SetItems(Items("a", "b", "c"));
        list.MoveDown();
        Assert.Equal(1, list.SelectedIndex);
        list.MoveDown();
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void MoveDownClampsAtLastItem()
    {
        ScrollableList list = new();
        list.SetItems(Items("a", "b"));
        list.MoveDown(); // → 1 (last)
        list.MoveDown(); // no-op
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void MoveUpMovesSelectionUp()
    {
        ScrollableList list = new();
        list.SetItems(Items("a", "b", "c"));
        list.MoveDown();
        list.MoveDown();
        list.MoveUp();
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void MoveUpClampsAtZero()
    {
        ScrollableList list = new();
        list.SetItems(Items("a", "b"));
        list.MoveUp(); // already at 0, no-op
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void MoveDownEmptyListIsNoOp()
    {
        ScrollableList list = new();
        list.MoveDown();
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void MoveUpEmptyListIsNoOp()
    {
        ScrollableList list = new();
        list.MoveUp();
        Assert.Equal(-1, list.SelectedIndex);
    }

    // ── Auto-scroll (viewport) ───────────────────────────────────────────────────────────────

    [Fact]
    public void RenderReturnsAtMostMaxRows()
    {
        ScrollableList list = new();
        list.MaxRows = 3;
        list.SetItems(Items("a", "b", "c", "d", "e"));
        IReadOnlyList<Line> rows = list.Render(80);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void RenderShowsAllItemsWhenCountLessThanMaxRows()
    {
        ScrollableList list = new();
        list.MaxRows = 10;
        list.SetItems(Items("a", "b"));
        IReadOnlyList<Line> rows = list.Render(80);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void AutoScrollMovingDownPastBottomScrollsViewport()
    {
        // 5 items, viewport = 3. Start: shows [0,1,2]. Move down until selected = 3 (past bottom).
        ScrollableList list = new();
        list.MaxRows = 3;
        list.SetItems(Items("a", "b", "c", "d", "e"));

        list.MoveDown(); // → 1
        list.MoveDown(); // → 2
        list.MoveDown(); // → 3, top should scroll to 1

        IReadOnlyList<Line> rows = list.Render(80);
        Assert.Equal(3, rows.Count);

        // The selected row must appear somewhere in the rendered output and carry Format.Reverse.
        Assert.Contains(rows, AllReversed);
    }

    [Fact]
    public void AutoScrollSelectedItemAlwaysVisibleAfterMovingDown()
    {
        // 10 items, viewport = 3. Move to the last item and confirm it's in the viewport.
        ScrollableList list = new();
        list.MaxRows = 3;
        list.SetItems(Items("0", "1", "2", "3", "4", "5", "6", "7", "8", "9"));

        for (int i = 0; i < 9; i++)
            list.MoveDown();

        Assert.Equal(9, list.SelectedIndex);

        IReadOnlyList<Line> rows = list.Render(80);
        Assert.Equal(3, rows.Count);
        // Last rendered row should be the selected (reversed) row.
        Assert.True(AllReversed(rows[^1]));
    }

    [Fact]
    public void AutoScrollMovingUpScrollsBackMinimally()
    {
        // 5 items, viewport = 3. Move down past viewport then back up.
        ScrollableList list = new();
        list.MaxRows = 3;
        list.SetItems(Items("a", "b", "c", "d", "e"));

        list.MoveDown();
        list.MoveDown();
        list.MoveDown(); // selected = 3, top = 1, viewport shows [1,2,3]
        list.MoveUp();   // selected = 2, still within [1,2,3] — no scroll needed

        IReadOnlyList<Line> rows = list.Render(80);
        Assert.Equal(3, rows.Count);
        // Selected (item index 2) is the middle row of the viewport [1,2,3].
        Assert.True(AllReversed(rows[1]));
    }

    [Fact]
    public void AutoScrollMoveUpScrollsViewportWhenSelectionLeavesTop()
    {
        // Move selection back to 0 from scrolled-down position.
        ScrollableList list = new();
        list.MaxRows = 3;
        list.SetItems(Items("a", "b", "c", "d", "e"));

        // Scroll down
        list.MoveDown();
        list.MoveDown();
        list.MoveDown(); // selected = 3, top scrolled to 1

        // Scroll all the way back up
        list.MoveUp();
        list.MoveUp();
        list.MoveUp();
        Assert.Equal(0, list.SelectedIndex);

        IReadOnlyList<Line> rows = list.Render(80);
        Assert.Equal(3, rows.Count);
        Assert.True(AllReversed(rows[0]));
    }

    // ── Selected-row highlight ───────────────────────────────────────────────────────────────

    [Fact]
    public void SelectedRowHasFormatReverse()
    {
        ScrollableList list = new();
        list.SetItems(Items("alpha", "beta", "gamma"));
        list.MoveDown(); // selected = 1

        IReadOnlyList<Line> rows = list.Render(80);

        Assert.True(AllReversed(rows[1]));
    }

    [Fact]
    public void NonSelectedRowsDoNotHaveFormatReverse()
    {
        ScrollableList list = new();
        list.SetItems(Items("alpha", "beta", "gamma"));

        IReadOnlyList<Line> rows = list.Render(80);

        // row 0 is selected; rows 1 and 2 must not be reversed.
        Assert.True(NoneReversed(rows[1]));
        Assert.True(NoneReversed(rows[2]));
    }

    [Fact]
    public void SelectedRowPreservesExistingFgBgAndOtherFormats()
    {
        // Item has Bold + Dim + a foreground color; Reverse must be ORed in, not replacing.
        Line item = new LineBuilder()
            .Append("hello", new Style(Foreground: Color.FromRgb(1, 2, 3), Format: Format.Bold | Format.Dim))
            .Build();

        ScrollableList list = new();
        list.SetItems([item]);

        IReadOnlyList<Line> rows = list.Render(80);
        Segment seg = rows[0].Segments[0];

        Assert.Equal(Color.FromRgb(1, 2, 3), seg.Style.Foreground);
        Assert.True((seg.Style.Format & Format.Bold) == Format.Bold);
        Assert.True((seg.Style.Format & Format.Dim) == Format.Dim);
        Assert.True((seg.Style.Format & Format.Reverse) == Format.Reverse);
    }

    [Fact]
    public void SelectedRowPaddedToFullWidth()
    {
        // Short item (3 cols) with width = 20: padded row should be exactly 20 cols wide.
        ScrollableList list = new();
        list.SetItems([Item("abc")]);

        IReadOnlyList<Line> rows = list.Render(20);
        int totalWidth = RowDisplayWidth(rows[0]);

        Assert.Equal(20, totalWidth);
    }

    [Fact]
    public void NonSelectedRowNotPadded()
    {
        ScrollableList list = new();
        list.SetItems(Items("abc", "xyz"));
        // selected is "abc" (row 0); "xyz" (row 1) must NOT be padded.
        IReadOnlyList<Line> rows = list.Render(20);
        int secondRowWidth = RowDisplayWidth(rows[1]);
        Assert.True(secondRowWidth <= 3); // "xyz" = 3 cols, no padding
    }

    // ── Width-aware truncation ────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderTruncatesOverWideItemToSingleRow()
    {
        // Item is wider than the available width.
        ScrollableList list = new();
        list.SetItems([Item("abcdefghijklmnop")]); // 16 chars
        IReadOnlyList<Line> rows = list.Render(5);
        Assert.Single(rows);
    }

    [Fact]
    public void RenderTruncatedRowDisplayWidthDoesNotExceedWidth()
    {
        // Non-selected row must not exceed the given width.
        // Use a two-item list so the first item is selected and the second is not.
        ScrollableList list = new();
        list.SetItems(Items("xxxxxx_short", "abcdefghijklmnop")); // second item > 5 cols
        list.MoveDown(); // selected = 1 (the over-wide item)
        IReadOnlyList<Line> rows = list.Render(5);
        // row 1 is the selected (padded) row — check its width <= 5.
        // row 0 is not padded — also check <= 5.
        Assert.True(RowDisplayWidth(rows[0]) <= 5);
        Assert.True(RowDisplayWidth(rows[1]) <= 5);
    }

    [Fact]
    public void RenderDoesNotSplitWideChar()
    {
        // CJK char is 2 cols. At width = 2, "A中" → first row is "A" (1 col) because "中" (2 cols)
        // would exceed width when combined with "A" (total 3 > 2). LineWrapper moves it to the
        // second row; we discard that second row.
        Line item = new LineBuilder().Text("A中").Build();
        ScrollableList list = new();
        list.SetItems([item]);

        IReadOnlyList<Line> rows = list.Render(2);
        Assert.Single(rows);

        // First row must be at most 2 display columns and must not end mid-wide-char.
        int w = RowDisplayWidth(rows[0]);
        Assert.InRange(w, 0, 2);
    }

    // ── Multi-select ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleCurrentAddsThenRemovesFromCheckedIndices()
    {
        ScrollableList list = new(multiSelect: true);
        list.SetItems(Items("a", "b", "c"));

        list.ToggleCurrent(); // check index 0
        Assert.Contains(0, list.CheckedIndices);

        list.ToggleCurrent(); // uncheck index 0
        Assert.DoesNotContain(0, list.CheckedIndices);
    }

    [Fact]
    public void ToggleCurrentMultipleItemsCheckedIndicesAscending()
    {
        ScrollableList list = new(multiSelect: true);
        list.SetItems(Items("a", "b", "c", "d"));

        list.MoveDown();      // selected = 1
        list.ToggleCurrent(); // check 1
        list.MoveDown();      // selected = 2
        list.ToggleCurrent(); // check 2
        list.MoveUp();        // selected = 1
        list.MoveUp();        // selected = 0
        list.ToggleCurrent(); // check 0

        int[] expected = [0, 1, 2];
        Assert.Equal(expected, list.CheckedIndices.ToArray());
    }

    [Fact]
    public void ToggleCurrentWhenNotMultiSelectIsNoOp()
    {
        ScrollableList list = new(multiSelect: false);
        list.SetItems(Items("a", "b"));
        list.ToggleCurrent();
        Assert.Empty(list.CheckedIndices);
    }

    [Fact]
    public void ToggleCurrentEmptyListIsNoOp()
    {
        ScrollableList list = new(multiSelect: true);
        list.ToggleCurrent();
        Assert.Empty(list.CheckedIndices);
    }

    [Fact]
    public void RenderCheckedRowShowsCheckedMarker()
    {
        ScrollableList list = new(multiSelect: true);
        list.SetItems(Items("alpha"));
        list.ToggleCurrent(); // check index 0

        IReadOnlyList<Line> rows = list.Render(80);
        Assert.True(AnySegmentContains(rows[0], "[x]"));
    }

    [Fact]
    public void RenderUncheckedRowShowsUncheckedMarker()
    {
        ScrollableList list = new(multiSelect: true);
        list.SetItems(Items("alpha", "beta"));
        list.ToggleCurrent(); // check 0
        list.MoveDown();      // selected = 1 (unchecked)

        IReadOnlyList<Line> rows = list.Render(80);
        // row 0 = item[0]: checked → "[x]"
        Assert.True(AnySegmentContains(rows[0], "[x]"));
        // row 1 = item[1]: not checked → "[ ]"
        Assert.True(AnySegmentContains(rows[1], "[ ]"));
    }

    [Fact]
    public void RenderNoMarkersWhenNotMultiSelect()
    {
        ScrollableList list = new(multiSelect: false);
        list.SetItems(Items("hello"));
        IReadOnlyList<Line> rows = list.Render(80);
        string allText = string.Concat(rows[0].Segments.Select(s => s.Text));
        Assert.DoesNotContain("[", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiSelectCheckedStateVisibleIndependentlyOfSelection()
    {
        // Item[0] is checked but NOT selected; item[1] is selected but NOT checked.
        ScrollableList list = new(multiSelect: true);
        list.SetItems(Items("a", "b"));
        list.ToggleCurrent(); // check 0
        list.MoveDown();      // selected = 1

        IReadOnlyList<Line> rows = list.Render(80);
        // row 0 = item[0]: checked, not selected.
        Assert.True(AnySegmentContains(rows[0], "[x]"));
        Assert.True(NoneReversed(rows[0]));

        // row 1 = item[1]: not checked, selected.
        Assert.True(AnySegmentContains(rows[1], "[ ]"));
        Assert.True(AllReversed(rows[1]));
    }

    // ── SetItems resets state ────────────────────────────────────────────────────────────────

    [Fact]
    public void SetItemsResetsSelectionToZero()
    {
        ScrollableList list = new();
        list.SetItems(Items("a", "b", "c"));
        list.MoveDown();
        list.MoveDown();
        Assert.Equal(2, list.SelectedIndex);

        list.SetItems(Items("x", "y"));
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void SetItemsResetsScrollToTop()
    {
        // Scroll down, then SetItems — first rendered row should be item[0] again.
        ScrollableList list = new();
        list.MaxRows = 2;
        list.SetItems(Items("a", "b", "c", "d"));
        list.MoveDown();
        list.MoveDown(); // selected = 2, scrolled down

        list.SetItems(Items("x", "y", "z"));
        IReadOnlyList<Line> rows = list.Render(80);

        // After reset the first rendered row is item[0] ("x") and it should be the selected (reversed) row.
        Assert.True(AllReversed(rows[0]));
    }

    [Fact]
    public void SetItemsClearsCheckedIndices()
    {
        ScrollableList list = new(multiSelect: true);
        list.SetItems(Items("a", "b", "c"));
        list.ToggleCurrent();
        list.MoveDown();
        list.ToggleCurrent();
        Assert.Equal(2, list.CheckedIndices.Count);

        list.SetItems(Items("x", "y"));
        Assert.Empty(list.CheckedIndices);
    }

    [Fact]
    public void SetItemsToEmptySelectedIndexMinusOne()
    {
        ScrollableList list = new();
        list.SetItems(Items("a"));
        list.SetItems([]);
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal(0, list.Count);
    }

    // ── Empty-list edge cases ────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderEmptyListReturnsEmpty()
    {
        ScrollableList list = new();
        Assert.Empty(list.Render(80));
    }

    [Fact]
    public void EmptyListMoveDownMoveUpAreNoOps()
    {
        ScrollableList list = new();
        list.MoveDown();
        list.MoveUp();
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void EmptyListToggleCurrentIsNoOp()
    {
        ScrollableList list = new(multiSelect: true);
        list.ToggleCurrent();
        Assert.Empty(list.CheckedIndices);
    }

    // ── Width edge cases ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderWidthBelowOneTreatedAsOne()
    {
        ScrollableList list = new();
        list.SetItems(Items("abc"));
        IReadOnlyList<Line> rows = list.Render(0);
        Assert.Single(rows);

        rows = list.Render(-5);
        Assert.Single(rows);
    }

    // ── MaxRows change between renders (B1 / B2 regression) ─────────────────────────────────

    [Fact]
    public void RenderDoesNotThrowWhenMaxRowsGrowsAfterScrollingDown()
    {
        // B1 repro: 10 items, MaxRows=3, scroll to bottom (_top=7), then grow MaxRows to 10.
        // Before the fix, end = 7 + 10 = 17 → IndexOutOfRange on _items[17..].
        ScrollableList list = new();
        list.MaxRows = 3;
        list.SetItems(Items("0", "1", "2", "3", "4", "5", "6", "7", "8", "9"));

        for (int i = 0; i < 9; i++)
            list.MoveDown();

        Assert.Equal(9, list.SelectedIndex);

        list.MaxRows = 10; // grow before render

        IReadOnlyList<Line> rows = list.Render(80); // must not throw
        Assert.Equal(10, rows.Count);               // all 10 items visible now
        Assert.Contains(rows, AllReversed);          // selected row present and highlighted
    }

    [Fact]
    public void RenderKeepsSelectionVisibleWhenMaxRowsShrinks()
    {
        // B2 repro: 10 items, MaxRows=10, navigate to last item (selection=9, _top=0),
        // then shrink MaxRows to 3. Before the fix, Render returned items [0,1,2] and
        // the selected row (index 9) was absent from the output.
        ScrollableList list = new();
        list.MaxRows = 10;
        list.SetItems(Items("0", "1", "2", "3", "4", "5", "6", "7", "8", "9"));

        for (int i = 0; i < 9; i++)
            list.MoveDown();

        Assert.Equal(9, list.SelectedIndex);

        list.MaxRows = 3; // shrink before render

        IReadOnlyList<Line> rows = list.Render(80);
        Assert.Equal(3, rows.Count);         // exactly MaxRows rows returned
        Assert.Contains(rows, AllReversed);  // selected row must appear within the window
        // The last rendered row must be item[9] (the selected one), highlighted.
        Assert.True(AllReversed(rows[^1]));
    }

    // ── CheckedIndices returns a snapshot (N1) ───────────────────────────────────────────────

    [Fact]
    public void CheckedIndicesReturnsSnapshotNotLiveSet()
    {
        ScrollableList list = new(multiSelect: true);
        list.SetItems(Items("a", "b", "c"));
        list.ToggleCurrent(); // check index 0

        IReadOnlyCollection<int> snapshot = list.CheckedIndices;
        Assert.Contains(0, snapshot);

        // Toggle again (uncheck 0, check 1) — snapshot must be unaffected.
        list.ToggleCurrent();          // uncheck 0
        list.MoveDown();
        list.ToggleCurrent();          // check 1

        // The snapshot captured before those mutations still shows only [0].
        Assert.Contains(0, snapshot);
        Assert.Single(snapshot);

        // The current CheckedIndices reflects the new state.
        Assert.Contains(1, list.CheckedIndices);
        Assert.DoesNotContain(0, list.CheckedIndices);
    }
}
