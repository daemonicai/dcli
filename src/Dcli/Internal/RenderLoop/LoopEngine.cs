using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Dcli.Internal.Input;

namespace Dcli.Internal.RenderLoop;

/// <summary>
/// The single-writer render loop actor.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Threading contract — the load-bearing correctness property:</strong><br/>
/// One dedicated OS thread (<c>dcli-render-loop</c>) owns ALL mutable UI state. Every mutation
/// of <see cref="_model"/> happens inside <see cref="RunLoop"/> on that thread. No other thread
/// ever reads or writes the model fields. No locks are needed because there is exactly one writer.
/// </para>
/// <para>
/// The blocking point in the loop is <see cref="BlockUntilMessageOrDeadline"/>. When the model
/// is clean the loop parks via <c>WaitToReadAsync().AsTask().GetAwaiter().GetResult()</c> — zero
/// CPU until a message arrives. When dirty it races the channel wait against
/// <c>IClock.WaitUntilAsync(deadline)</c> via <c>Task.WaitAny</c>, so the deadline goes through
/// the virtual clock and cadence tests remain fully deterministic. Both calls are synchronous
/// blocking calls on the loop thread — the dedicated thread is never yielded to the pool. Every
/// line of <see cref="RunLoop"/> — including every read and write of <see cref="_model"/> —
/// executes on the single loop thread.
/// </para>
/// <para>
/// Producers call <see cref="Post"/> (non-blocking <c>TryWrite</c>) and return immediately before
/// the command is applied or a frame is painted.
/// </para>
/// <para>
/// <strong>Outbound channel:</strong> the loop writes via <c>TryWrite</c> on an unbounded channel.
/// A consumer that never drains the channel accumulates events but does not stall painting.
/// </para>
/// </remarks>
internal sealed class LoopEngine : IDisposable
{
    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>Minimum interval between successive frames (~16 ms ≈ 60 fps ceiling).</summary>
    internal static readonly TimeSpan DefaultMinFrameInterval = TimeSpan.FromMilliseconds(16);

    // ── Inbound channel ──────────────────────────────────────────────────────
    // Unbounded (Decision 10): apply is cheap; drains fast; no message is ever dropped.
    private readonly Channel<LoopMessage> _inbound = Channel.CreateUnbounded<LoopMessage>(
        new UnboundedChannelOptions { SingleReader = true });

    // ── Outbound channel ─────────────────────────────────────────────────────
    // Unbounded: TryWrite never blocks; a slow consumer accumulates without stalling the loop.
    private readonly Channel<TerminalEvent> _outbound = Channel.CreateUnbounded<TerminalEvent>(
        new UnboundedChannelOptions { SingleWriter = true });

    // ── Pending settle completions ────────────────────────────────────────────
    // SettleCommand enqueues a TCS here; the loop thread signals it after the next paint.
    // ConcurrentQueue because producers write from any thread, loop reads on loop thread.
    private readonly ConcurrentQueue<TaskCompletionSource> _pendingSettles = new();

    // ── Injected edges ───────────────────────────────────────────────────────
    private readonly IClock _clock;
    private readonly IOutputSink _sink;
    private readonly TimeSpan _minFrameInterval;

    // Optional hook called from RunLoop's finally so that an unhandled loop-thread
    // exception still restores the terminal. The RestoreCoordinator covers signals
    // and ProcessExit; this covers the loop-thread-crash path.
    private readonly Action? _restoreOnExit;

    // ── Loop state ───────────────────────────────────────────────────────────
    private readonly RenderModel _model;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();

    // Signals loop-thread exit to any awaiter (e.g. DisposeAsync fault rethrow).
    // RunContinuationsAsynchronously: continuations must not resume on the loop thread.
    private readonly TaskCompletionSource _loopTerminated =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ── Volatile terminal-size snapshot (Decision 10: snapshot reads) ────────
    // Updated on the loop thread; read from any thread via GetTerminalSize().
    private volatile int _snapshotColumns;
    private volatile int _snapshotRows;

    // Disposal guard.
    private int _disposed;

