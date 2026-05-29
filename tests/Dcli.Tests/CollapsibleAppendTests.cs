using Dcli.Internal.RenderLoop;
using Dcli.Internal.Scrollback;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Behavioural tests for §3 (api-ergonomics-pass-2): incremental <c>AppendLine</c> on
/// <see cref="ICollapsible"/> / <see cref="Collapsible"/> / <see cref="ScrollbackModel"/>.
/// All tests operate purely on the model (no real terminal, no loop thread).
/// </summary>
public sealed class CollapsibleAppendTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static RenderModel MakeModel(int cols = 80, int rows = 24) =>
        new(new FixedSizeSource(cols, rows));

    private static Line PlainLine(string text) =>
        new([new Segment(text)]);

    private static string JoinRows(IEnumerable<Line> rows) =>
        string.Join("\n", rows.Select(r => string.Concat(r.Segments.Select(s => s.Text))));

    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }

    // ─── 3.4: Append before expansion → revealed on expand ───────────────────

    /// <summary>
    /// AppendLine before Expand adds the line to the hidden set; after Expand the live window
    /// shows original hidden lines followed by the appended line, in order.
    /// </summary>
    [Fact]
    public void AppendBeforeExpansionGrowsHiddenSetRevealedOnExpand()
    {
        // rows=24 → maxHeight=24; hidden set fits.
        RenderModel model = MakeModel(cols: 80, rows: 24);
        ScrollbackModel sm = new();

        Collapsible c = sm.BeginCollapsible(
            PlainLine("summary"),
            [PlainLine("original-h1"), PlainLine("original-h2")],
            model);

        // Append before expansion.
        sm.AppendToCollapsible(c, PlainLine("appended-h3"), model);

        // Expand (normal path — fits live window).
        sm.ExpandCollapsible(c, model);
        Assert.True(c.IsExpanded);

        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        // All four rows must appear in order.
        int idxSummary = liveText.IndexOf("summary", StringComparison.Ordinal);
        int idxOrigH1 = liveText.IndexOf("original-h1", StringComparison.Ordinal);
        int idxOrigH2 = liveText.IndexOf("original-h2", StringComparison.Ordinal);
        int idxAppended = liveText.IndexOf("appended-h3", StringComparison.Ordinal);

        Assert.True(idxSummary >= 0, "summary must appear");
        Assert.True(idxOrigH1 >= 0, "original-h1 must appear");
        Assert.True(idxOrigH2 >= 0, "original-h2 must appear");
        Assert.True(idxAppended >= 0, "appended-h3 must appear");

        // Order: summary < original-h1 < original-h2 < appended-h3.
        Assert.True(idxSummary < idxOrigH1,
            "summary must precede original-h1");
        Assert.True(idxOrigH1 < idxOrigH2,
            "original-h1 must precede original-h2");
        Assert.True(idxOrigH2 < idxAppended,
            "original-h2 must precede appended-h3 (append order preserved)");
    }

    // ─── 3.5: Append after expansion is a no-op ──────────────────────────────

    /// <summary>
    /// AppendLine after Expand is silently dropped; the revealed content is unchanged.
    /// </summary>
    [Fact]
    public void AppendAfterExpansionIsNoOp()
    {
        RenderModel model = MakeModel(cols: 80, rows: 24);
        ScrollbackModel sm = new();

        Collapsible c = sm.BeginCollapsible(
            PlainLine("summary"),
            [PlainLine("hidden-original")],
            model);

        // Expand first.
        sm.ExpandCollapsible(c, model);
        Assert.True(c.IsExpanded);

        sm.PrePaint(model);
        string liveBeforeAppend = JoinRows(model.LiveWindowRows);

        // Now attempt to append — must be no-op.
        sm.AppendToCollapsible(c, PlainLine("should-not-appear"), model);

        sm.PrePaint(model);
        string liveAfterAppend = JoinRows(model.LiveWindowRows);

        Assert.DoesNotContain("should-not-appear", liveAfterAppend, StringComparison.Ordinal);
        // Visible content is the same as before the append attempt.
        Assert.Equal(liveBeforeAppend, liveAfterAppend);
    }

    // ─── 3.6: Append after horizon-freeze is a no-op ─────────────────────────

    /// <summary>
    /// AppendLine on a collapsible that has been committed past the horizon (dropped from the
    /// live list while still collapsed) is silently dropped.
    /// </summary>
    [Fact]
    public void AppendAfterHorizonFreezeIsNoOp()
    {
        // maxHeight=2. The collapsible (1 row) gets pushed past the horizon by 2 filler rows.
        RenderModel model = MakeModel(cols: 80, rows: 2);
        ScrollbackModel sm = new();

        Collapsible c = sm.BeginCollapsible(
            PlainLine("frozen-summary"),
            [PlainLine("frozen-hidden")],
            model);

        sm.Append(new TextBlock(PlainLine("filler1")), model);
        sm.Append(new TextBlock(PlainLine("filler2")), model);

        // Force the collapsible past the horizon.
        sm.PrePaint(model);
        sm.ClearCommitted(model);

        // The collapsible is no longer in the live list. Append must be a no-op.
        sm.AppendToCollapsible(c, PlainLine("post-freeze-append"), model);

        sm.PrePaint(model);

        string liveText = JoinRows(model.LiveWindowRows);
        Assert.DoesNotContain("post-freeze-append", liveText, StringComparison.Ordinal);
        Assert.DoesNotContain("frozen-hidden", liveText, StringComparison.Ordinal);
        // Nothing extra committed by the append attempt.
        Assert.Empty(model.NewlyCommittedRows);
    }

    // ─── 3.7: Oversized-expansion ordering with prior AppendLine ─────────────

    /// <summary>
    /// AppendLine before an oversized expansion does not reorder or duplicate hidden rows.
    /// The committed flow contains original hidden lines followed by the appended line, in
    /// append order — the natural ordering the oversized reprint would produce.
    /// </summary>
    [Fact]
    public void AppendBeforeOversizedExpansionPreservesCommitOrdering()
    {
        // maxHeight=3. summary=1 row; original hidden=4 rows; appended=1 row → total hidden=5.
        // Expanded height = 1 (summary) + 5 (hidden) = 6 > 3 → oversized path.
        RenderModel model = MakeModel(cols: 80, rows: 3);
        ScrollbackModel sm = new();

        IReadOnlyList<Line> initialHidden =
        [
            PlainLine("h1"), PlainLine("h2"), PlainLine("h3"), PlainLine("h4")
        ];
        Collapsible c = sm.BeginCollapsible(PlainLine("summary"), initialHidden, model);

        // Append before expansion.
        sm.AppendToCollapsible(c, PlainLine("h5-appended"), model);

        // Trigger oversized expansion.
        sm.ExpandCollapsible(c, model);

        // Block must still be collapsed (oversized path does not call Expand()).
        Assert.False(c.IsExpanded);

        sm.PrePaint(model);

        // Live window: only the summary (collapsed marker).
        string liveText = JoinRows(model.LiveWindowRows);
        Assert.Contains("summary", liveText, StringComparison.Ordinal);
        Assert.DoesNotContain("h1", liveText, StringComparison.Ordinal);

        // Committed flow: all hidden rows in order (h1..h4 then h5-appended).
        string committed = JoinRows(model.NewlyCommittedRows);
        Assert.Contains("h1", committed, StringComparison.Ordinal);
        Assert.Contains("h4", committed, StringComparison.Ordinal);
        Assert.Contains("h5-appended", committed, StringComparison.Ordinal);

        // Verify natural ordering: h1 < h2 < h3 < h4 < h5-appended.
        int idxH1 = committed.IndexOf("h1", StringComparison.Ordinal);
        int idxH2 = committed.IndexOf("h2", StringComparison.Ordinal);
        int idxH3 = committed.IndexOf("h3", StringComparison.Ordinal);
        int idxH4 = committed.IndexOf("h4", StringComparison.Ordinal);
        int idxH5 = committed.IndexOf("h5-appended", StringComparison.Ordinal);

        Assert.True(idxH1 < idxH2, "h1 before h2");
        Assert.True(idxH2 < idxH3, "h2 before h3");
        Assert.True(idxH3 < idxH4, "h3 before h4");
        Assert.True(idxH4 < idxH5, "h4 before h5-appended (appended row last)");

        // Verify reprint is emitted only once (guard from existing oversized reprint test).
        sm.ClearCommitted(model);
        model.MarkDirty();
        sm.PrePaint(model);
        Assert.Empty(model.NewlyCommittedRows);
    }

    // ─── Collapsible.AppendHidden unit test ───────────────────────────────────

    /// <summary>
    /// <see cref="Collapsible.AppendHidden"/> adds to the hidden set visible after expansion.
    /// </summary>
    [Fact]
    public void CollapsibleAppendHiddenAppearsAfterExpand()
    {
        Collapsible c = new(PlainLine("summary"), [PlainLine("h1")]);
        c.AppendHidden(PlainLine("h2-appended"));
        c.Expand();

        IReadOnlyList<Line> rows = c.Render(80);
        // summary + h1 + h2-appended = 3 rows.
        Assert.Equal(3, rows.Count);
        Assert.Equal("summary", rows[0].Segments[0].Text);
        Assert.Equal("h1", rows[1].Segments[0].Text);
        Assert.Equal("h2-appended", rows[2].Segments[0].Text);
    }

    /// <summary>
    /// The <see cref="Collapsible"/> ctor makes an owned copy: mutating the caller's list
    /// after construction does not affect the collapsible's hidden set.
    /// </summary>
    [Fact]
    public void CollapsibleCtorCopiesHiddenLinesList()
    {
        List<Line> callerList = [PlainLine("original")];
        Collapsible c = new(PlainLine("summary"), callerList);

        // Mutate the original list after construction.
        callerList.Add(PlainLine("injected-after-construction"));

        c.Expand();
        IReadOnlyList<Line> rows = c.Render(80);

        // Only summary + original; the post-construction add must NOT appear.
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.Segments.Any(s => s.Text.Contains("injected", StringComparison.Ordinal)));
    }
}
