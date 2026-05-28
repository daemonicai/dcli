using Dcli.Testing;
using Xunit;

namespace Dcli.Tests.Testing;

/// <summary>
/// End-to-end golden-frame tests (§15.6 retarget): drive commands through the harness,
/// settle, and assert on <see cref="FrameSnapshotPrinter.PrettyPrint"/> output.
/// These tests demonstrate the pattern that consumers should use for visual regression testing.
/// </summary>
/// <remarks>
/// The assertions use structural predicates (contains, starts-with) rather than full-string
/// equality so the golden strings survive minor rendering changes (e.g. padding adjustments).
/// When you need strict equality, lock the full <see cref="FrameSnapshotPrinter.PrettyPrint"/>
/// output in a constant and compare with <c>Assert.Equal</c>.
/// </remarks>
public sealed class GoldenFrameTests
{
    // ── §15.6 — Scrollback + status golden frame ─────────────────────────────

    [Fact]
    public async Task ScrollbackAndStatusRowProduceExpectedGoldenFrame()
    {
        // This test retargets the §8.5 representative scenario:
        // "scrollback append + fixed-region status row" — end-to-end through the harness.
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 6 });

        harness.Terminal.Scrollback.Append(new Line([new Segment("output line")]));
        harness.Terminal.Status.SetRows([new Line([new Segment("ready")])]);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        string frame = FrameSnapshotPrinter.PrettyPrint(snap);

        // Box structure
        Assert.Contains("+", frame, StringComparison.Ordinal);
        Assert.Contains("|", frame, StringComparison.Ordinal);
        Assert.Contains("--- horizon ---", frame, StringComparison.Ordinal);

        // Content
        Assert.Contains("output line", frame, StringComparison.Ordinal);
        Assert.Contains("ready", frame, StringComparison.Ordinal);

        // No active overlay
        Assert.Contains("Overlay: None", frame, StringComparison.Ordinal);

        // Size was recorded
        Assert.Equal((40, 6), snap.Size);
    }

    // ── §15.6 — Input typed into editor appears in fixed region ──────────────

    [Fact]
    public async Task TypedInputAppearsInFixedRegionRows()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 6 });

        harness.Type("hello");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        string frame = FrameSnapshotPrinter.PrettyPrint(snap);

        // Typed text appears somewhere in the fixed region
        bool inFixed = snap.FixedRegionRows.Any(r => r.Segments.Any(s => s.Text.Contains("hello", StringComparison.Ordinal)));
        Assert.True(inFixed, "Typed text must appear in FixedRegionRows.");
        Assert.Contains("hello", frame, StringComparison.Ordinal);
    }
}