    // Instrumentation: how many times the loop entered its blocking wait.
    // Exposed for idle-efficiency tests; not part of the public surface.
    private int _waitCount;

    /// <summary>
    /// Initialises and starts the render loop on its dedicated thread.
    /// </summary>
    /// <param name="sizeSource">Provides the initial terminal dimensions.</param>
    /// <param name="clock">Supplies "now" and deadline waits.</param>
    /// <param name="sink">Destination for rendered frames.</param>
    /// <param name="minFrameInterval">
    /// Minimum time between successive paints. Defaults to <see cref="DefaultMinFrameInterval"/>.
    /// </param>
    /// <param name="restoreOnExit">
    /// Optional action called from <c>RunLoop</c>'s <c>finally</c> block so that an unhandled
    /// exception on the loop thread still restores the terminal. Must be idempotent (it may also
    /// be called by <see cref="Dispose"/> and the <see cref="RestoreCoordinator"/>).
    /// </param>
    /// <param name="maxFixedHeight">
    /// Optional consumer-supplied cap on the fixed-region height in rows.
    /// Forwarded to <see cref="RenderModel.MaxFixedHeight"/>; enforced by §10.
    /// </param>
    internal LoopEngine(
        ITerminalSizeSource sizeSource,
        IClock clock,
        IOutputSink sink,
        TimeSpan? minFrameInterval = null,
        Action? restoreOnExit = null,
        int? maxFixedHeight = null)
    {
        ArgumentNullException.ThrowIfNull(sizeSource);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(sink);

        _clock = clock;
        _sink = sink;
        _minFrameInterval = minFrameInterval ?? DefaultMinFrameInterval;
        _restoreOnExit = restoreOnExit;
        _model = new RenderModel(sizeSource, maxFixedHeight);

        _snapshotColumns = _model.Columns;
        _snapshotRows = _model.Rows;

        _thread = new Thread(RunLoop)
        {
            Name = "dcli-render-loop",
            IsBackground = true,
        };
        _thread.Start();
    }

    // ── Public surface ───────────────────────────────────────────────────────

    /// <summary>
    /// The outbound event stream. The consumer drains this on its own thread.
    /// The loop never reads this reader.
    /// </summary>
    internal ChannelReader<TerminalEvent> OutboundEvents => _outbound.Reader;

    /// <summary>
    /// How many times the loop entered its blocking wait.
    /// A truly-idle loop increments this once and parks; a busy-poll increments it thousands of times.
    /// </summary>
    internal int WaitCount => Volatile.Read(ref _waitCount);

    /// <summary>
    /// A task that completes when the loop thread exits.
    /// Completes successfully on a clean shutdown; faulted when the loop died due to an
    /// unhandled exception (e.g. a crashing sink or command). Use to detect loop death.
    /// </summary>
    internal Task LoopTerminated => _loopTerminated.Task;

    /// <summary>
    /// A <see cref="ChannelWriter{T}"/> that wraps incoming <see cref="InputEvent"/>s as
    /// <see cref="InputMessage"/>s before posting to the inbound channel.
    /// Wire §6's <see cref="InputReader"/> to this writer.
    /// </summary>
    internal ChannelWriter<InputEvent> InputWriter => new InputWrappingWriter(_inbound.Writer);

    /// <summary>
    /// Posts a fire-and-forget command to the loop. Returns before the command is applied
    /// or the frame is painted (Decision 10: API is fire-and-forget).
    /// </summary>
    internal void Post(ILoopCommand command)
    {
        // TryWrite on an unbounded channel always succeeds.
        _inbound.Writer.TryWrite(new CommandMessage(command));
    }

    /// <summary>
    /// Returns the last-known terminal size from the volatile snapshot.
    /// Does not round-trip to the loop thread (Decision 10: snapshot read).
    /// </summary>
    internal (int Columns, int Rows) GetTerminalSize() => (_snapshotColumns, _snapshotRows);

    // ── Internal settle helper (used by cadence tests; §15 will generalize) ──

