using System.Text;
using Dcli.Internal.FixedRegion;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Unit tests for <see cref="TextBuffer"/> — tasks 10.1 and 10.2.
/// </summary>
public sealed class TextBufferTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static TextBuffer Buffer(string text = "")
    {
        TextBuffer buf = new();
        if (text.Length > 0)
            buf.Insert(text);
        return buf;
    }

    /// <summary>
    /// Sets the caret index directly. Only valid if the index is a valid char boundary.
    /// </summary>
    private static void SetCaret(TextBuffer buf, int index) =>
        buf.SetCaretIndexForTest(index);

    // ── Basic insert / caret ─────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertRuneAdvancesCaretByUtf16Length()
    {
        TextBuffer buf = new();
        buf.Insert(new Rune('A'));
        Assert.Equal("A", buf.Text);
        Assert.Equal(1, buf.CaretIndex);
    }

    [Fact]
    public void InsertSupplementaryRuneAdvancesCaretBy2()
    {
        // U+1F600 😀 is a surrogate pair: Utf16SequenceLength = 2.
        TextBuffer buf = new();
        buf.Insert(new Rune(0x1F600));
        Assert.Equal("\U0001F600", buf.Text);
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void InsertStringCaretAtEnd()
    {
        TextBuffer buf = new();
        buf.Insert("hello");
        Assert.Equal("hello", buf.Text);
        Assert.Equal(5, buf.CaretIndex);
    }

    [Fact]
    public void InsertMidTextInsertsAtCaret()
    {
        TextBuffer buf = Buffer("ac");
        SetCaret(buf, 1); // between a and c
        buf.Insert(new Rune('b'));
        Assert.Equal("abc", buf.Text);
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void InsertNewlineSplitsLine()
    {
        TextBuffer buf = Buffer("ab");
        SetCaret(buf, 1);
        buf.InsertNewline();
        Assert.Equal("a\nb", buf.Text);
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void SetTextReplacesAndMovesCaretToEnd()
    {
        TextBuffer buf = Buffer("old");
        buf.SetText("new content");
        Assert.Equal("new content", buf.Text);
        Assert.Equal(11, buf.CaretIndex);
    }

    [Fact]
    public void ClearEmptiesBuffer()
    {
        TextBuffer buf = Buffer("hello");
        buf.Clear();
        Assert.Equal(string.Empty, buf.Text);
        Assert.Equal(0, buf.CaretIndex);
    }

    // ── Backspace ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BackspaceDeletesAsciiChar()
    {
        TextBuffer buf = Buffer("hello");
        buf.Backspace();
        Assert.Equal("hell", buf.Text);
        Assert.Equal(4, buf.CaretIndex);
    }

    [Fact]
    public void BackspaceAtStartIsNoOp()
    {
        TextBuffer buf = Buffer("hi");
        SetCaret(buf, 0);
        buf.Backspace();
        Assert.Equal("hi", buf.Text);
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void BackspaceDeletesWholeGraphemeClusterCombiningMark()
    {
        // "é" as base + combining acute = 2 UTF-16 chars, 1 grapheme cluster.
        string text = "é"; // e + COMBINING ACUTE ACCENT
        TextBuffer buf = Buffer(text);
        // Caret is after the combining mark (index 2).
        Assert.Equal(2, buf.CaretIndex);
        buf.Backspace();
        Assert.Equal(string.Empty, buf.Text);
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void BackspaceDeletesWholeSurrogatePair()
    {
        // U+1F600 is a surrogate pair: 2 UTF-16 code units.
        TextBuffer buf = Buffer("\U0001F600");
        Assert.Equal(2, buf.CaretIndex);
        buf.Backspace();
        Assert.Equal(string.Empty, buf.Text);
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void BackspaceDeletesEntireZwjSequence()
    {
        // Woman+ZWJ+Laptop: U+1F469 + U+200D + U+1F4BB  (3 code points, 5 UTF-16 units)
        string zwjSeq = "\U0001F469‍\U0001F4BB";
        TextBuffer buf = Buffer(zwjSeq);
        buf.Backspace();
        // The whole ZWJ sequence is one grapheme cluster: deleted entirely.
        Assert.Equal(string.Empty, buf.Text);
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void BackspaceDeletesEntireFlagSequence()
    {
        // Flag: Regional Indicator GB = U+1F1EC + U+1F1E7 (4 UTF-16 units, 1 grapheme cluster)
        string flag = "\U0001F1EC\U0001F1E7"; // 🇬🇧
        TextBuffer buf = Buffer(flag);
        buf.Backspace();
        Assert.Equal(string.Empty, buf.Text);
        Assert.Equal(0, buf.CaretIndex);
    }

    // ── Delete (forward) ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRemovesAsciiChar()
    {
        TextBuffer buf = Buffer("hello");
        SetCaret(buf, 0);
        buf.Delete();
        Assert.Equal("ello", buf.Text);
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void DeleteAtEndIsNoOp()
    {
        TextBuffer buf = Buffer("hi");
        buf.Delete();
        Assert.Equal("hi", buf.Text);
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void DeleteRemovesWholeGraphemeClusterCombiningMark()
    {
        string text = "é"; // e + COMBINING ACUTE ACCENT (1 grapheme cluster)
        TextBuffer buf = Buffer(text);
        SetCaret(buf, 0);
        buf.Delete();
        Assert.Equal(string.Empty, buf.Text);
    }

    // ── MoveLeft / MoveRight ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveLeftMovesOneCharBack()
    {
        TextBuffer buf = Buffer("abc");
        buf.MoveLeft();
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void MoveLeftAtStartIsNoOp()
    {
        TextBuffer buf = Buffer("a");
        SetCaret(buf, 0);
        buf.MoveLeft();
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void MoveLeftOverCombiningMarkMovesOnePastCluster()
    {
        // "é" = 2 chars, 1 grapheme. Caret starts at 2, MoveLeft goes to 0.
        string text = "é";
        TextBuffer buf = Buffer(text);
        Assert.Equal(2, buf.CaretIndex);
        buf.MoveLeft();
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void MoveLeftOverSupplementaryMovesByTwoChars()
    {
        TextBuffer buf = Buffer("\U0001F600");
        Assert.Equal(2, buf.CaretIndex);
        buf.MoveLeft();
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void MoveLeftOverZwjSequenceMovesEntireCluster()
    {
        string zwjSeq = "\U0001F469‍\U0001F4BB";
        TextBuffer buf = Buffer(zwjSeq);
        buf.MoveLeft();
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void MoveRightMovesOneCharForward()
    {
        TextBuffer buf = Buffer("abc");
        SetCaret(buf, 0);
        buf.MoveRight();
        Assert.Equal(1, buf.CaretIndex);
    }

    [Fact]
    public void MoveRightAtEndIsNoOp()
    {
        TextBuffer buf = Buffer("a");
        buf.MoveRight();
        Assert.Equal(1, buf.CaretIndex);
    }

    [Fact]
    public void MoveRightOverCombiningMarkMovesOnePastCluster()
    {
        string text = "é"; // 2 chars, 1 grapheme
        TextBuffer buf = Buffer(text);
        SetCaret(buf, 0);
        buf.MoveRight();
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void MoveRightOverSupplementaryMovesByTwoChars()
    {
        TextBuffer buf = Buffer("\U0001F600");
        SetCaret(buf, 0);
        buf.MoveRight();
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void MoveRightOverFlagSequenceMovesEntireCluster()
    {
        string flag = "\U0001F1EC\U0001F1E7"; // 🇬🇧, 4 UTF-16 units, 1 cluster
        TextBuffer buf = Buffer(flag);
        SetCaret(buf, 0);
        buf.MoveRight();
        Assert.Equal(flag.Length, buf.CaretIndex);
    }

    [Fact]
    public void MoveLeftRightCrossesLogicalLineBoundary()
    {
        TextBuffer buf = Buffer("a\nb");
        // Caret at 'b' (index 2, after the '\n').
        SetCaret(buf, 2);
        buf.MoveLeft(); // crosses newline: moves to 1 (the '\n')
        Assert.Equal(1, buf.CaretIndex);
        buf.MoveLeft(); // moves to 0 ('a')
        Assert.Equal(0, buf.CaretIndex);
        buf.MoveRight(); // back to 1
        Assert.Equal(1, buf.CaretIndex);
    }

    // ── MoveHome / MoveEnd ───────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveHomeSingleRowGoesToStart()
    {
        TextBuffer buf = Buffer("hello");
        buf.MoveHome(80);
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void MoveEndSingleRowGoesToEnd()
    {
        TextBuffer buf = Buffer("hello");
        SetCaret(buf, 0);
        buf.MoveEnd(80);
        Assert.Equal(5, buf.CaretIndex);
    }

    [Fact]
    public void MoveHomeWrappedRowGoesToStartOfVisualRow()
    {
        // "hello world" at width 5: row0="hello"(0-5), row1=" worl"(5-10), row2="d"(10-11)
        // Caret at end (index 11, on row2 which starts at 10). MoveHome → 10.
        TextBuffer buf = Buffer("hello world");
        buf.MoveHome(5);
        Assert.Equal(10, buf.CaretIndex);
    }

    [Fact]
    public void MoveEndWrappedRowGoesToEndOfVisualRow()
    {
        // Caret at start (0), MoveEnd at width 5: end of first visual row "hello" → 5.
        TextBuffer buf = Buffer("hello world");
        SetCaret(buf, 0);
        buf.MoveEnd(5);
        Assert.Equal(5, buf.CaretIndex);
    }

    // ── MoveUp / MoveDown ────────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveUpOnFirstRowGoesToStart()
    {
        TextBuffer buf = Buffer("hello");
        SetCaret(buf, 3);
        buf.MoveUp(80);
        Assert.Equal(0, buf.CaretIndex);
    }

    [Fact]
    public void MoveDownOnLastRowGoesToEnd()
    {
        TextBuffer buf = Buffer("hello");
        SetCaret(buf, 2);
        buf.MoveDown(80);
        Assert.Equal(5, buf.CaretIndex);
    }

    [Fact]
    public void MoveUpWrappedContentMovesPreviousVisualRow()
    {
        // "hello world" at width 5: row0="hello"(0-5), row1=" worl"(5-10), row2="d"(10-11)
        // Caret at 11 (end, row2 col 1). MoveUp → row1 at col 1 → index 6.
        TextBuffer buf = Buffer("hello world");
        SetCaret(buf, 11);
        buf.MoveUp(5);
        // row1 = " worl" starts at 5; col 1 = after ' ' → index 6
        Assert.Equal(6, buf.CaretIndex);
    }

    [Fact]
    public void MoveDownWrappedContentMovesNextVisualRow()
    {
        // "hello world" at width 5: row0="hello"(0-5), row1=" worl"(5-10)
        // Caret at 3 (col 3 on row0). MoveDown → row1 at col 3 → index 5+3=8.
        TextBuffer buf = Buffer("hello world");
        SetCaret(buf, 3);
        buf.MoveDown(5);
        Assert.Equal(8, buf.CaretIndex);
    }

    [Fact]
    public void MoveUpDownMultilogicalLines()
    {
        // "abc\ndef" at width 80: row0="abc", row1="def"
        TextBuffer buf = Buffer("abc\ndef");
        SetCaret(buf, 5); // 'e' in "def" (a=0,b=1,c=2,\n=3,d=4,e=5)
        buf.MoveUp(80);   // row0 at col 1 → index 1
        Assert.Equal(1, buf.CaretIndex);
        buf.MoveDown(80); // row1 at col 1 → index 5
        Assert.Equal(5, buf.CaretIndex);
    }

    // ── Render: wrap tracks caret ─────────────────────────────────────────────────────────────

    [Fact]
    public void RenderShortTextProducesOneRowWithCaretAtEnd()
    {
        TextBuffer buf = Buffer("hello");
        RenderResult r = buf.Render(80);
        Assert.Single(r.VisibleRows);
        Assert.Equal((0, 5), r.CaretPosition);
    }

    [Fact]
    public void RenderTextExceedingWidthWrapsToMultipleRows()
    {
        // "hello world" at width 5 → 3 rows
        TextBuffer buf = Buffer("hello world");
        RenderResult r = buf.Render(5);
        Assert.Equal(3, r.VisibleRows.Count);
    }

    [Fact]
    public void RenderCaretAtEndOfWrappedRowIsInRange()
    {
        // "hello world" at width 5: row0="hello", row1=" worl", row2="d"
        // Caret at 5 (wrap boundary). Either (0,5) or (1,0) are valid.
        TextBuffer buf = Buffer("hello world");
        SetCaret(buf, 5);
        RenderResult r = buf.Render(5);
        int row = r.CaretPosition.Row;
        int col = r.CaretPosition.Col;
        Assert.True((row == 0 && col == 5) || (row == 1 && col == 0),
            $"Expected caret at end of row0 or start of row1, got ({row},{col})");
    }

    [Fact]
    public void RenderCaretAfterWideCjkCharIsAtCol2()
    {
        // "中" is 2 columns. Caret after it should be at col 2.
        TextBuffer buf = Buffer("中");
        RenderResult r = buf.Render(80);
        Assert.Equal((0, 2), r.CaretPosition);
    }

    [Fact]
    public void RenderCaretAfterCombiningMarkColCountsBaseOnly()
    {
        // "é" as e+combining: visual width = 1.
        TextBuffer buf = Buffer("é");
        RenderResult r = buf.Render(80);
        Assert.Equal((0, 1), r.CaretPosition);
    }

    [Fact]
    public void RenderEmptyBufferProducesOneRowCaretAt00()
    {
        TextBuffer buf = new();
        RenderResult r = buf.Render(80);
        Assert.Single(r.VisibleRows);
        Assert.Equal((0, 0), r.CaretPosition);
    }

    [Fact]
    public void RenderMultilineBufferCaretOnCorrectRow()
    {
        // "abc\ndef" — caret at start of "def" = index 4
        TextBuffer buf = Buffer("abc\ndef");
        SetCaret(buf, 4);
        RenderResult r = buf.Render(80);
        Assert.Equal(2, r.VisibleRows.Count);
        Assert.Equal(1, r.CaretPosition.Row); // second visual row
        Assert.Equal(0, r.CaretPosition.Col); // start of row
    }

    [Fact]
    public void RenderTrailingNewlineProducesExtraEmptyRow()
    {
        TextBuffer buf = Buffer("abc\n");
        RenderResult r = buf.Render(80);
        // "abc\n" → row0="abc", row1="" (empty trailing row)
        Assert.Equal(2, r.VisibleRows.Count);
        // Caret at end (index 4) → on row1, col 0
        Assert.Equal(1, r.CaretPosition.Row);
        Assert.Equal(0, r.CaretPosition.Col);
    }

    // ── Internal scroll ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderAllottedHeightLimitsVisibleRows()
    {
        // 5 lines, allotted height = 3: see exactly 3 rows.
        TextBuffer buf = Buffer("a\nb\nc\nd\ne");
        RenderResult r = buf.Render(80, allottedHeight: 3);
        Assert.Equal(3, r.VisibleRows.Count);
    }

    [Fact]
    public void RenderAllottedHeightKeepsCaretVisible()
    {
        // 5 lines, allotted height = 2, caret at end (last row).
        TextBuffer buf = Buffer("a\nb\nc\nd\ne");
        RenderResult r = buf.Render(80, allottedHeight: 2);
        Assert.Equal(2, r.VisibleRows.Count);
        // Caret must be within the visible window.
        Assert.InRange(r.CaretPosition.Row, 0, 1);
    }

    [Fact]
    public void RenderAllottedHeightScrollsToKeepCaretAtTop()
    {
        // Caret at 'a' (start), allotted height = 2. Window starts at row 0.
        TextBuffer buf = Buffer("a\nb\nc\nd\ne");
        SetCaret(buf, 0);
        RenderResult r = buf.Render(80, allottedHeight: 2);
        Assert.Equal(2, r.VisibleRows.Count);
        Assert.Equal(0, r.CaretPosition.Row); // caret on first visible row
    }

    [Fact]
    public void RenderAllottedHeightNullShowsAllRows()
    {
        TextBuffer buf = Buffer("a\nb\nc");
        RenderResult r = buf.Render(80, allottedHeight: null);
        Assert.Equal(3, r.VisibleRows.Count);
    }

    [Fact]
    public void RenderAllottedHeightLargerThanContentShowsAll()
    {
        TextBuffer buf = Buffer("a\nb");
        RenderResult r = buf.Render(80, allottedHeight: 10);
        Assert.Equal(2, r.VisibleRows.Count);
    }

    [Fact]
    public void RenderAllottedHeightOneKeepsCaretVisible()
    {
        // 3 lines, height 1: single visible row must contain the caret.
        TextBuffer buf = Buffer("a\nb\nc");
        SetCaret(buf, 2); // 'b' (a=0,\n=1,b=2)
        RenderResult r = buf.Render(80, allottedHeight: 1);
        Assert.Single(r.VisibleRows);
        Assert.Equal(0, r.CaretPosition.Row); // caret is on the one visible row
    }

    // ── History recall ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddToHistoryThenRecallPreviousReplacesBuffer()
    {
        TextBuffer buf = new();
        buf.AddToHistory("first");
        buf.Insert("current");
        buf.RecallPrevious();
        Assert.Equal("first", buf.Text);
    }

    [Fact]
    public void RecallPreviousStashesCurrentBuffer()
    {
        TextBuffer buf = new();
        buf.AddToHistory("entry1");
        buf.Insert("live");
        buf.RecallPrevious(); // goes to "entry1", stashes "live"
        Assert.Equal("entry1", buf.Text);
        buf.RecallNext(); // past newest, restores stash
        Assert.Equal("live", buf.Text);
    }

    [Fact]
    public void RecallPreviousMultipleEntriesNavigatesOldestFirst()
    {
        TextBuffer buf = new();
        buf.AddToHistory("alpha");
        buf.AddToHistory("beta");
        buf.AddToHistory("gamma");

        buf.RecallPrevious(); // "gamma" (newest)
        Assert.Equal("gamma", buf.Text);
        buf.RecallPrevious(); // "beta"
        Assert.Equal("beta", buf.Text);
        buf.RecallPrevious(); // "alpha"
        Assert.Equal("alpha", buf.Text);
        buf.RecallPrevious(); // no-op at oldest
        Assert.Equal("alpha", buf.Text);
    }

    [Fact]
    public void RecallNextPastNewestRestoresStash()
    {
        TextBuffer buf = new();
        buf.AddToHistory("a");
        buf.AddToHistory("b");
        buf.Insert("live");
        buf.RecallPrevious(); // "b"
        buf.RecallPrevious(); // "a"
        buf.RecallNext();     // "b"
        Assert.Equal("b", buf.Text);
        buf.RecallNext();     // past newest → restore "live"
        Assert.Equal("live", buf.Text);
    }

    [Fact]
    public void RecallNextWhenNotInHistoryIsNoOp()
    {
        TextBuffer buf = Buffer("hello");
        buf.RecallNext(); // no-op
        Assert.Equal("hello", buf.Text);
    }

    [Fact]
    public void RecallPreviousWithEmptyHistoryIsNoOp()
    {
        TextBuffer buf = Buffer("hello");
        buf.RecallPrevious(); // no-op
        Assert.Equal("hello", buf.Text);
    }

    [Fact]
    public void AddToHistorySkipsDuplicateConsecutiveEntry()
    {
        TextBuffer buf = new();
        buf.AddToHistory("dup");
        buf.AddToHistory("dup");
        // Only one entry; RecallPrevious goes to it; another RecallPrevious is no-op.
        buf.RecallPrevious();
        Assert.Equal("dup", buf.Text);
        buf.RecallPrevious(); // no-op
        Assert.Equal("dup", buf.Text);
    }

    [Fact]
    public void AddToHistoryResetsHistoryNavigationAndStash()
    {
        TextBuffer buf = new();
        buf.AddToHistory("entry");
        buf.Insert("live");
        buf.RecallPrevious(); // stash "live", go to "entry"
        // AddToHistory resets navigation and stash.
        buf.AddToHistory("new");
        // History navigation now starts fresh. Next RecallPrevious → newest = "new".
        buf.Clear();
        buf.Insert("fresh");
        buf.RecallPrevious(); // should go to "new"
        Assert.Equal("new", buf.Text);
    }

    // ── Multiline editing ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultilineInsertCaretMovesCorrectly()
    {
        TextBuffer buf = new();
        buf.Insert("line1");
        buf.InsertNewline();
        buf.Insert("line2");
        Assert.Equal("line1\nline2", buf.Text);
        Assert.Equal(11, buf.CaretIndex);
    }

    [Fact]
    public void MultilineDeletionAtBoundaryMergesLines()
    {
        TextBuffer buf = Buffer("a\nb");
        // Caret at index 2 (after '\n', before 'b'). Backspace removes '\n'.
        SetCaret(buf, 2);
        buf.Backspace();
        Assert.Equal("ab", buf.Text);
        Assert.Equal(1, buf.CaretIndex);
    }

    [Fact]
    public void MultilineDeleteAtNewlineMergesLines()
    {
        TextBuffer buf = Buffer("a\nb");
        SetCaret(buf, 1); // caret on '\n'
        buf.Delete();     // removes '\n'
        Assert.Equal("ab", buf.Text);
        Assert.Equal(1, buf.CaretIndex);
    }

    // ── Caret at wide-char boundary ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderCaretAtWideCharBoundaryColIsCorrect()
    {
        // "中A" → caret after A: col = 2 (中) + 1 (A) = 3.
        TextBuffer buf = Buffer("中A");
        RenderResult r = buf.Render(80);
        Assert.Equal((0, 3), r.CaretPosition);
    }

    [Fact]
    public void RenderCaretBetweenWideCharsColIsCorrect()
    {
        // "中文" — caret after 中 (U+4E2D, BMP → 1 UTF-16 char, index 1)
        TextBuffer buf = Buffer("中文");
        SetCaret(buf, 1); // between the two CJK chars
        RenderResult r = buf.Render(80);
        Assert.Equal((0, 2), r.CaretPosition); // 中 = 2 cols
    }

    // ── Grapheme cluster correctness (10.2) ───────────────────────────────────────────────────

    [Fact]
    public void GraphemeBaseAndCombiningOneVisualMove()
    {
        // "é" is one grapheme cluster (2 chars). Right arrow moves by 2 char positions.
        string text = "é";
        TextBuffer buf = Buffer(text);
        SetCaret(buf, 0);
        buf.MoveRight();
        Assert.Equal(2, buf.CaretIndex);
    }

    [Fact]
    public void GraphemeZwjSequenceOneVisualMove()
    {
        // Woman+ZWJ+Laptop: 5 UTF-16 units, 1 grapheme cluster.
        string zwjSeq = "\U0001F469‍\U0001F4BB";
        TextBuffer buf = Buffer(zwjSeq);
        SetCaret(buf, 0);
        buf.MoveRight(); // should jump the entire ZWJ sequence
        Assert.Equal(zwjSeq.Length, buf.CaretIndex);
    }

    [Fact]
    public void GraphemeFlagSequenceOneVisualMove()
    {
        string flag = "\U0001F1EC\U0001F1E7"; // 🇬🇧, 4 UTF-16 units
        TextBuffer buf = Buffer(flag);
        SetCaret(buf, 0);
        buf.MoveRight();
        Assert.Equal(flag.Length, buf.CaretIndex);
    }

    [Fact]
    public void GraphemeBackspaceOverZwjSequenceDeletesAll()
    {
        string zwjSeq = "\U0001F469‍\U0001F4BB";
        TextBuffer buf = Buffer(zwjSeq);
        buf.Backspace();
        Assert.Equal(string.Empty, buf.Text);
    }

    [Fact]
    public void GraphemeBackspaceOverFlagSequenceDeletesAll()
    {
        string flag = "\U0001F1EC\U0001F1E7";
        TextBuffer buf = Buffer(flag);
        buf.Backspace();
        Assert.Equal(string.Empty, buf.Text);
    }

    // ── Wrapped row caret tracking ────────────────────────────────────────────────────────────

    [Fact]
    public void RenderWrapAtCjkCharRowsAreCorrect()
    {
        // "AB中" at width 3: "AB" fits (2 cols), 中 is 2 cols → wraps to next row.
        // row0 = "AB", row1 = "中"
        TextBuffer buf = Buffer("AB中");
        RenderResult r = buf.Render(3);
        Assert.Equal(2, r.VisibleRows.Count);
        // Caret at end: on row1, col 2.
        Assert.Equal(1, r.CaretPosition.Row);
        Assert.Equal(2, r.CaretPosition.Col);
    }

    [Fact]
    public void RenderOnlyWideCharsEachOnOwnRow()
    {
        // "中文" at width 2: each is exactly 2 cols → one per row.
        TextBuffer buf = Buffer("中文");
        RenderResult r = buf.Render(2);
        Assert.Equal(2, r.VisibleRows.Count);
    }

    // ── Prompt prefix ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetPromptEmptyLineProducesNoOffset()
    {
        // Empty prompt → behaviour identical to no prompt (regression guard).
        TextBuffer buf = Buffer("hello");
        buf.SetPrompt(new Line([]));
        RenderResult r = buf.Render(80);
        Assert.Single(r.VisibleRows);
        Assert.Equal(5, r.CaretPosition.Col); // caret at end, col = len("hello")
    }

    [Fact]
    public void SetPromptAddsSegmentToFirstVisualRow()
    {
        // Prompt "> " (2 cols) prepended to row 0 line.
        TextBuffer buf = Buffer("hello");
        buf.SetPrompt(Line.FromText("> "));
        RenderResult r = buf.Render(80);
        // First row segments: prompt + text.
        IReadOnlyList<Segment> segs = r.VisibleRows[0].Segments;
        // Prompt segment first, then the text segment.
        Assert.True(segs.Count >= 2);
        Assert.Equal("> ", segs[0].Text);
        Assert.Equal("hello", segs[1].Text);
    }

    [Fact]
    public void SetPromptOffsetsCaret()
    {
        // Prompt "> " = 2 cols. Caret at start of "hello" should be col 2.
        TextBuffer buf = Buffer("hello");
        buf.SetPrompt(Line.FromText("> "));
        buf.SetCaretIndexForTest(0);
        RenderResult r = buf.Render(80);
        Assert.Equal(2, r.CaretPosition.Col);
    }

    [Fact]
    public void SetPromptOffsetsCaretAtEndOfText()
    {
        // Prompt ">>> " = 4 cols. Caret at end of "hi" should be col 4 + 2 = 6.
        TextBuffer buf = Buffer("hi");
        buf.SetPrompt(Line.FromText(">>> "));
        RenderResult r = buf.Render(80);
        Assert.Equal(6, r.CaretPosition.Col);
    }

    [Fact]
    public void SetPromptReducesAvailableWidthForWrapping()
    {
        // Width 10, prompt ">>> " = 4 cols → first row content width = 6.
        // Text "abcdefghij" (10 chars) should wrap at 6 chars on row 0.
        TextBuffer buf = Buffer("abcdefghij");
        buf.SetPrompt(Line.FromText(">>> "));
        RenderResult r = buf.Render(10);
        // Row 0 wraps at 6 chars; remaining 4 chars on row 1.
        Assert.True(r.VisibleRows.Count >= 2);
        // Row 0: prompt ">>> " + 6 chars of text.
        IReadOnlyList<Segment> row0segs = r.VisibleRows[0].Segments;
        Assert.Equal(">>> ", row0segs[0].Text);
        Assert.Equal("abcdef", row0segs[1].Text);
        // Row 1: remainder, no prompt.
        Assert.Equal("ghij", r.VisibleRows[1].Segments[0].Text);
    }

    [Fact]
    public void SetPromptDoesNotAffectRow1AndBeyond()
    {
        // Prompt ">> " = 3 cols. Width 10. Multiline text.
        // Row 1 (second logical line) must not have prompt prepended.
        TextBuffer buf = Buffer("abc\ndef");
        buf.SetPrompt(Line.FromText(">> "));
        RenderResult r = buf.Render(80);
        Assert.Equal(2, r.VisibleRows.Count);
        // Row 0: has prompt + "abc"
        Assert.True(r.VisibleRows[0].Segments.Count >= 2);
        Assert.Equal(">> ", r.VisibleRows[0].Segments[0].Text);
        // Row 1: only "def", no prompt prefix.
        Assert.Equal("def", r.VisibleRows[1].Segments[0].Text);
        Assert.Single(r.VisibleRows[1].Segments);
    }

    [Fact]
    public void SetPromptTextDoesNotAppearInTextProperty()
    {
        // Prompt must never bleed into the editable text.
        TextBuffer buf = Buffer("hello");
        buf.SetPrompt(Line.FromText("> "));
        Assert.Equal("hello", buf.Text);
    }

    [Fact]
    public void SetPromptClearingLeavesNoOffset()
    {
        // Set a prompt, then clear it with an empty line. Caret should return to col 0.
        TextBuffer buf = Buffer("hi");
        buf.SetPrompt(Line.FromText("> "));
        buf.SetPrompt(new Line([])); // clear
        buf.SetCaretIndexForTest(0);
        RenderResult r = buf.Render(80);
        Assert.Equal(0, r.CaretPosition.Col);
    }

    [Fact]
    public void SetPromptEmptyTextProducesNoPrefix()
    {
        // Second regression guard: same behaviour as SetPromptEmptyLineProducesNoOffset.
        TextBuffer buf = Buffer("hello");
        buf.SetPrompt(new Line([]));
        RenderResult r = buf.Render(80);
        // Only one segment (the "hello" text), no prompt segment prepended.
        Assert.Equal("hello", r.VisibleRows[0].Segments[0].Text);
    }

    [Fact]
    public void SetPromptOnEmptyBufferProducesPromptOnlyRow()
    {
        // Prompt "$ " with empty buffer → first row has only prompt segments.
        TextBuffer buf = new();
        buf.SetPrompt(Line.FromText("$ "));
        RenderResult r = buf.Render(80);
        Assert.Single(r.VisibleRows);
        Assert.Equal("$ ", r.VisibleRows[0].Segments[0].Text);
        // Caret at col 2 (after prompt).
        Assert.Equal(2, r.CaretPosition.Col);
    }

    [Fact]
    public void SetPromptDoesNotClearOnSetText()
    {
        // SetText should not reset the prompt.
        TextBuffer buf = new();
        buf.SetPrompt(Line.FromText("> "));
        buf.SetText("new text");
        RenderResult r = buf.Render(80);
        Assert.Equal("> ", r.VisibleRows[0].Segments[0].Text);
    }

    [Fact]
    public void SetPromptDoesNotClearOnClear()
    {
        // Clear() clears the buffer but must not clear the prompt.
        TextBuffer buf = new();
        buf.SetPrompt(Line.FromText("> "));
        buf.SetText("some text");
        buf.Clear();
        RenderResult r = buf.Render(80);
        // After Clear(), buffer is empty but prompt should still be visible.
        Assert.Equal("> ", r.VisibleRows[0].Segments[0].Text);
        // Caret is after prompt (col 2), not at col 0.
        Assert.Equal(2, r.CaretPosition.Col);
    }
}
