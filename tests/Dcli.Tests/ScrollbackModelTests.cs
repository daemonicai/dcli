using Dcli.Internal.RenderLoop;
using Dcli.Internal.Scrollback;
using Dcli.Internal.Scrollback.Commands;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Behavioural tests for §9 (Chunk A): <see cref="ILineObject"/> / <see cref="TextBlock"/>,
/// the commit-horizon / live-window overflow logic in <see cref="ScrollbackModel"/>, and the
/// <see cref="LiveBlock"/> streaming-block contract.
/// </summary>
/// <remarks>
/// All tests operate purely on the model (no real terminal, no loop thread). The
/// <see cref="RenderModel"/> is constructed with a <see cref="FixedSizeSource"/> so terminal
/// dimensions are explicit.
/// </remarks>
public sealed class ScrollbackModelTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static RenderModel MakeModel(int cols = 80, int rows = 24) =>
        new(new FixedSizeSource(cols, rows));

    private static Line PlainLine(string text) =>
        new([new Segment(text)]);

    private static Line StyledLine(string text, Style style) =>
        new([new Segment(text, style)]);

    /// <summary>Extracts all text from a sequence of visual rows.</summary>
    private static string JoinRows(IEnumerable<Line> rows) =>
        string.Join("\n", rows.Select(r => string.Concat(r.Segments.Select(s => s.Text))));

    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }

    // ─── 9.1: TextBlock.Render ───────────────────────────────────────────────

    [Fact]
    public void TextBlockSingleLineRendersAsOneRow()
    {
        TextBlock block = new(PlainLine("hello"));
        IReadOnlyList<Line> rows = block.Render(80);

        Assert.Single(rows);
        Assert.Equal("hello", rows[0].Segments[0].Text);
    }

    [Fact]
    public void TextBlockLongLineWrapsToMultipleRows()
    {
        // A 10-char line rendered at width 4 should wrap to at least 3 rows.
        TextBlock block = new(PlainLine("0123456789"));
        IReadOnlyList<Line> rows = block.Render(4);

        Assert.True(rows.Count >= 3, $"expected ≥3 rows at width 4, got {rows.Count}");
        // All chars must be present across all rows.
        string combined = JoinRows(rows).Replace("\n", string.Empty, StringComparison.Ordinal);
        Assert.Equal("0123456789", combined);
    }

    [Fact]
    public void TextBlockCjkCharactersCountedAs2Columns()
    {
        // Each CJK char is 2 columns wide. At width 4, each pair of CJK chars fills one row.
        // "日本語" = 3 × 2 = 6 columns → at width 4: ["日本", "語"] = 2 rows.
        TextBlock block = new(PlainLine("日本語"));
        IReadOnlyList<Line> rows = block.Render(4);

        Assert.Equal(2, rows.Count);
        Assert.Equal("日本", rows[0].Segments[0].Text);
        Assert.Equal("語", rows[1].Segments[0].Text);
    }

    [Fact]
    public void TextBlockZeroWidthCharacterDoesNotInflateRowCount()
    {
        // U+200B (ZWSP) is zero-width. "AB" + ZWSP + "C" at width 3:
        // "AB" (2 cols) + ZWSP (0 cols) = 2 cols → fits in row 1; "C" (1 col) in row 2 or same.
        // The ZWSP must not force an extra wrap by being counted as a column.
        string text = "AB​C";
        TextBlock block = new(PlainLine(text));
        IReadOnlyList<Line> rows = block.Render(3);
        // Without ZWSP "ABC" at width 3 is 1 row; with ZWSP still fits in ≤2 rows.
        Assert.True(rows.Count <= 2, $"ZWSP inflated row count to {rows.Count}");
    }

    [Fact]
    public void TextBlockMultipleLogicalLinesEachWrapIndependently()
    {
        // Two 5-char lines at width 3 → each wraps into 2 rows → 4 total.
        TextBlock block = new([PlainLine("ABCDE"), PlainLine("12345")]);
        IReadOnlyList<Line> rows = block.Render(3);

        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void TextBlockEmptyBlockProducesOneEmptyRow()
    {
        TextBlock block = new(Array.Empty<Line>());
        IReadOnlyList<Line> rows = block.Render(80);

        Assert.Single(rows);
        Assert.Empty(rows[0].Segments);
    }

    [Fact]
    public void TextBlockStylePreservedAcrossWrappedRows()
    {
        Style bold = new(Format: Format.Bold);
        TextBlock block = new(StyledLine("ABCDE", bold));
        IReadOnlyList<Line> rows = block.Render(3);

        foreach (Line row in rows)
        {
            foreach (Segment seg in row.Segments)
                Assert.Equal(bold, seg.Style);
        }
    }

    // ─── 9.2: Commit horizon + bounded live window ───────────────────────────

    [Fact]
    public void ScrollbackAppendWithinBudgetKeepsAllRowsLive()
    {
        // 3 single-row lines in a 10-row terminal: all stay live.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        sm.Append(new TextBlock(PlainLine("line1")), model);
        sm.Append(new TextBlock(PlainLine("line2")), model);
        sm.Append(new TextBlock(PlainLine("line3")), model);
        sm.PrePaint(model);

        Assert.Equal(3, model.LiveWindowRows.Count);
        Assert.Empty(model.NewlyCommittedRows);
    }

    [Fact]
    public void ScrollbackOverflowCommitsTopLine()
    {
        // maxHeight = rows - fixedHeight = 3 - 0 = 3.
        // Append 4 single-row lines → the topmost one must commit.
        RenderModel model = MakeModel(cols: 80, rows: 3);
        ScrollbackModel sm = new();

        sm.Append(new TextBlock(PlainLine("A")), model);
        sm.Append(new TextBlock(PlainLine("B")), model);
        sm.Append(new TextBlock(PlainLine("C")), model);
        sm.Append(new TextBlock(PlainLine("D")), model);
        sm.PrePaint(model);

        // Live window must be exactly maxHeight = 3.
        Assert.Equal(3, model.LiveWindowRows.Count);
        // One row committed — the first one appended.
        Assert.Single(model.NewlyCommittedRows);
        Assert.Equal("A", model.NewlyCommittedRows[0].Segments[0].Text);
    }

    [Fact]
    public void ScrollbackLiveWindowNeverExceedsMaxHeight()
    {
        // maxHeight = 2. Append 10 single-row lines; after each PrePaint live ≤ 2.
        RenderModel model = MakeModel(cols: 80, rows: 2);
        ScrollbackModel sm = new();

        for (int i = 0; i < 10; i++)
        {
            sm.Append(new TextBlock(PlainLine($"line{i}")), model);
            sm.PrePaint(model);
            Assert.True(model.LiveWindowRows.Count <= 2,
                $"after append {i}: live={model.LiveWindowRows.Count} > maxHeight 2");
            sm.ClearCommitted(model);
        }
    }

    [Fact]
    public void ScrollbackCommittedRowsResetAfterClearCommitted()
    {
        // Frame 1: overflow → NewlyCommittedRows non-empty.
        // After ClearCommitted → empty.
        // Frame 2 with no new content → still empty.
        RenderModel model = MakeModel(cols: 80, rows: 2);
        ScrollbackModel sm = new();

        sm.Append(new TextBlock(PlainLine("A")), model);
        sm.Append(new TextBlock(PlainLine("B")), model);
        sm.Append(new TextBlock(PlainLine("C")), model);

        sm.PrePaint(model);
        Assert.NotEmpty(model.NewlyCommittedRows);

        sm.ClearCommitted(model);
        Assert.Empty(model.NewlyCommittedRows);

        // Second frame with no additional appends.
        model.MarkDirty();
        sm.PrePaint(model);
        Assert.Empty(model.NewlyCommittedRows);
    }

    [Fact]
    public void ScrollbackCommittedRowsEmittedOnlyOnce()
    {
        // The core "commit-once / never-rewrite" invariant: rows that commit in frame 1
        // must not appear in NewlyCommittedRows on frame 2.
        RenderModel model = MakeModel(cols: 80, rows: 2);
        ScrollbackModel sm = new();

        sm.Append(new TextBlock(PlainLine("A")), model);
        sm.Append(new TextBlock(PlainLine("B")), model);
        sm.Append(new TextBlock(PlainLine("overflow")), model);

        sm.PrePaint(model);
        Assert.NotEmpty(model.NewlyCommittedRows);

        sm.ClearCommitted(model);
        model.ClearDirty();

        // Frame 2: no new appends.
        model.MarkDirty();
        sm.PrePaint(model);
        Assert.Empty(model.NewlyCommittedRows);
    }

    [Fact]
    public void ScrollbackZeroRowTerminalAllLinesCommit()
    {
        // maxHeight = max(0, 0 - 0) = 0 → every appended row immediately commits.
        RenderModel model = MakeModel(cols: 80, rows: 0);
        ScrollbackModel sm = new();

        sm.Append(new TextBlock(PlainLine("A")), model);
        sm.Append(new TextBlock(PlainLine("B")), model);
        sm.PrePaint(model);

        Assert.Empty(model.LiveWindowRows);
        Assert.Equal(2, model.NewlyCommittedRows.Count);
    }

    [Fact]
    public void ScrollbackWrappedBlockRowCountUsedForOverflowMath()
    {
        // width=2 → "ABCD" wraps to 2 rows ("AB", "CD"). maxHeight = 3.
        // Two 2-row blocks → 4 rows > 3 → first block (2 rows) commits.
        RenderModel model = MakeModel(cols: 2, rows: 3);
        ScrollbackModel sm = new();

        sm.Append(new TextBlock(PlainLine("ABCD")), model);
        sm.Append(new TextBlock(PlainLine("EFGH")), model);
        sm.PrePaint(model);

        Assert.Equal(2, model.NewlyCommittedRows.Count); // "AB" and "CD"
        Assert.Equal(2, model.LiveWindowRows.Count);     // "EF" and "GH"
    }

    [Fact]
    public void ScrollbackFixedRegionReducesLiveWindowBudget()
    {
        // rows=5, fixedRegion has 2 rows → maxHeight = 3.
        // Append 4 single-row lines → overflow.
        RenderModel model = MakeModel(cols: 80, rows: 5);
        model.FixedRegionRows =
        [
            new Line([new Segment("status1")]),
            new Line([new Segment("status2")])
        ];
        ScrollbackModel sm = new();

        for (int i = 0; i < 4; i++)
            sm.Append(new TextBlock(PlainLine($"line{i}")), model);
        sm.PrePaint(model);

        Assert.True(model.LiveWindowRows.Count <= 3,
            $"live={model.LiveWindowRows.Count} > maxHeight 3 with fixedRegion=2");
        Assert.NotEmpty(model.NewlyCommittedRows);
    }

    // ─── 9.3: LiveBlock streaming ─────────────────────────────────────────────

    [Fact]
    public void LiveBlockAppendTextAppearsInLiveWindow()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        LiveBlock block = sm.BeginLive(model);
        block.AppendText("hello ");
        block.AppendText("world");
        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("hello world", liveText, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveBlockSetContentReplacesAppendedText()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        LiveBlock block = sm.BeginLive(model);
        block.AppendText("initial");
        block.SetContent([PlainLine("replaced content")]);
        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("replaced content", liveText, StringComparison.Ordinal);
        Assert.DoesNotContain("initial", liveText, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveBlockAppendTextAfterSetContentIsIgnored()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        LiveBlock block = sm.BeginLive(model);
        block.SetContent([PlainLine("replacement")]);
        block.AppendText("ignored");
        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("replacement", liveText, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", liveText, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveBlockCommitRemovesBlockFromLiveWindow()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        LiveBlock block = sm.BeginLive(model);
        block.AppendText("streaming...");

        // Frame 1: block is live.
        sm.PrePaint(model);
        Assert.NotEmpty(model.LiveWindowRows);
        sm.ClearCommitted(model);
        model.ClearDirty();

        // Commit the block then paint frame 2.
        sm.CommitLiveBlock(block, model);
        sm.PrePaint(model);

        Assert.Empty(model.LiveWindowRows);                // no longer live
        Assert.NotEmpty(model.NewlyCommittedRows);         // drained into committed
        Assert.Contains("streaming...",
            model.NewlyCommittedRows[0].Segments[0].Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LiveBlockCommitAfterSetContentFreezesReplacedContent()
    {
        // "replace-then-commit": the replaced content is what freezes.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        LiveBlock block = sm.BeginLive(model);
        block.AppendText("draft");
        block.SetContent([PlainLine("final")]);
        sm.CommitLiveBlock(block, model);
        sm.PrePaint(model);

        string committed = JoinRows(model.NewlyCommittedRows);
        Assert.Contains("final", committed, StringComparison.Ordinal);
        Assert.DoesNotContain("draft", committed, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveBlockAppendTextAfterCommitIsIgnored()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        LiveBlock block = sm.BeginLive(model);
        block.AppendText("before");
        sm.CommitLiveBlock(block, model);
        block.AppendText("after-commit-ignored");

        sm.PrePaint(model);

        string committed = JoinRows(model.NewlyCommittedRows);
        Assert.Contains("before", committed, StringComparison.Ordinal);
        Assert.DoesNotContain("after-commit-ignored", committed, StringComparison.Ordinal);
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [Fact]
    public void AppendLineCommandAddsLineToLiveWindow()
    {
        RenderModel model = MakeModel();
        ScrollbackModel sm = new();
        ILoopCommand cmd = new AppendLineCommand(sm, PlainLine("via command"));
        cmd.Apply(model);
        sm.PrePaint(model);

        Assert.Contains("via command", JoinRows(model.LiveWindowRows), StringComparison.Ordinal);
    }

    [Fact]
    public void BeginLiveCommandCapturesBlockRef()
    {
        RenderModel model = MakeModel();
        ScrollbackModel sm = new();

        LiveBlock? captured = null;
        ILoopCommand cmd = new BeginLiveCommand(sm, b => captured = b);
        cmd.Apply(model);

        Assert.NotNull(captured);
        captured.AppendText("test");
        sm.PrePaint(model);

        Assert.Contains("test", JoinRows(model.LiveWindowRows), StringComparison.Ordinal);
    }

    [Fact]
    public void AppendTextCommandGrowsLiveBlock()
    {
        RenderModel model = MakeModel();
        ScrollbackModel sm = new();
        LiveBlock block = sm.BeginLive(model);

        ILoopCommand cmd = new AppendTextCommand(block, "token");
        cmd.Apply(model);
        sm.PrePaint(model);

        Assert.Contains("token", JoinRows(model.LiveWindowRows), StringComparison.Ordinal);
    }

    [Fact]
    public void SetContentCommandReplacesContent()
    {
        RenderModel model = MakeModel();
        ScrollbackModel sm = new();
        LiveBlock block = sm.BeginLive(model);
        block.AppendText("old");

        ILoopCommand cmd = new SetContentCommand(block, [PlainLine("new content")]);
        cmd.Apply(model);
        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("new content", liveText, StringComparison.Ordinal);
        Assert.DoesNotContain("old", liveText, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitLiveBlockCommandFreezesBlock()
    {
        RenderModel model = MakeModel();
        ScrollbackModel sm = new();
        LiveBlock block = sm.BeginLive(model);
        block.AppendText("frozen content");

        ILoopCommand cmd = new CommitLiveBlockCommand(sm, block);
        cmd.Apply(model);

        Assert.True(block.IsCommitted);
    }

    // ─── 9.2 nit: FIFO sweep stops at first non-committed object ──────────────

    [Fact]
    public void CommittedLiveBlockBehindNonCommittedObjectIsNotSweptUntilObjectAheadCommits()
    {
        // FIFO invariant: the sweep in PrePaint walks from the front and stops at the first
        // object that is NOT a committed LiveBlock. A committed LiveBlock sitting behind a
        // non-committed TextBlock is therefore NOT swept in the same frame.
        RenderModel model = MakeModel(cols: 80, rows: 20);
        ScrollbackModel sm = new();

        // layout: [TextBlock (never committed)] [LiveBlock (committed immediately)]
        sm.Append(new TextBlock(PlainLine("text-ahead")), model);
        LiveBlock block = sm.BeginLive(model);
        block.AppendText("behind-text");
        sm.CommitLiveBlock(block, model);

        sm.PrePaint(model);

        // The committed LiveBlock is behind a TextBlock which has not committed.
        // The sweep stops at the TextBlock → the committed LiveBlock is NOT drained into
        // NewlyCommittedRows; it stays in the live window.
        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("text-ahead", liveText, StringComparison.Ordinal);
        Assert.Contains("behind-text", liveText, StringComparison.Ordinal);
        // Nothing committed because the TextBlock at the front did not overflow.
        Assert.Empty(model.NewlyCommittedRows);
    }

    // ─── 9.4: Collapsible one-way expand ─────────────────────────────────────

    [Fact]
    public void CollapsibleCreatedCollapsedRendersOnlySummary()
    {
        Collapsible c = new(PlainLine("summary"), [PlainLine("hidden line 1"), PlainLine("hidden line 2")]);
        IReadOnlyList<Line> rows = c.Render(80);

        Assert.Single(rows);
        Assert.Equal("summary", rows[0].Segments[0].Text);
    }

    [Fact]
    public void CollapsibleExpandRevealsSummaryPlusHiddenLines()
    {
        Collapsible c = new(PlainLine("summary"), [PlainLine("detail A"), PlainLine("detail B")]);
        c.Expand();
        IReadOnlyList<Line> rows = c.Render(80);

        // summary + 2 hidden = 3 rows
        Assert.Equal(3, rows.Count);
        Assert.Equal("summary", rows[0].Segments[0].Text);
        Assert.Equal("detail A", rows[1].Segments[0].Text);
        Assert.Equal("detail B", rows[2].Segments[0].Text);
    }

    [Fact]
    public void CollapsibleExpandIsIdempotent()
    {
        // Second Expand call must not change state or throw.
        Collapsible c = new(PlainLine("summary"), [PlainLine("hidden")]);
        c.Expand();
        c.Expand(); // no-op

        Assert.True(c.IsExpanded);
        IReadOnlyList<Line> rows = c.Render(80);
        Assert.Equal(2, rows.Count); // not duplicated
    }

    [Fact]
    public void CollapsibleCannotRecollapseAfterExpand()
    {
        // There is no re-collapse operation; verify Expand stays expanded forever.
        Collapsible c = new(PlainLine("summary"), [PlainLine("hidden")]);
        c.Expand();
        Assert.True(c.IsExpanded);

        // No collapse API — IsExpanded can only go false→true.
        // Verify Render still returns expanded content.
        IReadOnlyList<Line> rows = c.Render(80);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void CollapsibleInLiveWindowShowsSummaryOnly()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        Collapsible c = sm.BeginCollapsible(PlainLine("summary"), [PlainLine("hidden")], model);
        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("summary", liveText, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", liveText, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsibleExpandViaModelBecomesLive()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        Collapsible c = sm.BeginCollapsible(PlainLine("summary"), [PlainLine("detail")], model);
        sm.ExpandCollapsible(c, model);
        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("summary", liveText, StringComparison.Ordinal);
        Assert.Contains("detail", liveText, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsibleExpandTwiceViaModelIsNoOp()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        Collapsible c = sm.BeginCollapsible(PlainLine("summary"), [PlainLine("detail")], model);
        sm.ExpandCollapsible(c, model);
        sm.ExpandCollapsible(c, model); // idempotent

        sm.PrePaint(model);
        string liveText = JoinRows(model.LiveWindowRows);

        // "detail" appears exactly once (not doubled).
        int count = 0;
        int idx = 0;
        while ((idx = liveText.IndexOf("detail", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += "detail".Length;
        }
        Assert.Equal(1, count);
    }

    // ─── 9.4: Freeze-collapsed-at-horizon ────────────────────────────────────

    [Fact]
    public void CollapsibleFreezeCollapsedAtHorizonMakesExpandNoOp()
    {
        // maxHeight = 2 rows. Append enough content to force the collapsible (at the front)
        // to commit, then attempt to expand it — must be a clean no-op.
        RenderModel model = MakeModel(cols: 80, rows: 2);
        ScrollbackModel sm = new();

        // The collapsible occupies 1 row (summary). With maxHeight=2, adding 2 more single-row
        // TextBlocks will push the collapsible past the horizon.
        Collapsible c = sm.BeginCollapsible(PlainLine("frozen-summary"), [PlainLine("frozen-hidden")], model);
        sm.Append(new TextBlock(PlainLine("filler1")), model);
        sm.Append(new TextBlock(PlainLine("filler2")), model);

        sm.PrePaint(model); // collapsible overflows → committed and dropped
        sm.ClearCommitted(model);

        // The collapsible is now frozen in native scrollback. Expand must be a no-op.
        sm.ExpandCollapsible(c, model);
        sm.PrePaint(model);

        // The live window contains the two filler lines, not the expanded hidden content.
        string liveText = JoinRows(model.LiveWindowRows);
        Assert.DoesNotContain("frozen-hidden", liveText, StringComparison.Ordinal);
        // Nothing new commits (the collapsible was already committed in a previous frame).
        Assert.Empty(model.NewlyCommittedRows);
    }

    // ─── 9.5: Oversized expansion reprints into flow ─────────────────────────

    [Fact]
    public void CollapsibleOversizedExpansionReprintsHiddenLinesIntoCommittedFlow()
    {
        // maxHeight = 3 rows. The collapsible summary = 1 row; hidden = 5 rows (5 lines).
        // Expanded height = 6 > 3 → oversized path: hidden lines go to NewlyCommittedRows,
        // block stays a collapsed summary in the live window.
        RenderModel model = MakeModel(cols: 80, rows: 3);
        ScrollbackModel sm = new();

        IReadOnlyList<Line> hiddenLines =
        [
            PlainLine("h1"), PlainLine("h2"), PlainLine("h3"),
            PlainLine("h4"), PlainLine("h5")
        ];
        Collapsible c = sm.BeginCollapsible(PlainLine("summary"), hiddenLines, model);

        sm.ExpandCollapsible(c, model);

        // Block must still be collapsed (not expanded).
        Assert.False(c.IsExpanded);

        sm.PrePaint(model);

        // Live window contains only the summary (collapsed marker).
        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("summary", liveText, StringComparison.Ordinal);
        Assert.DoesNotContain("h1", liveText, StringComparison.Ordinal);

        // Hidden lines were emitted into committed flow.
        string committed = JoinRows(model.NewlyCommittedRows);
        Assert.Contains("h1", committed, StringComparison.Ordinal);
        Assert.Contains("h5", committed, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsibleOversizedReprintEmittedOnlyOnce()
    {
        // The reprinted rows must not appear in NewlyCommittedRows on the next frame.
        RenderModel model = MakeModel(cols: 80, rows: 3);
        ScrollbackModel sm = new();

        IReadOnlyList<Line> hidden = [PlainLine("h1"), PlainLine("h2"), PlainLine("h3"), PlainLine("h4")];
        Collapsible c = sm.BeginCollapsible(PlainLine("summary"), hidden, model);
        sm.ExpandCollapsible(c, model);
        sm.PrePaint(model);
        Assert.NotEmpty(model.NewlyCommittedRows); // frame 1: emitted

        sm.ClearCommitted(model);
        model.MarkDirty();
        sm.PrePaint(model);
        Assert.Empty(model.NewlyCommittedRows); // frame 2: not re-emitted
    }

    [Fact]
    public void CollapsibleFittingExpansionStaysLiveRenderable()
    {
        // maxHeight = 10, summary = 1 row, hidden = 2 rows → expanded = 3 ≤ 10.
        // Normal path: block is marked expanded; live window shows all 3 rows.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        Collapsible c = sm.BeginCollapsible(PlainLine("summary"), [PlainLine("d1"), PlainLine("d2")], model);
        sm.ExpandCollapsible(c, model);

        Assert.True(c.IsExpanded);

        sm.PrePaint(model);
        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("summary", liveText, StringComparison.Ordinal);
        Assert.Contains("d1", liveText, StringComparison.Ordinal);
        Assert.Contains("d2", liveText, StringComparison.Ordinal);
        Assert.Empty(model.NewlyCommittedRows);
    }

    // ─── BeginCollapsibleCommand ──────────────────────────────────────────────

    [Fact]
    public void BeginCollapsibleCommandCapturesCollapsibleRef()
    {
        RenderModel model = MakeModel();
        ScrollbackModel sm = new();

        Collapsible? captured = null;
        ILoopCommand cmd = new BeginCollapsibleCommand(
            sm,
            PlainLine("summary"),
            [PlainLine("hidden")],
            c => captured = c);
        cmd.Apply(model);

        Assert.NotNull(captured);
        sm.PrePaint(model);
        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("summary", liveText, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandCollapsibleCommandExpandsBlockInLiveWindow()
    {
        RenderModel model = MakeModel(cols: 80, rows: 10);
        ScrollbackModel sm = new();

        Collapsible? captured = null;
        ILoopCommand beginCmd = new BeginCollapsibleCommand(
            sm,
            PlainLine("summary"),
            [PlainLine("detail")],
            c => captured = c);
        beginCmd.Apply(model);

        Assert.NotNull(captured);
        ILoopCommand expandCmd = new ExpandCollapsibleCommand(sm, captured);
        expandCmd.Apply(model);

        sm.PrePaint(model);
        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("detail", liveText, StringComparison.Ordinal);
    }
}
