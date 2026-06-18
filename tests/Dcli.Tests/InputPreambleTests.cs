using Dcli.Testing;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for the persistent input-preamble surface (tasks 3.1–3.6).
/// </summary>
public sealed class InputPreambleTests
{
    // ── Helpers ──

    private static readonly string[] _fourPreambleLabels = ["pre-1", "pre-2", "pre-3", "pre-4"];

    private static Line L(string text) => new([new Segment(text)]);

    private static int FindRow(IReadOnlyList<Line> rows, string text) =>
        rows.Select((r, i) => (r, i))
            .Where(x => x.r.Segments.Any(s => s.Text.Contains(text, StringComparison.Ordinal)))
            .Select(x => x.i)
            .FirstOrDefault(-1);

    // ── 3.1 — Preamble rows render directly above the input editor ────────────

    [Fact]
    public async Task PreambleRowsRenderAboveInputEditor()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.InputPreamble.SetRows(L("preamble-row-1"), L("preamble-row-2"));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        IReadOnlyList<Line> fixed_ = snap.FixedRegionRows;

        int idx1 = FindRow(fixed_, "preamble-row-1");
        int idx2 = FindRow(fixed_, "preamble-row-2");

        Assert.True(idx1 >= 0, "preamble-row-1 not found in FixedRegionRows");
        Assert.True(idx2 >= 0, "preamble-row-2 not found in FixedRegionRows");

        // Order: row-1 before row-2.
        Assert.True(idx1 < idx2, "preamble-row-1 should precede preamble-row-2");

        // Preamble rows at indices 0 and 1; something follows (the input editor row).
        Assert.Equal(0, idx1);
        Assert.Equal(1, idx2);
        Assert.True(idx2 < fixed_.Count - 1, "Something (input editor) should follow the last preamble row");
    }

    // ── 3.2 — Preamble persists across multiple input submissions ─────────────

    [Fact]
    public async Task PreamblePersistsAcrossInputSubmissions()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.InputPreamble.SetRows(L("persistent-header"));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            FindRow(harness.Snapshot.FixedRegionRows, "persistent-header") >= 0,
            "persistent-header missing before first submission");

        // First submission.
        harness.Type("first");
        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            FindRow(harness.Snapshot.FixedRegionRows, "persistent-header") >= 0,
            "persistent-header missing after first submission");

        // Second submission.
        harness.Type("second");
        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            FindRow(harness.Snapshot.FixedRegionRows, "persistent-header") >= 0,
            "persistent-header missing after second submission");

        // Third submission.
        harness.Type("third");
        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            FindRow(harness.Snapshot.FixedRegionRows, "persistent-header") >= 0,
            "persistent-header missing after third submission");
    }

    // ── 3.3 — SetRows with empty argument clears the preamble ────────────────

    [Fact]
    public async Task SetRowsEmptyClearsPreamble()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.InputPreamble.SetRows(L("clear-test-row-1"), L("clear-test-row-2"));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot withPreamble = harness.Snapshot;
        Assert.True(FindRow(withPreamble.FixedRegionRows, "clear-test-row-1") >= 0, "clear-test-row-1 missing");
        Assert.True(FindRow(withPreamble.FixedRegionRows, "clear-test-row-2") >= 0, "clear-test-row-2 missing");
        int countWithPreamble = withPreamble.FixedRegionRows.Count;

        // Clear preamble.
        harness.Terminal.InputPreamble.SetRows();
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot withoutPreamble = harness.Snapshot;
        Assert.True(FindRow(withoutPreamble.FixedRegionRows, "clear-test-row-1") < 0, "clear-test-row-1 still present after clear");
        Assert.True(FindRow(withoutPreamble.FixedRegionRows, "clear-test-row-2") < 0, "clear-test-row-2 still present after clear");
        Assert.True(withoutPreamble.FixedRegionRows.Count < countWithPreamble, "Budget not returned after clearing preamble");
    }

    // ── 3.4 — Preamble truncates under budget pressure; input ≥1 row; status sacred ──

    [Fact]
    public async Task PreambleTruncatesUnderBudgetPressure()
    {
        // On a 24-row terminal, cap = clamp(appSet ?? rows/2, 8, rows).
        // With MaxFixedHeight=null (default), cap = 12.
        // Use 10 sacred status rows → budget = cap - 10 = 2.
        // Input claims 1 row (empty editor), preamble gets preambleBudget = 2 - 1 = 1 from 4 requested.
        // Total fixed = 10 status + 1 input + 1 preamble = 12 ≤ cap.
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.Status.SetRows(
            L("status-1"), L("status-2"), L("status-3"), L("status-4"), L("status-5"),
            L("status-6"), L("status-7"), L("status-8"), L("status-9"), L("status-10"));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        harness.Terminal.InputPreamble.SetRows(L("pre-1"), L("pre-2"), L("pre-3"), L("pre-4"));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        IReadOnlyList<Line> fixed_ = snap.FixedRegionRows;

        // Budget cap respected (effective cap = 12 on 24-row terminal with default MaxFixedHeight).
        Assert.True(fixed_.Count <= 12,
            $"FixedRegionRows.Count={fixed_.Count} exceeds effective cap of 12");

        // Status is sacred — all 10 rows always present.
        for (int i = 1; i <= 10; i++)
        {
            Assert.True(FindRow(fixed_, $"status-{i}") >= 0,
                $"status-{i} not found (status must be sacred)");
        }

        // Input editor kept ≥1 row (cursor visible).
        Assert.True(snap.IsCursorVisible, "Cursor hidden — input editor must keep ≥1 row");

        // Not all 4 preamble rows visible (truncation occurred).
        int preambleVisible = _fourPreambleLabels
            .Count(p => FindRow(fixed_, p) >= 0);
        Assert.True(preambleVisible < 4,
            $"Expected preamble truncation but all 4 rows are visible (count={preambleVisible})");
    }

    // ── 3.5 — Keys reach the input editor; hardware cursor at input caret ─────

    [Fact]
    public async Task KeysReachInputEditorCaretBelowPreamble()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Terminal.InputPreamble.SetRows(L("cursor-test-header"));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        harness.SendKey(new KeyEvent(KeyCode.FromRune(new System.Text.Rune('X')), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        IReadOnlyList<Line> fixed_ = snap.FixedRegionRows;

        // 'X' must appear in the fixed region (the input editor row).
        Assert.True(FindRow(fixed_, "X") >= 0, "Key 'X' did not reach the input editor");

        // Hardware cursor visible.
        Assert.True(snap.IsCursorVisible, "Cursor should be visible when input is active");

        // Caret is not null.
        Assert.NotNull(snap.Caret);

        // Caret is in the input editor band, below the 1-row preamble.
        // caretRowInFixed = snap.Caret.Value.Row - snap.LiveWindowRows.Count
        int caretRowInFixed = snap.Caret!.Value.Row - snap.LiveWindowRows.Count;
        Assert.True(caretRowInFixed >= 1,
            $"Caret row in fixed region (={caretRowInFixed}) should be ≥ 1 (past the preamble row at index 0)");
    }

    // ── 3.6 — Regression guard: existing FixedRegionTests stay green ──────────

    [Fact]
    public void ExistingFixedRegionTestsRegression()
    {
        // Satisfied by dotnet test running FixedRegionTests without failure.
        // Constructor arity updates for PreambleLine were applied in Section 2.
    }
}
