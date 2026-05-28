using Dcli.Internal;
using Dcli.Internal.Input;
using Dcli.Internal.RenderLoop;
using Dcli.Internal.Scrollback;
using Xunit;
using DcliTerminal = Dcli.Terminal;

namespace Dcli.Tests.RenderLoop;

/// <summary>
/// Behavioural tests for §13.2: terminal resize reflows the live window and fixed region.
/// All tests use injected edges — no real tty, no real raw mode.
/// </summary>
public sealed class ResizeReflowTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A controllable <see cref="IResizeWatcher"/> for tests. Stores the callback registered
    /// via <see cref="Start"/> and exposes <see cref="Fire"/> to simulate a resize event.
    /// <see cref="Dispose"/> is idempotent and does not null the callback, so the watcher
    /// remains usable after the Terminal takes ownership and disposes it.
    /// </summary>
    private sealed class FakeResizeWatcher : IResizeWatcher
    {
        private Action<int, int>? _callback;

        public void Start(Action<int, int> onResize) => _callback = onResize;

        /// <summary>Idempotent no-op; does not clear the callback so <see cref="Fire"/> still works.</summary>
        public void Dispose() { }

        /// <summary>Simulates a terminal resize by invoking the registered callback.</summary>
        internal void Fire(int columns, int rows) =>
            _callback?.Invoke(columns, rows);
    }

    /// <summary>Captures the most-recent painted <see cref="RenderModel"/> for assertion.</summary>
    private sealed class CapturingOutputSink : IOutputSink
    {
        private volatile RenderModel? _lastModel;

        internal RenderModel? LastModel => _lastModel;

        public void Paint(RenderModel model) => _lastModel = model;

        public void EmitRestoreSequence() { }
    }

    /// <summary>Byte source that immediately times out — no data.</summary>
    private sealed class ImmediateTimeoutByteSource : IInputByteSource
    {
        public int Read(Span<byte> buffer) => 0;
    }

    /// <summary>Fixed terminal dimensions.</summary>
    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }

    /// <summary>A <see cref="ILoopCommand"/> that appends a line to the scrollback model.</summary>
    private sealed class AppendLineCmd(Line line) : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model) =>
            model.Scrollback.Append(new TextBlock(line), model);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Factory helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a headless terminal with the given dimensions and the caller-supplied watcher.
    /// Ownership of the watcher is transferred to the returned Terminal.
    /// </summary>
    private static (DcliTerminal Term, CapturingOutputSink Sink)
        CreateTerminal(int cols, int rows, FakeResizeWatcher watcher)
    {
        using RecordingRawModeSession session = new();
        RestoreCoordinator coordinator = RestoreCoordinator.Wire(session);
        CapturingOutputSink sink = new();

        // VirtualClock at TimeSpan.Zero + minFrameInterval=Zero means every dirty state paints
        // immediately — no real-time dependency, fully deterministic.
        DcliTerminal term = DcliTerminal.StartCore(
            session,
            coordinator,
            watcher,
            new ImmediateTimeoutByteSource(),
            new VirtualClock(TimeSpan.Zero),
            sink,
            new FixedSizeSource(cols, rows),
            minFrameInterval: TimeSpan.Zero);

        // session and coordinator transferred to term; DisposeAsync disposes them.
        // The `using` also calls Dispose() on scope exit, but RecordingRawModeSession.Dispose is idempotent.
        return (term, sink);
    }

    private static Task SettleAsync(DcliTerminal term) =>
        term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

    /// <summary>
    /// Minimal virtual clock: always reports TimeSpan.Zero, WaitUntilAsync returns immediately.
    /// With minFrameInterval=Zero, the loop paints on every dirty-state settle.
    /// </summary>
    private sealed class VirtualClock(TimeSpan initial) : IClock
    {
        public TimeSpan Now => initial;
        public Task WaitUntilAsync(TimeSpan deadline, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static Line PlainLine(string text) => new([new Segment(text)]);

    // ─────────────────────────────────────────────────────────────────────────
    // 13.2-a — Live window reflows on width change
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A line that wraps into multiple rows at width 20 should reflow to fewer rows at width 40.
    /// </summary>
    [Fact]
    public async Task LiveWindowReflowsOnWidthChange()
    {
        // "01234567890123456789ABCDEFGHIJ" = 30 chars.
        // At width 20: wraps to 2 rows. At width 40: fits in 1 row.
        const string text = "01234567890123456789ABCDEFGHIJ";

        using FakeResizeWatcher watcher = new();
        (DcliTerminal term, CapturingOutputSink sink) = CreateTerminal(cols: 20, rows: 24, watcher);
        await using (term)
        {
            term.Loop.Post(new AppendLineCmd(PlainLine(text)));
            await SettleAsync(term);

            RenderModel? modelAt20 = sink.LastModel;
            Assert.NotNull(modelAt20);
            int rowsAt20 = modelAt20.LiveWindowRows.Count;
            Assert.True(rowsAt20 >= 2, $"Expected ≥2 live rows at width 20; got {rowsAt20}.");

            // Resize to width 40 — the same text should now fit in 1 row.
            watcher.Fire(40, 24);
            await SettleAsync(term);

            RenderModel? modelAt40 = sink.LastModel;
            Assert.NotNull(modelAt40);
            Assert.Equal(40, modelAt40.Columns);
            int rowsAt40 = modelAt40.LiveWindowRows.Count;
            Assert.True(rowsAt40 < rowsAt20,
                $"Expected fewer live rows at width 40 (got {rowsAt40}) than at width 20 ({rowsAt20}).");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13.2-b — Fixed-region cap recomputes on row change
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shrinking the terminal row count causes the fixed-region cap to recompute.
    /// The paint frame after the resize must use the new row budget.
    /// </summary>
    [Fact]
    public async Task FixedRegionCapRecomputesOnRowChange()
    {
        using FakeResizeWatcher watcher = new();
        (DcliTerminal term, CapturingOutputSink sink) = CreateTerminal(cols: 80, rows: 24, watcher);
        await using (term)
        {
            // Force a paint at rows=24.
            term.Loop.Post(new AppendLineCmd(PlainLine("initial")));
            await SettleAsync(term);

            RenderModel? modelAt24 = sink.LastModel;
            Assert.NotNull(modelAt24);
            Assert.Equal(24, modelAt24.Rows);

            // Resize to rows=10 — the frame must reflect the new row count.
            watcher.Fire(80, 10);
            await SettleAsync(term);

            RenderModel? modelAt10 = sink.LastModel;
            Assert.NotNull(modelAt10);
            Assert.Equal(10, modelAt10.Rows);

            // The fixed region total rows must be ≤ the row budget of 10.
            int fixedRows = modelAt10.FixedRegionRows.Count;
            Assert.True(fixedRows <= 10,
                $"Fixed region rows {fixedRows} exceed the row budget of 10.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13.2-c — Committed rows are NOT re-emitted on resize
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rows already committed (above the horizon) must not appear in NewlyCommittedRows
    /// after a resize. ClearCommitted semantics guarantee this; this test pins it.
    /// </summary>
    [Fact]
    public async Task CommittedRowsAreNotReEmittedOnResize()
    {
        // rows=5 so the live window is very small; appending many lines commits some.
        using FakeResizeWatcher watcher = new();
        (DcliTerminal term, CapturingOutputSink sink) = CreateTerminal(cols: 80, rows: 5, watcher);
        await using (term)
        {
            // Append enough lines to overflow the live window → some commit above the horizon.
            for (int i = 0; i < 10; i++)
                term.Loop.Post(new AppendLineCmd(PlainLine($"line {i}")));

            await SettleAsync(term);

            RenderModel? postAppend = sink.LastModel;
            Assert.NotNull(postAppend);

            // After the paint, ClearCommitted ran → NewlyCommittedRows is empty.
            Assert.Empty(postAppend.NewlyCommittedRows);

            // Fire a resize at the same size — no new overflows should occur.
            watcher.Fire(80, 5);
            await SettleAsync(term);

            RenderModel? postResize = sink.LastModel;
            Assert.NotNull(postResize);

            // The post-resize frame must not re-emit already-committed rows.
            Assert.Empty(postResize.NewlyCommittedRows);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13.2-d — Resized outbound event is emitted
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Firing the watcher must produce exactly one <see cref="Resized"/> event on the
    /// outbound channel with the new dimensions.
    /// </summary>
    [Fact]
    public async Task ResizedOutboundEventIsEmittedOnResize()
    {
        using FakeResizeWatcher watcher = new();
        (DcliTerminal term, _) = CreateTerminal(cols: 80, rows: 24, watcher);
        await using (term)
        {
            watcher.Fire(120, 40);
            await SettleAsync(term);

            // Drain the outbound channel.
            List<TerminalEvent> events = [];
            while (term.Events.TryRead(out TerminalEvent? ev))
                events.Add(ev);

            Resized? resized = events.OfType<Resized>().FirstOrDefault();
            Assert.NotNull(resized);
            Assert.Equal(120, resized.Columns);
            Assert.Equal(40, resized.Rows);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13.2-e — Volatile snapshot updates after resize
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After a resize settles, <see cref="DcliTerminal.GetTerminalSize()"/> must return
    /// the new dimensions.
    /// </summary>
    [Fact]
    public async Task VolatileSnapshotUpdatesAfterResize()
    {
        using FakeResizeWatcher watcher = new();
        (DcliTerminal term, _) = CreateTerminal(cols: 80, rows: 24, watcher);
        await using (term)
        {
            (int cols0, int rows0) = term.GetTerminalSize();
            Assert.Equal(80, cols0);
            Assert.Equal(24, rows0);

            watcher.Fire(160, 48);
            await SettleAsync(term);

            (int cols1, int rows1) = term.GetTerminalSize();
            Assert.Equal(160, cols1);
            Assert.Equal(48, rows1);
        }
    }
}
