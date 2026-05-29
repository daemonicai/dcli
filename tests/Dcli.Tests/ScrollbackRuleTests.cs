using Dcli.Testing;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for §2 — <see cref="IScrollback.AppendRule"/>:
/// width-aware rule rendering and resize re-expansion.
/// </summary>
public sealed class ScrollbackRuleTests
{
    // ── §2.5 — AppendRule renders a horizontal separator spanning the content width ───

    /// <summary>
    /// After AppendRule(), the live window contains a single row whose text is exactly
    /// the content-width count of U+2500 box-drawing light-horizontal characters.
    /// </summary>
    [Fact]
    public async Task AppendRuleRendersHorizontalSeparatorAtContentWidth()
    {
        const int width = 20;
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = width, InitialRows = 24 });

        harness.Terminal.Scrollback.AppendRule();
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snapshot = harness.Snapshot;
        string expected = new string('─', width);
        bool found = snapshot.LiveWindowRows.Any(row =>
            row.Segments.Count == 1 &&
            row.Segments[0].Text == expected);
        Assert.True(found,
            $"Expected a rule row of {width} '─' chars; live rows: [{string.Join(", ", snapshot.LiveWindowRows.Select(r => $"'{string.Concat(r.Segments.Select(s => s.Text))}'"))}]");
    }

    // ── §2.6 — Rule re-expands to new content width after resize ─────────────

    /// <summary>
    /// After a terminal resize, the rule row width tracks the new content width, not the old one.
    /// </summary>
    [Fact]
    public async Task AppendRuleReExpandsToNewWidthAfterResize()
    {
        const int initialWidth = 20;
        const int resizedWidth = 40;

        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = initialWidth, InitialRows = 24 });

        harness.Terminal.Scrollback.AppendRule();
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // Verify initial render at initialWidth.
        string expectedInitial = new string('─', initialWidth);
        bool foundInitial = harness.Snapshot.LiveWindowRows.Any(row =>
            row.Segments.Count == 1 &&
            row.Segments[0].Text == expectedInitial);
        Assert.True(foundInitial,
            $"Expected initial rule row of {initialWidth} '─' chars.");

        // Resize to resizedWidth — harness.Resize keeps both sizeSource and resizeWatcher
        // in sync, so the model column count and the live-window re-render both update correctly.
        harness.Resize(resizedWidth, 24);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snapshot = harness.Snapshot;
        Assert.Equal(resizedWidth, snapshot.Size.Columns);

        string expectedResized = new string('─', resizedWidth);
        bool foundResized = snapshot.LiveWindowRows.Any(row =>
            row.Segments.Count == 1 &&
            row.Segments[0].Text == expectedResized);
        Assert.True(foundResized,
            $"Expected resized rule row of {resizedWidth} '─' chars; live rows: [{string.Join(", ", snapshot.LiveWindowRows.Select(r => $"'{string.Concat(r.Segments.Select(s => s.Text))}'"))}]");
    }
}
