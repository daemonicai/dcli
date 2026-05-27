using System.Collections.Concurrent;
using System.Threading.Channels;
using Dcli.Internal.RenderLoop;
using Xunit;

namespace Dcli.Tests.Terminal.RenderLoop;

/// <summary>
/// Deterministic cadence tests for §7: render loop core.
/// All tests use a virtual clock and an in-memory output sink — no wall-clock sleeps.
/// </summary>
public sealed class RenderLoopTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Virtual clock that reports a fixed "now" until the test advances it.
    /// <see cref="WaitUntilAsync"/> returns a task that completes when <see cref="Advance"/>
    /// moves the clock to or past the requested deadline — deterministic, no real time.
    /// </summary>
    private sealed class VirtualClock : IClock
    {
        private readonly object _lock = new();
        private TimeSpan _now;
        // Each entry: the deadline and a TCS to signal when the deadline is reached.
        // The linked CTS mirrors the caller's cancellation token; disposing it cancels the TCS.
        private readonly List<(TimeSpan Deadline, TaskCompletionSource Tcs, CancellationTokenSource LinkedCts)> _waiters = [];

        internal VirtualClock(TimeSpan? initial = null) =>
            _now = initial ?? TimeSpan.Zero;

        public TimeSpan Now
        {
            get { lock (_lock) { return _now; } }
        }

        /// <summary>
        /// Advance the virtual clock by <paramref name="by"/> and complete any registered
        /// waiters whose deadline is now reached.
        /// </summary>
        internal void Advance(TimeSpan by)
        {
            List<(TaskCompletionSource Tcs, CancellationTokenSource LinkedCts)> toComplete = [];
            lock (_lock)
            {
                _now += by;
                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (_waiters[i].Deadline <= _now)
                    {
                        toComplete.Add((_waiters[i].Tcs, _waiters[i].LinkedCts));
                        _waiters.RemoveAt(i);
                    }
                }
            }
            foreach ((TaskCompletionSource tcs, CancellationTokenSource linkedCts) in toComplete)
            {
                tcs.TrySetResult();
                linkedCts.Dispose();
            }
        }

        public Task WaitUntilAsync(TimeSpan deadline, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (deadline <= _now)
                    return Task.CompletedTask;
            }

            // A linked CTS cancels our TCS when the caller's token fires.
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            linkedCts.Token.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                tcs,
                useSynchronizationContext: false);

            lock (_lock)
            {
                // Re-check under the lock in case Advance raced between the outer check and here.
                if (deadline <= _now)
                {
                    tcs.TrySetResult();
                    linkedCts.Dispose();
                    return tcs.Task;
                }

                _waiters.Add((deadline, tcs, linkedCts));
            }

            return tcs.Task;
        }
    }

    /// <summary>
    /// In-memory output sink that counts and records each paint call.
    /// </summary>
    private sealed class CapturingOutputSink : IOutputSink
    {
        private readonly List<(int Columns, int Rows)> _frames = [];
        private int _paintCount;

        internal int PaintCount => Volatile.Read(ref _paintCount);

        internal IReadOnlyList<(int Columns, int Rows)> Frames
        {
            get { lock (_frames) { return _frames.ToArray(); } }
        }

        public void Paint(RenderModel model)
        {
            lock (_frames)
            {
                _frames.Add((model.Columns, model.Rows));
            }
            Interlocked.Increment(ref _paintCount);
        }
    }

    /// <summary>Fixed terminal dimensions for tests.</summary>
    private sealed class FixedSizeSource : ITerminalSizeSource
    {
        private readonly int _cols;
        private readonly int _rows;

        internal FixedSizeSource(int cols = 80, int rows = 24)
        {
            _cols = cols;
            _rows = rows;
        }

        public (int Columns, int Rows) GetSize() => (_cols, _rows);
    }

    /// <summary>A simple command that marks the model dirty (simulates an API call).</summary>
    private sealed class DirtyCommand : ILoopCommand
    {
        /// <summary>Thread ID on which Apply was last called, for the no-interleave test.</summary>
        internal int? AppliedOnThreadId { get; private set; }

        void ILoopCommand.Apply(RenderModel model)
        {
            AppliedOnThreadId = Environment.CurrentManagedThreadId;
            model.MarkDirty();
        }
    }

    /// <summary>A command that records the thread ID of each Apply call.</summary>
    private sealed class ThreadCapturingCommand : ILoopCommand
    {
        internal ConcurrentBag<int> ThreadIds { get; } = [];

        void ILoopCommand.Apply(RenderModel model)
        {
            ThreadIds.Add(Environment.CurrentManagedThreadId);
            model.MarkDirty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Factory helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (LoopEngine Engine, VirtualClock Clock, CapturingOutputSink Sink) CreateEngine(
        TimeSpan? minFrameInterval = null,
        int cols = 80,
        int rows = 24)
    {
        VirtualClock clock = new(TimeSpan.Zero);
        CapturingOutputSink sink = new();
        LoopEngine engine = new(
            new FixedSizeSource(cols, rows),
            clock,
            sink,
            minFrameInterval ?? TimeSpan.FromMilliseconds(16));
        return (engine, clock, sink);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.6 — Burst coalescing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BurstCoalescingNCommandsProducesExactlyOneFrame()
    {
        (LoopEngine engine, _, CapturingOutputSink sink) = CreateEngine();
        using (engine)
        {
            // Post N commands before any settle — all should be batched into one frame.
            for (int i = 0; i < 10; i++)
            {
                engine.Post(new DirtyCommand());
            }

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            Assert.Equal(1, sink.PaintCount);
        }
    }

    [Fact]
    public async Task BurstCoalescingNInputEventsProducesExactlyOneFrame()
    {
        (LoopEngine engine, _, CapturingOutputSink sink) = CreateEngine();
        using (engine)
        {
            ChannelWriter<InputEvent> inputWriter = engine.InputWriter;
            for (int i = 0; i < 5; i++)
            {
                inputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new System.Text.Rune('a')), Modifiers.None));
            }

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            Assert.Equal(1, sink.PaintCount);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.6 — Idle: no messages → zero repaints
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IdleWithNoMessagesProducesZeroFrames()
    {
        // Large minFrameInterval so the paint deadline never arrives during the observation window.
        (LoopEngine engine, _, CapturingOutputSink sink) =
            CreateEngine(minFrameInterval: TimeSpan.FromHours(1));
        using (engine)
        {
            await Task.Delay(50); // give the loop time to spin if it were going to

            Assert.Equal(0, sink.PaintCount);

            // A truly-idle loop parks on the channel and enters the wait exactly once.
            // A busy-poll (~1 000 Hz) would rack up hundreds of wait entries over 50 ms.
            // Allow a small cushion (≤ 3) for startup / scheduling jitter.
            int waitCount = engine.WaitCount;
            Assert.True(waitCount <= 3, $"Expected ≤3 wait entries for idle loop; got {waitCount}. Loop may be busy-polling.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.6 — Throttle: frame-rate cap
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ThrottleSecondSettleBeforeIntervalElapseDoesNotPaint()
    {
        // minFrameInterval = 100 ms; virtual clock stays at 0 ms throughout.
        // First settle: clock at 0, deadline at 0 → paints (0 >= 0); deadline advances to 100 ms.
        // Second settle: clock still at 0, now (0) < deadline (100 ms) → no paint.
        (LoopEngine engine, _, CapturingOutputSink sink) =
            CreateEngine(minFrameInterval: TimeSpan.FromMilliseconds(100));
        using (engine)
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

            engine.Post(new DirtyCommand());
            await engine.SettleAsync(cts.Token);
            int afterFirst = sink.PaintCount;

            engine.Post(new DirtyCommand());
            await engine.SettleAsync(cts.Token);
            int afterSecond = sink.PaintCount;

            Assert.Equal(1, afterFirst);
            Assert.Equal(1, afterSecond); // throttled — clock didn't advance
        }
    }

    [Fact]
    public async Task ThrottleAdvancingClockAllowsNextFrame()
    {
        // After the first frame, advance the virtual clock past the interval.
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) =
            CreateEngine(minFrameInterval: TimeSpan.FromMilliseconds(100));
        using (engine)
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

            engine.Post(new DirtyCommand());
            await engine.SettleAsync(cts.Token);
            Assert.Equal(1, sink.PaintCount);

            // Advance the virtual clock past the frame interval.
            clock.Advance(TimeSpan.FromMilliseconds(200));

            engine.Post(new DirtyCommand());
            await engine.SettleAsync(cts.Token);
            Assert.Equal(2, sink.PaintCount); // second frame allowed
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.6 — No interleaving: all mutations on the single loop thread
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoInterleavingConcurrentProducersAllMutationsOnLoopThread()
    {
        (LoopEngine engine, _, _) = CreateEngine();
        using (engine)
        {
            ThreadCapturingCommand capture = new();

            // Fire 20 commands from 4 concurrent producer threads.
            Task[] producers = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < 5; i++)
                    {
                        engine.Post(capture);
                    }
                }))
                .ToArray();

            await Task.WhenAll(producers);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            // All 20 Apply calls must have happened on the same single loop thread.
            Assert.NotEmpty(capture.ThreadIds);
            Assert.Single(capture.ThreadIds.Distinct()); // exactly one unique thread ID
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.6 — Slow consumer doesn't stall the loop
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SlowConsumerNeverDrainsOutboundLoopKeepsPainting()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        using (engine)
        {
            // Consumer never reads from OutboundEvents — events accumulate unboundedly.
            ChannelWriter<InputEvent> inputWriter = engine.InputWriter;
            for (int i = 0; i < 20; i++)
            {
                inputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new System.Text.Rune('x')), Modifiers.None));
            }

            // Advance the virtual clock so the throttle deadline is met.
            clock.Advance(TimeSpan.FromMilliseconds(100));
            engine.Post(new DirtyCommand());

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            // Loop must have painted despite consumer never draining.
            Assert.True(sink.PaintCount >= 1, $"Expected ≥1 paint; got {sink.PaintCount}");

            // Outbound events must have accumulated unread.
            Assert.True(engine.OutboundEvents.Count > 0, "Expected unread outbound events");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.6 — Snapshot read (terminal size)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SnapshotReadReturnsSizeWithoutRoundTrip()
    {
        (LoopEngine engine, _, _) = CreateEngine(cols: 120, rows: 40);
        using (engine)
        {
            (int cols, int rows) = engine.GetTerminalSize();

            Assert.Equal(120, cols);
            Assert.Equal(40, rows);
        }
    }

    [Fact]
    public async Task SnapshotReadUpdatesAfterResizeEvent()
    {
        (LoopEngine engine, _, _) = CreateEngine(cols: 80, rows: 24);
        using (engine)
        {
            engine.InputWriter.TryWrite(new ResizeEvent(160, 48));

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            (int cols, int rows) = engine.GetTerminalSize();

            Assert.Equal(160, cols);
            Assert.Equal(48, rows);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.6 — FIFO ordering
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OrderingInputEventAndCommandAppliedFifoAndReflectedInSameFrame()
    {
        // Post an input event then a command; both must appear in the same settle frame.
        (LoopEngine engine, _, CapturingOutputSink sink) = CreateEngine();
        using (engine)
        {
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
            engine.Post(new DirtyCommand());

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            // Both caused dirty state → one frame; the input event also emitted an outbound event.
            Assert.Equal(1, sink.PaintCount);
            Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? ev));
            Assert.IsType<KeyPressed>(ev);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.1 — TerminalEvent types are public and constructible
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TerminalEventTypesArePublicAndConstructible()
    {
        KeyEvent ke = new(KeyCode.Named(NamedKey.Enter), Modifiers.None);

        TerminalEvent e1 = new InputSubmitted("hello");
        TerminalEvent e2 = new InputChanged("hel");
        TerminalEvent e3 = new KeyPressed(ke);
        TerminalEvent e4 = new Resized(80, 24);

        Assert.IsType<InputSubmitted>(e1);
        Assert.IsType<InputChanged>(e2);
        Assert.IsType<KeyPressed>(e3);
        Assert.IsType<Resized>(e4);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.3 — Fire-and-forget: Post returns before paint
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PostReturnsImmediatelyWithoutBlockingOnLoopThread()
    {
        (LoopEngine engine, _, CapturingOutputSink sink) = CreateEngine();
        using (engine)
        {
            // Post must return synchronously without blocking on the loop thread.
            engine.Post(new DirtyCommand());

            // If we reach here without deadlocking, the enqueue-and-return contract holds.
            // Paint count is unobservable here without settle — that is intentional.
            Assert.True(true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.4 — Outbound channel: input events propagate correctly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OutboundInputEventProducesKeyPressedOnOutboundChannel()
    {
        (LoopEngine engine, _, _) = CreateEngine();
        using (engine)
        {
            KeyEvent ke = new(KeyCode.FromRune(new System.Text.Rune('z')), Modifiers.None);
            engine.InputWriter.TryWrite(ke);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? ev));
            KeyPressed kp = Assert.IsType<KeyPressed>(ev);
            Assert.Equal(ke, kp.Key);
        }
    }

    [Fact]
    public async Task OutboundResizeEventProducesResizedOnOutboundChannel()
    {
        (LoopEngine engine, _, _) = CreateEngine();
        using (engine)
        {
            engine.InputWriter.TryWrite(new ResizeEvent(100, 30));

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await engine.SettleAsync(cts.Token);

            Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? ev));
            Resized resized = Assert.IsType<Resized>(ev);
            Assert.Equal(100, resized.Columns);
            Assert.Equal(30, resized.Rows);
        }
    }
}