    /// <summary>
    /// Drains all currently-enqueued inbound messages, produces at most one coalesced frame
    /// (if dirty and the deadline allows), then completes the returned task.
    /// </summary>
    /// <remarks>
    /// Provides deterministic cadence-test synchronization without wall-clock sleeps.
    /// §15's <c>HeadlessTerminal.SettleAsync</c> will generalize this into a public contract.
    ///
    /// A <see cref="SettleCommand"/> sentinel is enqueued. When the loop applies it, the
    /// <see cref="RenderModel"/> is marked dirty and the TCS is queued for post-paint signaling.
    /// After the paint step in <see cref="RunLoop"/> the loop drains all pending settle TCSes,
    /// ensuring the caller's task completes only after the frame (if any) has been produced.
    /// </remarks>
    internal Task SettleAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _inbound.Writer.TryWrite(new CommandMessage(new SettleCommand(_pendingSettles, tcs)));
        return tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        // Complete the channel so WaitToReadAsync unblocks immediately; the clock wait is
        // cancelled via the loop token, so both waits in BlockUntilMessageOrDeadline exit.
        _inbound.Writer.TryComplete();
        _thread.Join(timeout: TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }

    // ── Loop body ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The render loop. Runs entirely on the dedicated <c>dcli-render-loop</c> thread.
    /// Every read and write of <see cref="_model"/> occurs here.
    /// </summary>
    private void RunLoop()
    {
        CancellationToken ct = _cts.Token;
        TimeSpan nextPaintDeadline = _clock.Now; // first dirty state may paint immediately

        Exception? loopFault = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Block the loop thread until a message is available, or — only when dirty —
                // until the next paint deadline arrives. Clean model: park indefinitely on
                // the channel (zero CPU). Returns false when cancelled or channel is completed.
                if (!BlockUntilMessageOrDeadline(nextPaintDeadline, waitForDeadline: _model.IsDirty, ct))
                    break;

                // Drain all available messages. Thread discipline: all ApplyMessage calls run
                // here on the loop thread — the only mutator of _model.
                while (_inbound.Reader.TryRead(out LoopMessage? msg))
                {
                    ApplyMessage(msg);
                }

                // Paint only when dirty and the minimum frame interval has elapsed.
                TimeSpan now = _clock.Now;
                if (_model.IsDirty && now >= nextPaintDeadline)
                {
                    // §10 integration point: compose the fixed region (input + status) first.
                    // This sets FixedRegionRows, which §9's PrePaint reads to compute the live
                    // window budget (maxHeight = rows − fixedRegionRows.Count).
                    _model.FixedRegion.Compose(_model);

                    // §9 integration point: recompute LiveWindowRows and NewlyCommittedRows
                    // from the live object list, applying the commit-horizon overflow rule.
                    // Must run after fixed-region compose and before Paint.
                    _model.Scrollback.PrePaint(_model);

                    // §10 caret finalisation: offset the editor-local caret row by
                    // LiveWindowRows.Count to produce the full-frame caret position.
                    if (_model.EditorCaretLocal is (int editorRow, int editorCol))
                        _model.CaretPosition = (_model.LiveWindowRows.Count + editorRow, editorCol);
                    else
                        _model.CaretPosition = null;

                    _sink.Paint(_model);
                    // Reset NewlyCommittedRows to empty after the frame — committed rows must
                    // be emitted exactly once (never rewritten on the next frame).
                    _model.Scrollback.ClearCommitted(_model);
                    _model.ClearDirty();
                    nextPaintDeadline = now + _minFrameInterval;
                }

                // Signal any pending settle waiters (after the paint so they observe the frame).
                DrainSettles();
            }

            // Signal any settles that are still pending at shutdown.
            DrainSettles();
        }
#pragma warning disable CA1031 // background actor-thread root: must not propagate; finally restores terminal
        catch (Exception ex)
        {
            // An unhandled exception on the loop thread (e.g. a crash in the sink or a
            // command's Apply). The exception must NOT propagate to the thread root — that
            // would crash the process. The finally block below handles restoration and faults
            // the loop-terminated task so callers (e.g. DisposeAsync) can observe the crash.
            loopFault = ex;
            FaultSettles(ex);
        }
