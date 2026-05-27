using System.Text;
using Dcli.Internal;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.Input;
using Dcli.Internal.RenderLoop;
using Xunit;
using DcliTerminal = Dcli.Terminal;

namespace Dcli.Tests;

/// <summary>
/// End-to-end tests for §12 Chunk C: façade surfaces (Scrollback, Input, Status, Autocomplete)
/// and event emission (InputSubmitted, InputChanged).
/// All tests use injected edges — no real tty, no real raw mode.
/// </summary>
public sealed class FacadeTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private sealed class CapturingOutputSink : IOutputSink
    {
        private volatile RenderModel? _lastModel;

        internal RenderModel? LastModel => _lastModel;

        public void Paint(RenderModel model)
        {
            _lastModel = model;
        }
    }

    private sealed class ImmediateTimeoutByteSource : IInputByteSource
    {
        public int Read(Span<byte> buffer) => 0;
    }

    private sealed class FixedSizeSource : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (80, 24);
    }

    private sealed class VirtualClock : IClock
    {
        private readonly object _lock = new();
        private TimeSpan _now;
        private readonly List<(TimeSpan Deadline, TaskCompletionSource Tcs, CancellationTokenSource LinkedCts)> _waiters = [];

        internal VirtualClock(TimeSpan? initial = null) => _now = initial ?? TimeSpan.Zero;

        public TimeSpan Now { get { lock (_lock) { return _now; } } }

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
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            linkedCts.Token.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                tcs,
                useSynchronizationContext: false);
            lock (_lock)
            {
                if (deadline <= _now) { linkedCts.Dispose(); return Task.CompletedTask; }
                _waiters.Add((deadline, tcs, linkedCts));
            }
            return tcs.Task;
        }
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires all injected edges through Terminal.StartCore. session and coordinator are
    /// transferred to term and disposed by term.DisposeAsync; they are returned in the tuple so
    /// the CA2000 analyzer sees they escape the factory and are not leaked locally.
    /// </summary>
    private static (DcliTerminal Term, CapturingOutputSink Sink, VirtualClock Clock,
        RecordingRawModeSession Session) CreateTerminal()
    {
        RecordingRawModeSession session = new();
        RestoreCoordinator coordinator = RestoreCoordinator.Wire(session);
        CapturingOutputSink sink = new();
        VirtualClock clock = new(TimeSpan.Zero);

        DcliTerminal term = DcliTerminal.StartCore(
            session,
            coordinator,
            new ImmediateTimeoutByteSource(),
            clock,
            sink,
            new FixedSizeSource(),
            minFrameInterval: TimeSpan.Zero);

        return (term, sink, clock, session);
    }

    // Settle with a 5-second wall-clock guard so tests cannot hang.
    private static Task SettleAsync(DcliTerminal term, VirtualClock clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(10));
        return term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Line PlainLine(string text) => new([new Segment(text)]);

    // ── Drain helpers for outbound event channel ──────────────────────────────

    /// <summary>
    /// Drains all currently available events from the outbound channel.
    /// Returns immediately — does not block waiting for new events.
    /// </summary>
    private static List<TerminalEvent> DrainEvents(DcliTerminal term)
    {
        List<TerminalEvent> events = [];
        while (term.Events.TryRead(out TerminalEvent? ev))
            events.Add(ev);
        return events;
    }

    /// <summary>
    /// Waits for at least one event to arrive on the outbound channel, with a timeout guard.
    /// Returns all events drained after the wait.
    /// </summary>
    private static async Task<List<TerminalEvent>> WaitForEventsAsync(
        DcliTerminal term,
        int minCount = 1)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        List<TerminalEvent> events = [];

        while (events.Count < minCount)
        {
            if (term.Events.TryRead(out TerminalEvent? ev))
            {
                events.Add(ev);
            }
            else
            {
                // Wait for a new item to arrive, then loop.
                await term.Events.WaitToReadAsync(cts.Token);
            }
        }

        // Drain any additional items that arrived at the same time.
        while (term.Events.TryRead(out TerminalEvent? extra))
            events.Add(extra);

        return events;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.4 — InputChanged emitted on user key press
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Typing a printable key emits InputChanged with the new buffer text.
    /// This is the key input-change → candidates round-trip test from 12.5.
    /// </summary>
    [Fact]
    public async Task TypingPrintableKeyEmitsInputChanged()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Post a printable 'x' key.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('x')), Modifiers.None));

            await SettleAsync(term, clock);

            List<TerminalEvent> events = await WaitForEventsAsync(term);

            InputChanged? changed = events.OfType<InputChanged>().FirstOrDefault();
            Assert.NotNull(changed);
            Assert.Equal("x", changed.Text);

            // Also verify the editor state via the sink's captured model.
            Assert.NotNull(sink.LastModel);
            Assert.Equal("x", sink.LastModel.FixedRegion.Editor.Text);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.5 — Input-change → autocomplete candidates round-trip
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After receiving InputChanged, consumer calls Autocomplete.Show; the overlay appears.
    /// </summary>
    [Fact]
    public async Task InputChangedThenShowAutocompleteDisplaysOverlay()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Type 'x' to generate InputChanged.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('x')), Modifiers.None));

            await SettleAsync(term, clock);

            List<TerminalEvent> events = await WaitForEventsAsync(term);
            Assert.Contains(events, e => e is InputChanged { Text: "x" });

            // Consumer responds with autocomplete candidates.
            Line display = PlainLine("xyz - a candidate");
            term.Autocomplete.Show([new AutocompleteCandidate("xyz", display)]);

            await SettleAsync(term, clock);

            // The active overlay must be an autocomplete showing that candidate.
            Assert.NotNull(sink.LastModel);
            Autocomplete? overlay = sink.LastModel.ActiveOverlay as Autocomplete;
            Assert.NotNull(overlay);
            // Verify the candidate is reflected in the rendered rows.
            IReadOnlyList<Line> rows = overlay.Render(80);
            Assert.True(rows.Count > 0, "Autocomplete overlay must render at least one row.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.4 — InputSubmitted on Enter
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Typing text then pressing Enter emits InputSubmitted with the text, clears the buffer,
    /// and the text is recallable from history via Up.
    /// </summary>
    [Fact]
    public async Task EnterAfterTypingEmitsInputSubmittedClearsBufferAddsToHistory()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Type 'h', 'i'.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('h')), Modifiers.None));
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('i')), Modifiers.None));

            await SettleAsync(term, clock);
            // Consume the InputChanged events.
            DrainEvents(term);

            // Press Enter.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(term, clock);

            List<TerminalEvent> events = await WaitForEventsAsync(term);

            // (a) InputSubmitted with "hi".
            InputSubmitted? submitted = events.OfType<InputSubmitted>().FirstOrDefault();
            Assert.NotNull(submitted);
            Assert.Equal("hi", submitted.Text);

            // (b) KeyPressed(Enter) must NOT appear — Enter is consumed by submit.
            Assert.DoesNotContain(events, e => e is KeyPressed { Key.Code.NamedValue: NamedKey.Enter });

            // (c) Buffer was cleared after submit.
            Assert.NotNull(sink.LastModel);
            Assert.Equal(string.Empty, sink.LastModel.FixedRegion.Editor.Text);

            // (d) "hi" is recallable from history: press Up.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Up), Modifiers.None));
            await SettleAsync(term, clock);

            // After Up, the editor should contain "hi".
            Assert.Equal("hi", sink.LastModel.FixedRegion.Editor.Text);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.4 — Empty Enter emits InputSubmitted("") but nothing added to history
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyEnterEmitsInputSubmittedEmptyAndAddsNothingToHistory()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Press Enter with empty buffer.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(term, clock);
            List<TerminalEvent> events = await WaitForEventsAsync(term);

            InputSubmitted? submitted = events.OfType<InputSubmitted>().FirstOrDefault();
            Assert.NotNull(submitted);
            Assert.Equal(string.Empty, submitted.Text);

            // History should still be empty — pressing Up should not change the buffer.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Up), Modifiers.None));
            await SettleAsync(term, clock);

            // Buffer remains empty (Up is a no-op with empty history).
            Assert.NotNull(sink.LastModel);
            Assert.Equal(string.Empty, sink.LastModel.FixedRegion.Editor.Text);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.4 — No InputChanged for pure caret moves (Left/Right)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PureCaretMoveDoesNotEmitInputChanged()
    {
        (DcliTerminal term, _, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Type something first so Left/Right has text to move over.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('a')), Modifiers.None));
            await SettleAsync(term, clock);
            DrainEvents(term); // discard the InputChanged for 'a'

            // Move caret left — should not emit InputChanged.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Left), Modifiers.None));
            await SettleAsync(term, clock);

            List<TerminalEvent> leftEvents = DrainEvents(term);
            Assert.DoesNotContain(leftEvents, e => e is InputChanged);

            // Move caret right — should not emit InputChanged.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Right), Modifiers.None));
            await SettleAsync(term, clock);

            List<TerminalEvent> rightEvents = DrainEvents(term);
            Assert.DoesNotContain(rightEvents, e => e is InputChanged);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.3 — Input.SetText and Input.Clear do NOT emit InputChanged
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProgrammaticSetTextDoesNotEmitInputChanged()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            term.Input.SetText("hello");
            await SettleAsync(term, clock);

            List<TerminalEvent> events = DrainEvents(term);

            // No InputChanged.
            Assert.DoesNotContain(events, e => e is InputChanged);

            // But the buffer was updated.
            Assert.NotNull(sink.LastModel);
            Assert.Equal("hello", sink.LastModel.FixedRegion.Editor.Text);
        }
    }

    [Fact]
    public async Task ProgrammaticClearDoesNotEmitInputChanged()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            term.Input.SetText("hello");
            await SettleAsync(term, clock);
            DrainEvents(term);

            term.Input.Clear();
            await SettleAsync(term, clock);

            List<TerminalEvent> events = DrainEvents(term);
            Assert.DoesNotContain(events, e => e is InputChanged);

            Assert.NotNull(sink.LastModel);
            Assert.Equal(string.Empty, sink.LastModel.FixedRegion.Editor.Text);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.3 — Scrollback.Append adds a live row
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScrollbackAppendAddsLiveRow()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            Line hello = PlainLine("hello scrollback");
            term.Scrollback.Append(hello);
            await SettleAsync(term, clock);

            Assert.NotNull(sink.LastModel);
            IReadOnlyList<Line> liveRows = sink.LastModel.LiveWindowRows;
            Assert.True(liveRows.Count > 0, "Expected at least one live row after Append.");
            // The hello line's text should appear in the live window.
            bool found = liveRows.Any(row => row.Segments.Any(s =>
                s.Text.Contains("hello scrollback", StringComparison.Ordinal)));
            Assert.True(found, "Expected 'hello scrollback' in live window rows.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.3 — BeginLive + AppendText + Commit streams then freezes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LiveBlockAppendTextThenCommitStreamsThenFreezes()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            ILiveBlock block = term.Scrollback.BeginLive();
            block.AppendText("streaming...");
            await SettleAsync(term, clock);

            // While live: text should appear in LiveWindowRows.
            Assert.NotNull(sink.LastModel);
            bool liveBefore = sink.LastModel.LiveWindowRows.Any(row =>
                row.Segments.Any(s => s.Text.Contains("streaming", StringComparison.Ordinal)));
            Assert.True(liveBefore, "Expected live block text in LiveWindowRows before commit.");

            // Commit the block.
            block.Commit();
            await SettleAsync(term, clock);

            // After commit, the block drains from the live list into NewlyCommittedRows (or has
            // already been emitted). The live window should no longer contain it (it committed).
            // Since NewlyCommittedRows is cleared after each paint, we only check that the
            // live window shrank (block was removed from live list).
            bool liveAfter = sink.LastModel.LiveWindowRows.Any(row =>
                row.Segments.Any(s => s.Text.Contains("streaming", StringComparison.Ordinal)));
            Assert.False(liveAfter, "Committed block must not remain in LiveWindowRows.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.3 — Status.Set sets status rows
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StatusSetUpdatesStatusRows()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            Line statusLine = PlainLine("ready");
            term.Status.SetRows(statusLine);
            await SettleAsync(term, clock);

            Assert.NotNull(sink.LastModel);
            IReadOnlyList<Line> statusRows = sink.LastModel.FixedRegion.Status.Rows;
            Assert.Single(statusRows);
            Assert.Same(statusLine, statusRows[0]);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.3 — Autocomplete.Hide clears the overlay
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AutocompleteHideClearsOverlay()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Show an autocomplete overlay.
            term.Autocomplete.Show([new AutocompleteCandidate("item1", PlainLine("item1"))]);
            await SettleAsync(term, clock);

            Assert.NotNull(sink.LastModel);
            Assert.IsType<Autocomplete>(sink.LastModel.ActiveOverlay);

            // Hide it.
            term.Autocomplete.Hide();
            await SettleAsync(term, clock);

            Assert.Null(sink.LastModel.ActiveOverlay);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.3 — Autocomplete.Hide is a no-op when a dialog is active
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AutocompleteHideIsNoOpWhenDialogActive()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Open a dialog — this takes priority over autocomplete.
            Task<DialogResult<int>> dialogTask = term.SelectAsync(
                new SelectRequest([PlainLine("opt1"), PlainLine("opt2")]));

            clock.Advance(TimeSpan.FromMilliseconds(10));
            await term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Calling Hide should not clear the dialog.
            term.Autocomplete.Hide();
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

            // Dialog must still be active.
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Clean up: dismiss the dialog.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await dialogTask; // must be cancelled
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.3 — Autocomplete.Show is no-op when modal dialog is active
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AutocompleteShowIsNoOpWhenDialogActive()
    {
        (DcliTerminal term, CapturingOutputSink sink, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            // Open a dialog.
            Task<DialogResult<int>> dialogTask = term.SelectAsync(
                new SelectRequest([PlainLine("opt1")]));

            clock.Advance(TimeSpan.FromMilliseconds(10));
            await term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Try to show autocomplete — should be a no-op.
            term.Autocomplete.Show([new AutocompleteCandidate("x", PlainLine("x"))]);
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

            // Dialog must still be the active overlay (not replaced by autocomplete).
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Clean up.
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await term.Loop.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await dialogTask;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12.4 — Ctrl+Enter falls through as KeyPressed, not InputSubmitted
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CtrlEnterFallsThroughAsKeyPressedNotSubmitted()
    {
        (DcliTerminal term, _, VirtualClock clock, _) = CreateTerminal();
        await using (term)
        {
            term.Loop.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.Ctrl));
            await SettleAsync(term, clock);

            List<TerminalEvent> events = await WaitForEventsAsync(term);

            // Must emit KeyPressed, not InputSubmitted.
            Assert.Contains(events, e => e is KeyPressed { Key.Code.NamedValue: NamedKey.Enter });
            Assert.DoesNotContain(events, e => e is InputSubmitted);
        }
    }
}
