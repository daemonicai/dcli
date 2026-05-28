using Dcli.Testing;
using Xunit;

namespace Dcli.Tests.Testing;

/// <summary>
/// Tests for §15 Chunk A: <see cref="HeadlessTerminal"/>, <see cref="VirtualClock"/>,
/// and the internal edge fakes.
/// </summary>
public sealed class HeadlessTerminalTests
{
    // ── §15.2 / §15.7-1 — StartAsync lifecycle ───────────────────────────────

    [Fact]
    public async Task StartAsyncReturnsUsableHarness()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        Assert.NotNull(harness);
        Assert.NotNull(harness.Terminal);
        Assert.IsAssignableFrom<ITerminal>(harness.Terminal);
        Assert.NotNull(harness.Clock);
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        // First dispose — must not throw.
        await harness.DisposeAsync();
        // Second dispose — must also not throw.
        await harness.DisposeAsync();
    }

    // ── §15.7-2 — Byte-feed → parsed event (Up arrow via ESC[A) ─────────────

    [Fact]
    public async Task FeedEscapeSequenceProducesExpectedKeyEvent()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        // ESC O P = F1 (SS3 encoding used by xterm/macOS).
        // F1 is not consumed by the editor so it reaches the outbound channel as KeyPressed.
        harness.Feed([0x1B, (byte)'O', (byte)'P']);

        // First settle lets the InputReader thread drain the byte queue.
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        // Second settle ensures any KeyEvent posted by the InputReader is processed.
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        List<TerminalEvent> events = await DrainEventsAsync(harness.Terminal);
        KeyPressed? pressed = events.OfType<KeyPressed>().FirstOrDefault();

        Assert.NotNull(pressed);
        Assert.Equal(KeyCode.Named(NamedKey.F1), pressed.Key.Code);
    }

    // ── §15.3 / §15.7-3 — SettleAsync → exactly one coalesced frame ─────────

    [Fact]
    public async Task SettleAsyncMultipleAppendsProducesExactlyOneFrame()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        // Post ≥3 scrollback lines before settling.
        harness.Terminal.Scrollback.Append(new Line([new Segment("line 1")]));
        harness.Terminal.Scrollback.Append(new Line([new Segment("line 2")]));
        harness.Terminal.Scrollback.Append(new Line([new Segment("line 3")]));

        int paintsBefore = harness.Sink.PaintCount;
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        int paintsAfter = harness.Sink.PaintCount;

        // Exactly one coalesced frame was produced for the batch.
        Assert.Equal(1, paintsAfter - paintsBefore);
    }

    // ── §15.3 / §15.7-4 — Virtual clock advance triggers a throttled paint ───

    [Fact]
    public async Task VirtualClockAdvanceTriggersPaintAfterThrottle()
    {
        // Use a 100 ms minimum frame interval so paints are throttled.
        HeadlessTerminalOptions options = new()
        {
            MinFrameInterval = TimeSpan.FromMilliseconds(100),
        };
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(options);

        // Frame 1: advance clock past initial deadline (starts at zero) and settle.
        harness.Terminal.Scrollback.Append(new Line([new Segment("first")]));
        harness.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        int paintAfterFirst = harness.Sink.PaintCount;
        Assert.True(paintAfterFirst >= 1, "At least one paint expected after first settle.");

        // Frame 2: post dirty content, then advance past the next deadline (now+100ms).
        // The settle will only complete once the clock fires and the loop paints.
        harness.Terminal.Scrollback.Append(new Line([new Segment("second")]));
        harness.Clock.Advance(TimeSpan.FromMilliseconds(200));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        int paintAfterSecond = harness.Sink.PaintCount;

        Assert.True(paintAfterSecond > paintAfterFirst,
            "A second paint must fire after the clock advances past the frame interval.");
    }

    // ── §15.2 / §15.7-5 — Resize delivery ───────────────────────────────────

    [Fact]
    public async Task ResizeDeliversResizedEventAndUpdatesModel()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 80, InitialRows = 24 });

        harness.Resize(60, 18);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        List<TerminalEvent> events = await DrainEventsAsync(harness.Terminal);
        Resized? resized = events.OfType<Resized>().FirstOrDefault();

        Assert.NotNull(resized);
        Assert.Equal(60, resized.Columns);
        Assert.Equal(18, resized.Rows);

        (int cols, int rows) = harness.Terminal.GetTerminalSize();
        Assert.Equal(60, cols);
        Assert.Equal(18, rows);
    }

    // ── §15.2 / §15.7-6 — SendKey / Type / Paste basics ─────────────────────

    [Fact]
    public async Task SendKeyProducesKeyPressedEvent()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        List<TerminalEvent> events = await DrainEventsAsync(harness.Terminal);
        Assert.Contains(events, e => e is KeyPressed { Key.Code.NamedValue: NamedKey.Escape });
    }

    [Fact]
    public async Task TypeSingleCharacterProducesInputChangedEvent()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        harness.Type("x");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        List<TerminalEvent> events = await DrainEventsAsync(harness.Terminal);
        InputChanged? changed = events.OfType<InputChanged>().FirstOrDefault();
        Assert.NotNull(changed);
        Assert.Equal("x", changed.Text);
    }

    [Fact]
    public async Task PastePostsToLoopWithoutCrashing()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync();

        // PasteEvent routing to the editor is not yet implemented (deferred to a future chunk).
        // Verify the harness can post a paste and settle without crashing or deadlocking, and
        // that the loop emits a paint (the model is marked dirty on every input event).
        int paintsBefore = harness.Sink.PaintCount;
        harness.Paste("hello");
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(harness.Sink.PaintCount > paintsBefore,
            "Loop must have painted at least once after processing the PasteEvent.");
    }

    // ── §15.7-7 — HeadlessRawModeSession is a no-op / independent harnesses ──

    [Fact]
    public async Task TwoHarnessesInSameProcessDoNotConflict()
    {
        // Two harnesses must be independent: each has its own model, session, and loop.
        await using HeadlessTerminal first = await HeadlessTerminal.StartAsync();
        await using HeadlessTerminal second = await HeadlessTerminal.StartAsync();

        first.Type("a");
        second.Type("b");

        await first.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await second.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(first.Sink.LastModel);
        Assert.NotNull(second.Sink.LastModel);
        Assert.Equal("a", first.Sink.LastModel.FixedRegion.Editor.Text);
        Assert.Equal("b", second.Sink.LastModel.FixedRegion.Editor.Text);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<List<TerminalEvent>> DrainEventsAsync(
        ITerminal terminal,
        int minCount = 1)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        List<TerminalEvent> events = [];

        while (events.Count < minCount)
        {
            if (terminal.Events.TryRead(out TerminalEvent? ev))
                events.Add(ev);
            else
                await terminal.Events.WaitToReadAsync(cts.Token);
        }

        while (terminal.Events.TryRead(out TerminalEvent? extra))
            events.Add(extra);

        return events;
    }
}
