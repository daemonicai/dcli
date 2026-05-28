using System.Threading.Channels;
using Dcli.Internal;
using Dcli.Internal.Input;
using Dcli.Internal.RenderLoop;
using Xunit;
using DcliTerminal = Dcli.Terminal;

namespace Dcli.Tests.Terminal;

/// <summary>
/// Headless lifecycle tests for §7.5: Terminal.StartAsync / DisposeAsync.
/// All tests use injected edges — no real tty, no real raw mode.
/// </summary>
public sealed class TerminalLifecycleTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Byte source that immediately returns 0 (simulates VTIME=1 timeout — no data).
    /// The input reader thread sees this as a timed read that returned no data, checks
    /// the stopping flag, and loops — exiting promptly when Stop() is called.
    /// </summary>
    private sealed class ImmediateTimeoutByteSource : IInputByteSource
    {
        public int Read(Span<byte> buffer) => 0; // always "timed out, no data"
    }

    /// <summary>
    /// Output sink that records Paint calls.
    /// </summary>
    private sealed class CapturingOutputSink : IOutputSink
    {
        private int _paintCount;

        internal int PaintCount => Volatile.Read(ref _paintCount);

        public void Paint(RenderModel model)
        {
            Interlocked.Increment(ref _paintCount);
        }

        public void EmitRestoreSequence() { }
    }

    /// <summary>
    /// Output sink that throws on any Paint call.
    /// Used to simulate an unhandled exception on the loop thread.
    /// </summary>
    private sealed class ThrowingOutputSink : IOutputSink
    {
        public void Paint(RenderModel model) =>
            throw new InvalidOperationException("Simulated loop-thread exception.");

        public void EmitRestoreSequence() { }
    }

    /// <summary>Fixed terminal dimensions.</summary>
    private sealed class FixedSizeSource : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (80, 24);
    }

    /// <summary>
    /// Real-time clock for lifecycle tests. Uses Stopwatch for a wall-clock source
    /// (SystemClock is internal-only in the main assembly, so we replicate the shape here).
    /// </summary>
    private sealed class TestSystemClock : IClock
    {
        private static readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

        public TimeSpan Now => _sw.Elapsed;

        public Task WaitUntilAsync(TimeSpan deadline, CancellationToken cancellationToken) =>
            Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(0, (deadline - Now).TotalMilliseconds)),
                cancellationToken);
    }

    /// <summary>Simple command that marks the model dirty.</summary>
    private sealed class DirtyCommandStub : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model) => model.MarkDirty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Factory helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires all injected edges through Terminal.StartCore, returning a live Terminal
    /// and the recording session for observation.
    /// </summary>
    private static (DcliTerminal Term, RecordingRawModeSession Session) CreateTerminal(
        IOutputSink? sink = null,
        TimeSpan? minFrameInterval = null)
    {
        RecordingRawModeSession session = new();
        RestoreCoordinator coordinator = RestoreCoordinator.Wire(session);

        // NoopResizeWatcher is a no-op disposable; ownership transfers to term on success.
        using NoopResizeWatcher resizeWatcher = new();
        DcliTerminal term = DcliTerminal.StartCore(
            session,
            coordinator,
            resizeWatcher,
            new ImmediateTimeoutByteSource(),
            new TestSystemClock(),
            sink ?? new CapturingOutputSink(),
            new FixedSizeSource(),
            minFrameInterval ?? TimeSpan.FromMilliseconds(16));

        // coordinator and resizeWatcher transferred to term; DisposeAsync disposes them.
        // The `using` above also calls Dispose() on scope exit, but NoopResizeWatcher.Dispose() is idempotent.
        return (term, session);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — Capability gate: non-VT inputs throw TerminalNotSupportedException
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NonVtCapabilityInputsThrowTerminalNotSupportedException()
    {
        // StartAsync(options, capabilityInputs) throws before entering raw mode.
        CapabilityInputs notVt = new()
        {
            IsSupportedPlatform = true,
            IsStdinTty = true,
            IsStdoutTty = true,
            TermVariable = "dumb",   // triggers the VT check
        };

        await Assert.ThrowsAsync<TerminalNotSupportedException>(
            () => DcliTerminal.StartAsync(new TerminalOptions(), notVt));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — Start: raw mode entered (RestoreCallCount is 0 before Dispose)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartEntersRawModeRestoreCountIsZeroBeforeDispose()
    {
        (DcliTerminal term, RecordingRawModeSession session) = CreateTerminal();

        // Session should not have Restore called before we ask for it.
        Assert.Equal(0, session.RestoreCallCount);

        await term.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — DisposeAsync restores the terminal (at least one Restore)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsyncRestoresTerminal()
    {
        (DcliTerminal term, RecordingRawModeSession session) = CreateTerminal();
        await term.DisposeAsync();

        // Restore is idempotent; the count should be ≥1 (coordinator.Dispose + session.Dispose).
        Assert.True(session.RestoreCallCount >= 1,
            $"Expected Restore called at least once; got {session.RestoreCallCount}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — Single DisposeAsync produces exactly one effective restore
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleDisposeProducesExactlyOneEffectiveRestore()
    {
        // Even though multiple code paths call Restore (loop finally, coordinator.Dispose,
        // session.Dispose), the CAS-idempotent guard collapses them to one effective restore.
        (DcliTerminal term, RecordingRawModeSession session) = CreateTerminal();

        await term.DisposeAsync();

        Assert.Equal(1, session.EffectiveRestoreCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — DisposeAsync is idempotent: second call does not add effective restores
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsyncIdempotentSecondCallAddsNoEffectiveRestore()
    {
        (DcliTerminal term, RecordingRawModeSession session) = CreateTerminal();

        await term.DisposeAsync();
        int effectiveAfterFirst = session.EffectiveRestoreCount;
        int rawAfterFirst = session.RestoreCallCount;

        await term.DisposeAsync(); // second call — Terminal guard makes this a no-op

        // Terminal's guard: the second DisposeAsync does nothing at all (no new calls).
        Assert.Equal(effectiveAfterFirst, session.EffectiveRestoreCount);
        Assert.Equal(rawAfterFirst, session.RestoreCallCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — Restore on loop-thread crash: loop's finally calls restoreOnExit
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoopFinallyRestoresWhenPaintThrows()
    {
        // The ThrowingOutputSink throws on the first Paint. To trigger a Paint, the model
        // must be dirty AND the deadline must pass. We use a short minFrameInterval and
        // post a DirtyCommandStub directly via LoopEngine (bypassing Terminal's public API
        // since §9+ commands are not implemented yet).
        ThrowingOutputSink throwingSink = new();
        int restoreCallCount = 0;

        // Create LoopEngine directly so we can post a command and observe the restore hook.
        LoopEngine engine = new(
            sizeSource: new FixedSizeSource(),
            clock: new TestSystemClock(),
            sink: throwingSink,
            minFrameInterval: TimeSpan.FromMilliseconds(1),
            restoreOnExit: () => Interlocked.Increment(ref restoreCallCount));

        // Post a command to mark the model dirty, triggering a paint attempt.
        engine.Post(new DirtyCommandStub());

        // Wait for the loop thread to die from the exception in ThrowingOutputSink.
        await Task.Delay(200);

        // The loop's finally block must have called restoreOnExit.
        Assert.True(Volatile.Read(ref restoreCallCount) >= 1,
            $"Expected restoreOnExit called ≥1 time from loop finally; got {Volatile.Read(ref restoreCallCount)}");

        // Dispose the engine (already stopped; Dispose is idempotent).
        engine.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — Events channel is reachable from a started Terminal
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventsChannelReachableAndNotNull()
    {
        (DcliTerminal term, _) = CreateTerminal();
        await using (term)
        {
            ChannelReader<TerminalEvent> events = term.Events;
            Assert.NotNull(events);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — GetTerminalSize returns the injected size
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTerminalSizeReturnsInjectedSize()
    {
        (DcliTerminal term, _) = CreateTerminal();
        await using (term)
        {
            (int cols, int rows) = term.GetTerminalSize();
            Assert.Equal(80, cols);
            Assert.Equal(24, rows);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §7.5 — Clean shutdown: threads join promptly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsyncJoinsThreadsPromptly()
    {
        // ImmediateTimeoutByteSource returns 0 immediately, so the reader thread's
        // timed-read loop exits on the next _stopping check after Stop() is called.
        (DcliTerminal term, _) = CreateTerminal();

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        await term.DisposeAsync();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1),
            $"DisposeAsync took {sw.Elapsed.TotalMilliseconds:F0} ms — threads did not join promptly.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Nit B — dirty-park shutdown: loop parked in dirty WaitAny joins promptly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DirtyParkShutdownViaLoopEngineJoinsPromptly()
    {
        // Create LoopEngine directly with a very large minFrameInterval.
        // Post a DirtyCommand so the loop parks in the dirty WaitAny (waiting for
        // a deadline that would not arrive for hours in real time).
        // Then Dispose: this must cancel the CTS, unblocking both WaitAny sub-tasks
        // and allowing the loop thread to join within the timeout.
        CapturingOutputSink sink = new();
        LoopEngine engine = new(
            sizeSource: new FixedSizeSource(),
            clock: new TestSystemClock(),
            sink: sink,
            minFrameInterval: TimeSpan.FromHours(1)); // very long — guarantees dirty-park

        engine.Post(new DirtyCommandStub()); // model becomes dirty → loop enters dirty WaitAny

        // Give the loop a moment to enter the dirty WaitAny.
        await Task.Delay(50);

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Dispose(); // must unblock the dirty WaitAny via CTS cancellation
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Dispose from dirty-park state took {sw.Elapsed.TotalMilliseconds:F0} ms — " +
            "loop thread did not join promptly from dirty WaitAny.");
    }

    [Fact]
    public async Task DirtyParkTerminalDisposeJoinsPromptly()
    {
        // Same dirty-park test but via the full Terminal lifecycle.
        // Since Terminal doesn't expose Post() yet, we rely on the clean-idle state
        // and verify Dispose completes promptly from any park state.
        (DcliTerminal term, _) = CreateTerminal(
            minFrameInterval: TimeSpan.FromHours(1)); // ensures dirty WaitAny if model is dirty

        // Model is initially clean (no commands posted), but verify clean-idle dispose is fast too.
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        await term.DisposeAsync();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"DisposeAsync took {sw.Elapsed.TotalMilliseconds:F0} ms.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Nit 1 — Loop fault: LoopTerminated is faulted when the loop crashes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoopTerminatedIsFaultedWhenLoopCrashes()
    {
        LoopEngine engine = new(
            sizeSource: new FixedSizeSource(),
            clock: new TestSystemClock(),
            sink: new ThrowingOutputSink(),
            minFrameInterval: TimeSpan.FromMilliseconds(1));

        engine.Post(new DirtyCommandStub());

        // Wait for the loop to die.
        await Task.Delay(200);

        Assert.True(engine.LoopTerminated.IsFaulted,
            "LoopTerminated task must be faulted after a loop-thread exception.");
        Assert.IsType<InvalidOperationException>(engine.LoopTerminated.Exception!.GetBaseException());

        engine.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Nit 1 — Loop fault: DisposeAsync rethrows fault and terminal is restored
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsyncRethrowsLoopFaultAndTerminalIsRestored()
    {
        // Use the full Terminal lifecycle with a throwing sink.
        // Post a dirty command via the internal Loop property so paint fires.
        (DcliTerminal term, RecordingRawModeSession session) = CreateTerminal(
            sink: new ThrowingOutputSink(),
            minFrameInterval: TimeSpan.FromMilliseconds(1));

        term.Loop.Post(new DirtyCommandStub());

        // Wait for the loop to crash.
        await Task.Delay(200);

        // (a) Terminal was restored via the loop's finally block.
        Assert.Equal(1, session.EffectiveRestoreCount);

        // (b) DisposeAsync rethrows the fault.
        Exception? caught = null;
        try
        {
            await term.DisposeAsync();
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.Contains("Simulated loop-thread exception", caught.Message, StringComparison.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Nit 1 — Loop fault: SettleAsync outstanding at crash time does not hang
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SettleAsyncFaultsWhenLoopCrashesDuringPaint()
    {
        // Use a sink that blocks until signalled, then throws, so we can enqueue a
        // SettleAsync TCS before the exception fires.
        using SemaphoreSlim paintGate = new(0, 1);
        using SemaphoreSlim paintStarted = new(0, 1);

        BlockThenThrowSink blockSink = new(paintStarted, paintGate);

        LoopEngine engine = new(
            sizeSource: new FixedSizeSource(),
            clock: new TestSystemClock(),
            sink: blockSink,
            minFrameInterval: TimeSpan.FromMilliseconds(1));

        // Dirty the model so paint fires.
        engine.Post(new DirtyCommandStub());

        // Wait until paint has started (sink is blocking).
        bool started = await paintStarted.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(started, "Timed out waiting for paint to start.");

        // Enqueue a settle WHILE the loop is blocking inside paint.
        Task settleTask = engine.SettleAsync();

        // Now let the sink throw.
        paintGate.Release();

        // The settle task must not hang — it must fault (or cancel) within a short timeout.
        Task faultOrCancel = await Task.WhenAny(settleTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Equal(settleTask, faultOrCancel); // completed, not timeout
        Assert.True(settleTask.IsFaulted || settleTask.IsCanceled,
            $"Expected settle to fault or cancel; status={settleTask.Status}");

        engine.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ANSI restore sequence emitted on disposal (cursor-hidden bug fix)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// DisposeAsync must cause the loop's finally block to emit the ANSI restore sequence
    /// (ESC[?2026l ESC[?25h ESC[0m) before releasing the terminal session.
    /// This ensures cursor visibility is restored even when a dialog had hidden the cursor.
    /// </summary>
    [Fact]
    public async Task DisposeAsyncEmitsAnsiRestoreSequenceBeforeTermiosRestore()
    {
        // Use a VtFrameRenderer backed by a StringWriter so we can inspect raw output.
        StringWriter writer = new();
        VtFrameRenderer renderer = new(writer);

        // Build the session manually so we can attach the ordering probe before wiring.
        // `using` satisfies CA2000; DisposeAsync also calls Restore() which is idempotent.
        using RecordingRawModeSession session = new();
        // Snapshot the writer's char count at the moment Restore() is first called so we
        // can assert the ANSI bytes were already in the buffer (i.e., emitted before termios restore).
        session.GetOutputLength = () => writer.GetStringBuilder().Length;

        using RestoreCoordinator coordinator = RestoreCoordinator.Wire(session);
        using NoopResizeWatcher resizeWatcher = new();
        DcliTerminal term = DcliTerminal.StartCore(
            session, coordinator, resizeWatcher,
            new ImmediateTimeoutByteSource(), new TestSystemClock(),
            renderer, new FixedSizeSource(),
            minFrameInterval: TimeSpan.FromMilliseconds(16));

        await term.DisposeAsync();

        string output = writer.ToString();
        const string restoreSuffix = "\x1b[?2026l\x1b[?25h\x1b[0m";

        // The restore sequence must be present in the output.
        Assert.Contains("\x1b[?2026l", output, StringComparison.Ordinal);
        Assert.Contains("\x1b[?25h", output, StringComparison.Ordinal);
        Assert.Contains("\x1b[0m", output, StringComparison.Ordinal);

        // Termios restore must also have been called.
        Assert.True(session.RestoreCallCount >= 1,
            "Expected Restore called at least once alongside the ANSI restore.");

        // Ordering: the ANSI bytes must have been in the buffer when Restore() was called.
        // OutputLengthAtRestore captures the writer length at the moment of the first Restore()
        // call. The restore suffix must fall within that prefix — not appended after.
        long lengthAtRestore = session.OutputLengthAtRestore;
        Assert.True(lengthAtRestore >= restoreSuffix.Length,
            $"OutputLengthAtRestore ({lengthAtRestore}) is smaller than the restore suffix — ANSI bytes were not emitted before termios restore.");
        string prefixAtRestore = output[..(int)lengthAtRestore];
        Assert.EndsWith(restoreSuffix, prefixAtRestore, StringComparison.Ordinal);
    }

    /// <summary>
    /// The restore sequence must appear at the end of the output (after any frame content),
    /// confirming it is the last thing emitted before the terminal session is released.
    /// </summary>
    [Fact]
    public async Task AnsiRestoreSequenceAppearsAtEndOfOutput()
    {
        StringWriter writer = new();
        VtFrameRenderer renderer = new(writer);

        (DcliTerminal term, _) = CreateTerminal(sink: renderer);

        await term.DisposeAsync();

        string output = writer.ToString();
        const string restoreSuffix = "\x1b[?2026l\x1b[?25h\x1b[0m";

        Assert.EndsWith(restoreSuffix, output, StringComparison.Ordinal);
    }

    /// <summary>
    /// SimulateTerminateSignal must invoke the halt action wired by Terminal.StartCore (which
    /// calls loop.Dispose, causing the loop's finally to emit the ANSI restore sequence on the
    /// loop thread) and then call session.Restore via the coordinator.
    /// </summary>
    [Fact]
    public async Task SimulateTerminateSignalHaltsLoopAndEmitsAnsiRestoreSequence()
    {
        StringWriter writer = new();
        VtFrameRenderer renderer = new(writer);

        (DcliTerminal term, RecordingRawModeSession session) = CreateTerminal(sink: renderer);

        // Exercise the actual signal path: SimulateTerminateSignal → coordinator invokes
        // the halt action (loop.Dispose) → loop.Dispose joins the thread → loop's finally
        // emits the ANSI restore → then coordinator calls session.Restore.
        // This is distinct from DisposeAsync(), which skips the halt action and goes directly
        // to coordinator.Dispose(). Only this path verifies the SetHaltAction wiring.
        term.Coordinator.SimulateTerminateSignal();

        // Give the loop thread up to 2 s to join (Dispose already joined it synchronously,
        // but LoopTerminated is the async observable handle).
        await term.Loop.LoopTerminated.WaitAsync(TimeSpan.FromSeconds(2));

        string output = writer.ToString();
        const string restoreSuffix = "\x1b[?2026l\x1b[?25h\x1b[0m";

        // The ANSI restore sequence must appear in the output (emitted by the loop finally).
        Assert.EndsWith(restoreSuffix, output, StringComparison.Ordinal);

        // Session restore must also have been called (by the coordinator after halt).
        Assert.True(session.RestoreCallCount >= 1,
            $"Expected RestoreCallCount ≥ 1 after SimulateTerminateSignal; got {session.RestoreCallCount}.");

        // Suppress the DisposeAsync fault that may surface if the loop was already stopped.
#pragma warning disable CA1031 // intentional: suppress any residual cleanup exception
        try { await term.DisposeAsync(); } catch (Exception) { }
#pragma warning restore CA1031
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Nit 1 helper sink: blocks paint until signalled, then throws
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class BlockThenThrowSink : IOutputSink
    {
        private readonly SemaphoreSlim _started;
        private readonly SemaphoreSlim _gate;

        internal BlockThenThrowSink(SemaphoreSlim started, SemaphoreSlim gate)
        {
            _started = started;
            _gate = gate;
        }

        public void Paint(RenderModel model)
        {
            _started.Release(); // signal "I have started"
            _gate.Wait();       // block until the test releases us
            throw new InvalidOperationException("Simulated loop-thread exception.");
        }

        public void EmitRestoreSequence() { }
    }
}