#pragma warning restore CA1031
        finally
        {
            // Restore the terminal regardless of how the loop exits (normal stop, exception,
            // or external cancellation). The action is idempotent — Dispose, signals, and
            // ProcessExit all converge on the same CAS-guarded IRawModeSession.Restore.
            _restoreOnExit?.Invoke();

            // Signal loop-thread exit. Fault if the loop crashed; succeed on clean shutdown.
            // RunContinuationsAsynchronously ensures awaiters do not resume on the loop thread.
            if (loopFault is not null)
                _loopTerminated.TrySetException(loopFault);
            else
                _loopTerminated.TrySetResult();
        }
    }

    /// <summary>Applies one inbound message to the render model. Loop-thread only.</summary>
    private void ApplyMessage(LoopMessage msg)
    {
        switch (msg)
        {
            case InputMessage inputMsg:
                ApplyInputEvent(inputMsg.Event);
                break;

            case CommandMessage cmdMsg:
                cmdMsg.Command.Apply(_model);
                break;
        }
    }

    /// <summary>
    /// Routes an input event to the editor or falls through to the outbound channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Editing keys</strong> (consumed — mutate the editor, mark dirty, do NOT emit):
    /// printable Unicode scalars → <c>Insert</c>; Backspace → <c>Backspace</c>;
    /// Delete → <c>Delete</c>; Left/Right → <c>MoveLeft/MoveRight</c>;
    /// Home/End → <c>MoveHome/MoveEnd</c>; Up/Down → history navigation when the caret is on
    /// the first/last visual row respectively, otherwise <c>MoveUp/MoveDown</c>.
    /// </para>
    /// <para>
    /// <strong>Fall-through keys</strong> (not consumed here — emitted on the outbound channel):
    /// Enter, Tab, Ctrl+*, Alt+*, and any key not listed above. §12 handles submit-on-Enter,
    /// completion, and the full event story.
    /// </para>
    /// <para>
    /// <strong>History-nav convention:</strong> Up navigates to the previous (older) history
    /// entry when the caret is on the <em>first</em> visual row; otherwise moves the caret up
    /// within the editor. Down navigates to the next (newer) entry when the caret is on the
    /// <em>last</em> visual row; otherwise moves the caret down.
    /// </para>
    /// </remarks>
    private void ApplyInputEvent(InputEvent ev)
    {
        _model.MarkDirty();

        switch (ev)
        {
            case KeyEvent ke:
                bool consumed = false;

                // Intercept-chain: active overlay is the FRONT (first refusal).
                if (_model.ActiveOverlay is { } overlay)
                {
                    consumed = overlay.HandleKey(ke);
                    if (overlay.IsDismissed)
                        _model.ClearOverlay(); // §12 will complete the dialog's TCS here first
                }

                if (!consumed)
                    consumed = RouteEditorKey(ke);

                if (!consumed)
                    _outbound.Writer.TryWrite(new KeyPressed(ke));
                break;

            case ResizeEvent re:
                _model.UpdateSize(re.Columns, re.Rows);
                _snapshotColumns = re.Columns;
                _snapshotRows = re.Rows;
                _outbound.Writer.TryWrite(new Resized(re.Columns, re.Rows));
                break;
        }
    }

    /// <summary>
    /// Attempts to route <paramref name="ke"/> to the input editor.
    /// Returns <see langword="true"/> when the key was consumed (editor mutated or navigation
    /// performed); <see langword="false"/> when the key should fall through to the outbound channel.
    /// </summary>
    private bool RouteEditorKey(KeyEvent ke)
    {
        // Keys with Ctrl or Alt modifiers fall through (§12 owns submit / completion / etc.).
        if ((ke.Modifiers & (Modifiers.Ctrl | Modifiers.Alt)) != Modifiers.None)
            return false;

        int width = _model.Columns;
        var editor = _model.FixedRegion.Editor;

        if (ke.Code.Kind == KeyCode.KeyCodeKind.UnicodeScalar)
        {
            Rune rune = ke.Code.RuneValue;
            // Only insert displayable characters — skip control characters (U+0000..U+001F, U+007F).
            if (rune.Value >= 0x20 && rune.Value != 0x7F)
            {
                editor.Insert(rune);
                return true;
            }
            return false;
        }

        if (ke.Code.Kind == KeyCode.KeyCodeKind.Named)
        {
            switch (ke.Code.NamedValue)
            {
                case NamedKey.Backspace:
                    editor.Backspace();
                    return true;

                case NamedKey.Delete:
                    editor.Delete();
                    return true;

                case NamedKey.Left:
                    editor.MoveLeft();
                    return true;

                case NamedKey.Right:
                    editor.MoveRight();
                    return true;

                case NamedKey.Home:
                    editor.MoveHome(width);
                    return true;

                case NamedKey.End:
                    editor.MoveEnd(width);
                    return true;

                case NamedKey.Up:
                    {
                        (bool isFirst, _) = editor.GetCaretVisualRowBounds(width);
                        if (isFirst)
                            editor.RecallPrevious();
                        else
                            editor.MoveUp(width);
                        return true;
                    }

                case NamedKey.Down:
                    {
                        (_, bool isLast) = editor.GetCaretVisualRowBounds(width);
                        if (isLast)
                            editor.RecallNext();
                        else
                            editor.MoveDown(width);
                        return true;
                    }

                // Enter, Tab, and all others fall through to §12.
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Signals all pending settle TCSes. Called after each paint attempt on the loop thread.
    /// </summary>
    private void DrainSettles()
    {
        while (_pendingSettles.TryDequeue(out TaskCompletionSource? tcs))
        {
            tcs.TrySetResult();
        }
    }

    /// <summary>
    /// Faults all pending settle TCSes with <paramref name="fault"/>. Called from the
    /// catch block when the loop dies so no settle awaiter hangs forever. Also drains
    /// any <see cref="SettleCommand"/>s still in the inbound channel (i.e. enqueued after
    /// the last drain but before the exception propagated) and faults them too.
    /// </summary>
    private void FaultSettles(Exception fault)
    {
        // Fault TCSes that were already moved to the pending queue.
        while (_pendingSettles.TryDequeue(out TaskCompletionSource? tcs))
        {
            tcs.TrySetException(fault);
        }

        // Also drain the inbound channel for SettleCommands that arrived after the last
        // drain but before the exception fired (e.g. enqueued while Paint was blocking).
        while (_inbound.Reader.TryRead(out LoopMessage? msg))
        {
            if (msg is CommandMessage { Command: SettleCommand settle })
                settle.FaultExternally(fault);
        }
    }

    // ── Blocking wait ─────────────────────────────────────────────────────────

    /// <summary>
    /// Blocks the loop thread until a message is available on the inbound channel, or —
    /// when <paramref name="waitForDeadline"/> is <see langword="true"/> — until
    /// <paramref name="deadline"/> is reached via <see cref="IClock.WaitUntilAsync"/>.
    /// Returns <see langword="true"/> when a message is (likely) available or the deadline
    /// was reached; <see langword="false"/> on cancellation or channel completion.
    /// </summary>
    /// <param name="deadline">The next paint deadline (only used when <paramref name="waitForDeadline"/> is true).</param>
    /// <param name="waitForDeadline">
    /// <see langword="true"/> when the model is dirty and a deadline wake is needed;
    /// <see langword="false"/> when the model is clean — parks indefinitely on the channel.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// Clean-idle path: <c>WaitToReadAsync().AsTask().GetAwaiter().GetResult()</c> parks the
    /// OS thread in the kernel wait until the channel is signalled — zero CPU while idle.
    /// </para>
    /// <para>
    /// Dirty path: <c>Task.WaitAny</c> races the channel wait against
    /// <c>IClock.WaitUntilAsync(deadline)</c>. The deadline goes through <see cref="IClock"/>
    /// so the virtual-clock test double can advance time deterministically without real time passing.
    /// </para>
    /// <para>
    /// Both paths are synchronous blocking calls on the loop thread — no <c>await</c>, no
    /// thread-pool continuation, no mutation on any thread other than the dedicated loop thread.
    /// </para>
    /// </remarks>
    private bool BlockUntilMessageOrDeadline(TimeSpan deadline, bool waitForDeadline, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _inbound.Reader.Completion.IsCompleted)
            return false;

        // Fast path: message already waiting.
        if (_inbound.Reader.TryPeek(out _))
            return true;

        Interlocked.Increment(ref _waitCount);

        if (!waitForDeadline)
        {
            // Model is clean — no paint pending. Park on the channel until a message arrives.
            // GetResult() blocks the OS thread in the kernel; zero CPU while idle.
            try
            {
                bool hasData = _inbound.Reader.WaitToReadAsync(ct).AsTask().GetAwaiter().GetResult();
                return hasData;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        else
        {
            // Model is dirty — race the channel wait against the paint deadline.
            // The deadline goes through IClock so the virtual clock controls cadence in tests.
            //
            // A per-iteration linked CTS lets us cancel the losing task's waiter so it
            // doesn't orphan a live Task.Delay / VirtualClock TCS registration.
            // The linked CTS is disposed (which cancels it) in the finally block.
            using CancellationTokenSource iterationCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct);

            Task waitToReadTask = _inbound.Reader.WaitToReadAsync(iterationCts.Token).AsTask();
            Task deadlineTask = _clock.WaitUntilAsync(deadline, iterationCts.Token);

            try
            {
                // WaitAny unblocks when either task completes (including via cancellation).
                // Cancellation propagates through the tasks themselves (iterationCts.Token is
                // passed to both); passing CancellationToken.None here avoids a redundant
                // OperationCanceledException throw from WaitAny itself.
                Task.WaitAny([waitToReadTask, deadlineTask], CancellationToken.None);
            }
            finally
            {
                // Cancel the losing waiter. The tasks swallow OperationCanceledException
                // internally, so no unobserved-task warning is produced.
                iterationCts.Cancel();
            }

            // Cancellation or channel completion handled by the next iteration check.
            return !ct.IsCancellationRequested && !_inbound.Reader.Completion.IsCompleted;
        }
    }

    // ── SettleCommand ──────────────────────────────────────────────────────────

    private sealed class SettleCommand : ILoopCommand
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _settleQueue;
        private readonly TaskCompletionSource _tcs;

        internal SettleCommand(ConcurrentQueue<TaskCompletionSource> settleQueue, TaskCompletionSource tcs)
        {
            _settleQueue = settleQueue;
            _tcs = tcs;
        }

        void ILoopCommand.Apply(RenderModel model)
        {
            // Mark the model dirty so the loop evaluates the paint condition after draining.
            model.MarkDirty();
            // Enqueue the TCS; it will be signaled by DrainSettles() after the paint.
            _settleQueue.Enqueue(_tcs);
        }

        /// <summary>
        /// Faults the settle TCS directly. Called when the loop crashes before this command
        /// was applied (i.e. it was still in the inbound channel at crash time).
        /// </summary>
        internal void FaultExternally(Exception fault) => _tcs.TrySetException(fault);
    }

    // ── InputWrappingWriter ────────────────────────────────────────────────────

    /// <summary>
    /// Adapts <see cref="ChannelWriter{LoopMessage}"/> to <see cref="ChannelWriter{InputEvent}"/>
    /// by wrapping each event in an <see cref="InputMessage"/>.
    /// </summary>
    private sealed class InputWrappingWriter : ChannelWriter<InputEvent>
    {
        private readonly ChannelWriter<LoopMessage> _inner;

        internal InputWrappingWriter(ChannelWriter<LoopMessage> inner) => _inner = inner;

        public override bool TryWrite(InputEvent item) =>
            _inner.TryWrite(new InputMessage(item));

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
            _inner.WaitToWriteAsync(cancellationToken);

        public override ValueTask WriteAsync(InputEvent item, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(new InputMessage(item), cancellationToken);
    }
}
