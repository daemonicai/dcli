using Dcli.Testing;
using Xunit;

namespace Dcli.Tests.Testing;

/// <summary>
/// Tests for §15.4: <see cref="FrameSnapshot"/> and <see cref="FrameSnapshotPrinter"/>.
/// </summary>
public sealed class FrameSnapshotTests
{
    // ── Pre-paint empty snapshot ──────────────────────────────────────────────

    [Fact]
    public async Task SnapshotBeforeFirstPaintReturnsEmptyAndDoesNotThrow()
    {
        // Access Snapshot before any SettleAsync — must return empty, not throw.
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        FrameSnapshot snap = harness.Snapshot;

        Assert.NotNull(snap);
        Assert.Equal((0, 0), snap.Size);
        Assert.Empty(snap.LiveWindowRows);
        Assert.Empty(snap.FixedRegionRows);
        Assert.Empty(snap.NewlyCommittedRows);
        Assert.Null(snap.Caret);
        Assert.Equal(OverlayKind.None, snap.Overlay.Kind);
    }

    // ── Snapshot reflects scrollback content ─────────────────────────────────

    [Fact]
    public async Task SnapshotReflectsScrollbackLine()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 10 });

        harness.Terminal.Scrollback.Append(new Line([new Segment("hello world")]));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;

        // The appended line should appear in LiveWindowRows or NewlyCommittedRows.
        // (When the scrollback fills the live window it is committed; either location is correct.)
        bool inLive = snap.LiveWindowRows.Any(r => r.Segments.Any(
            s => s.Text.Contains("hello world", StringComparison.Ordinal)));
        bool inCommitted = snap.NewlyCommittedRows.Any(r => r.Segments.Any(
            s => s.Text.Contains("hello world", StringComparison.Ordinal)));

        Assert.True(inLive || inCommitted,
            "Appended line must appear in LiveWindowRows or NewlyCommittedRows.");
    }

    // ── Snapshot reflects active overlay ─────────────────────────────────────

    [Fact]
    public async Task SnapshotReflectsAutocompleteOverlay()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 10 });

        // Show an autocomplete with two candidates.
        AutocompleteCandidate[] candidates =
        [
            new AutocompleteCandidate("/help", new Line([new Segment("/help")])),
            new AutocompleteCandidate("/quit", new Line([new Segment("/quit")])),
        ];
        harness.Terminal.Autocomplete.Show(candidates);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;

        Assert.Equal(OverlayKind.Autocomplete, snap.Overlay.Kind);
        Assert.Equal(0, snap.Overlay.SelectedIndex); // first item selected by default
        Assert.True(snap.Overlay.VisibleRowCount > 0);
    }

    // ── Snapshot is immutable ─────────────────────────────────────────────────

    [Fact]
    public async Task SnapshotIsImmutableAcrossSubsequentPaints()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 10 });

        // First paint: one line.
        harness.Terminal.Scrollback.Append(new Line([new Segment("line A")]));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        FrameSnapshot snap1 = harness.Snapshot;
        int rowCountAfterFirst = snap1.LiveWindowRows.Count + snap1.NewlyCommittedRows.Count;

        // Second paint: additional lines.
        harness.Terminal.Scrollback.Append(new Line([new Segment("line B")]));
        harness.Terminal.Scrollback.Append(new Line([new Segment("line C")]));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // The first snapshot must be unchanged.
        int rowCountAfterFirstNow = snap1.LiveWindowRows.Count + snap1.NewlyCommittedRows.Count;
        Assert.Equal(rowCountAfterFirst, rowCountAfterFirstNow);
    }

    // ── Pretty-printer golden frame ───────────────────────────────────────────

    [Fact]
    public async Task PrettyPrintProducesStableGoldenString()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 6 });

        // Produce a deterministic frame: one scrollback line + a status row.
        harness.Terminal.Scrollback.Append(new Line([new Segment("hello")]));
        harness.Terminal.Status.SetRows([new Line([new Segment("status: ok")])]);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        string pretty = FrameSnapshotPrinter.PrettyPrint(snap);

        // Structural assertions that are stable regardless of exact padding.
        Assert.Contains("hello", pretty, StringComparison.Ordinal);
        Assert.Contains("status: ok", pretty, StringComparison.Ordinal);
        Assert.Contains("--- horizon ---", pretty, StringComparison.Ordinal);
        Assert.Contains("Overlay: None", pretty, StringComparison.Ordinal);
        Assert.StartsWith("+", pretty.TrimStart('\n'), StringComparison.Ordinal);
        Assert.EndsWith("+", pretty.TrimEnd('\n'), StringComparison.Ordinal);
    }

    [Fact]
    public void PrettyPrintEmptySnapshotDoesNotThrow()
    {
        string pretty = FrameSnapshotPrinter.PrettyPrint(FrameSnapshot.Empty);

        Assert.NotNull(pretty);
        Assert.Contains("Overlay: None", pretty, StringComparison.Ordinal);
    }

    // ── §15.7-4 — Overlay state via snapshot ────────────────────────────────

    [Fact]
    public async Task SnapshotOverlayNoneWhenNoOverlayActive()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        Assert.Equal(OverlayKind.None, snap.Overlay.Kind);
    }
}
