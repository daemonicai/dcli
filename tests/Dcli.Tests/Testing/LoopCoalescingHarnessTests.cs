using Dcli.Testing;
using Xunit;

namespace Dcli.Tests.Testing;

/// <summary>
/// §15.6 retarget: loop-coalescing tests (§7.6) run on the headless harness.
/// These complement <c>RenderLoopTests</c> which drives <c>LoopEngine</c> directly.
/// The harness-based versions verify the same behaviour through the public API
/// so consumer code can rely on identical semantics.
/// </summary>
/// <remarks>
/// The existing direct-<c>LoopEngine</c> tests in <c>RenderLoopTests</c> are kept as-is:
/// some assert on internal state (<c>WaitCount</c>, <c>OutboundEvents.Count</c>,
/// <c>DirtyCommand</c>) that the harness does not expose. Those remain the
/// unit-level substrate; these tests are the integration-level substrate.
/// </remarks>
public sealed class LoopCoalescingHarnessTests
{
    // ── §7.6 — Burst coalescing ───────────────────────────────────────────────

    [Fact]
    public async Task BurstNScrollbackAppendsProducesExactlyOneFrame()
    {
        // Mirrors RenderLoopTests.BurstCoalescingNCommandsProducesExactlyOneFrame,
        // but driven via the public Scrollback.Append API through the harness.
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        int paintsBefore = harness.Sink.PaintCount;

        // Post 10 appends before settling.
        for (int i = 0; i < 10; i++)
        {
            harness.Terminal.Scrollback.Append(new Line([new Segment($"line {i}")]));
        }

        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, harness.Sink.PaintCount - paintsBefore);
    }

    [Fact]
    public async Task BurstNTypeEventsProducesExactlyOneFrame()
    {
        // Mirrors RenderLoopTests.BurstCoalescingNInputEventsProducesExactlyOneFrame,
        // but driven via harness.Type() which posts KeyEvents directly to the loop.
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        int paintsBefore = harness.Sink.PaintCount;

        // Type 5 characters before settling.
        for (int i = 0; i < 5; i++)
        {
            harness.Type("x");
        }

        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, harness.Sink.PaintCount - paintsBefore);
    }

    // ── §7.6 — Throttle with virtual clock ───────────────────────────────────

    [Fact]
    public async Task ThrottleSecondSettleBeforeIntervalElapseDoesNotPaint()
    {
        // Mirrors RenderLoopTests.ThrottleSecondSettleBeforeIntervalElapseDoesNotPaint.
        HeadlessTerminalOptions options = new()
        {
            MinFrameInterval = TimeSpan.FromMilliseconds(100),
        };
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(options);

        // Frame 1: model is dirty from startup; first settle paints.
        harness.Terminal.Scrollback.Append(new Line([new Segment("first")]));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        int afterFirst = harness.Sink.PaintCount;

        // Frame 2 attempt: dirty again, but clock hasn't advanced — throttled.
        harness.Terminal.Scrollback.Append(new Line([new Segment("second")]));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        int afterSecond = harness.Sink.PaintCount;

        Assert.Equal(afterFirst, afterSecond); // no extra paint — throttled
    }

    [Fact]
    public async Task ThrottleAdvancingVirtualClockAllowsNextFrame()
    {
        // Mirrors RenderLoopTests.ThrottleAdvancingClockAllowsNextFrame.
        HeadlessTerminalOptions options = new()
        {
            MinFrameInterval = TimeSpan.FromMilliseconds(100),
        };
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(options);

        // Frame 1.
        harness.Terminal.Scrollback.Append(new Line([new Segment("first")]));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        int afterFirst = harness.Sink.PaintCount;

        // Advance clock past the interval, then post dirty and settle.
        harness.Clock.Advance(TimeSpan.FromMilliseconds(200));
        harness.Terminal.Scrollback.Append(new Line([new Segment("second")]));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        int afterSecond = harness.Sink.PaintCount;

        Assert.True(afterSecond > afterFirst, "Second frame must fire after clock advances.");
    }
}
