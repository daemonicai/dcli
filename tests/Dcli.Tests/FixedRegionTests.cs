using System.Text;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;
using Dcli.Internal.Scrollback;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for tasks 10.3–10.5: StatusLine, FixedRegionComposer cap formula,
/// stack/budget behaviour, caret in combined frame, key routing, and the
/// wrap-boundary caret convention pin (Chunk-A reviewer nit).
/// </summary>
public sealed class FixedRegionTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RenderModel MakeModel(int cols = 80, int rows = 24, int? maxFixedHeight = null)
    {
        ConstantSizeSource size = new(cols, rows);
        return new RenderModel(size, maxFixedHeight);
    }

    private sealed class ConstantSizeSource : ITerminalSizeSource
    {
        private readonly int _cols;
        private readonly int _rows;

        internal ConstantSizeSource(int cols, int rows)
        {
            _cols = cols;
            _rows = rows;
        }

        public (int Columns, int Rows) GetSize() => (_cols, _rows);
    }

    private static Line PlainLine(string text) =>
        new([new Segment(text)]);

    // ── 10.5 — Cap formula ───────────────────────────────────────────────────

    [Fact]
    public void CapDefault24Rows()
    {
        // No configured MaxFixedHeight on 24-row terminal → 50% of 24 = 12.
        int cap = FixedRegionComposer.ComputeCap(rows: 24, appSet: null);
        Assert.Equal(12, cap);
    }

    [Fact]
    public void CapMinFloor10Rows()
    {
        // 50% of 10 = 5 < 8 (floor), rows ≥ 8 → cap = 8.
        int cap = FixedRegionComposer.ComputeCap(rows: 10, appSet: null);
        Assert.Equal(8, cap);
    }

    [Fact]
    public void CapTinyTerminal5Rows()
    {
        // rows < 8 → cap = rows (floor yields to terminal height).
        int cap = FixedRegionComposer.ComputeCap(rows: 5, appSet: null);
        Assert.Equal(5, cap);
    }

    [Fact]
    public void CapAppSetClampedIntoRange()
    {
        // appSet=4 < 8 on 24-row terminal → clamped to min 8.
        Assert.Equal(8, FixedRegionComposer.ComputeCap(rows: 24, appSet: 4));

        // appSet=30 > 24 rows → clamped to rows.
        Assert.Equal(24, FixedRegionComposer.ComputeCap(rows: 24, appSet: 30));

        // appSet=10 in [8, 24] → passes through unchanged.
        Assert.Equal(10, FixedRegionComposer.ComputeCap(rows: 24, appSet: 10));
    }

    [Fact]
    public void CapTinyTerminalAppSetIgnored()
    {
        // rows=5 < 8 → cap = rows regardless of appSet.
        Assert.Equal(5, FixedRegionComposer.ComputeCap(rows: 5, appSet: 3));
        Assert.Equal(5, FixedRegionComposer.ComputeCap(rows: 5, appSet: 10));
    }

    // ── 10.5 — Stack / budget behaviour ─────────────────────────────────────

    [Fact]
    public void StatusAlwaysShown()
    {
        // Even when cap is tight, status rows appear in the fixed region.
        // cap = rows/2 = 4 (rows=8). Status=2, input=1 row → fixedRows = 3 (input + status).
        RenderModel model = MakeModel(cols: 80, rows: 8);
        StatusLine status = new() { Rows = [PlainLine("status-A"), PlainLine("status-B")] };
        TextBuffer editor = new();
        editor.Insert("hi");
        FixedRegionComposer composer = new(editor, status);

        composer.Compose(model);

        // Fixed region should contain both status rows.
        IReadOnlyList<Line> fixed_ = model.FixedRegionRows;
        Assert.Contains(fixed_, r => r.Segments.Count > 0 && r.Segments[0].Text == "status-A");
        Assert.Contains(fixed_, r => r.Segments.Count > 0 && r.Segments[0].Text == "status-B");
    }

    [Fact]
    public void FixedRegionConsumsOnlyNeededRowsWhenUnderCap()
    {
        // cap = 12 (24-row terminal). Input="A" (1 row), status=1 row → fixed=2 rows, not 12.
        RenderModel model = MakeModel(cols: 80, rows: 24);
        StatusLine status = new() { Rows = [PlainLine("status")] };
        TextBuffer editor = new();
        editor.Insert("A");
        FixedRegionComposer composer = new(editor, status);

        composer.Compose(model);

        Assert.Equal(2, model.FixedRegionRows.Count);
    }

    [Fact]
    public void InputScrollsInternallyWhenExceedsCap()
    {
        // cap = 8 (floor, 10-row terminal, 50%=5<8). Status=3 rows → inputAllot=5.
        // Input with many lines → Render(width, 5) returns at most 5 rows.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        StatusLine status = new()
        {
            Rows = [PlainLine("s1"), PlainLine("s2"), PlainLine("s3")]
        };
        TextBuffer editor = new();
        // Insert 8 logical lines (9 visual rows with width=80).
        editor.Insert("line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8");
        FixedRegionComposer composer = new(editor, status);

        composer.Compose(model);

        // Fixed region: inputAllot=5 (cap 8 - 3 status) → at most 5 input rows + 3 status = 8.
        Assert.True(model.FixedRegionRows.Count <= 8,
            $"Fixed region should not exceed cap; got {model.FixedRegionRows.Count}");
        // Status rows still present at the bottom.
        Assert.Equal("s1", model.FixedRegionRows[^3].Segments[0].Text);
        Assert.Equal("s2", model.FixedRegionRows[^2].Segments[0].Text);
        Assert.Equal("s3", model.FixedRegionRows[^1].Segments[0].Text);
        // Caret is visible (editor was given allotted height and scrolled internally).
        Assert.True(model.IsCursorVisible);
        Assert.NotNull(model.EditorCaretLocal);
    }

    [Fact]
    public void FixedRegionComposedBeforeScrollbackSoLiveWindowIsCorrect()
    {
        // The fixed region must be set before scrollback PrePaint so the live-window budget
        // (rows − fixedRegionRows.Count) is correct.
        // Simulate the paint step: compose fixed region → scrollback PrePaint → check budget.
        RenderModel model = MakeModel(cols: 80, rows: 10);
        StatusLine status = new() { Rows = [PlainLine("st")] };
        TextBuffer editor = new();
        editor.Insert("hello");
        FixedRegionComposer composer = new(editor, status);

        // Append some scrollback content.
        ScrollbackModel scrollback = model.Scrollback;
        scrollback.Append(new TextBlock(PlainLine("content1")), model);
        scrollback.Append(new TextBlock(PlainLine("content2")), model);

        // Step 1: compose fixed region (sets FixedRegionRows = 2 rows: input + status).
        composer.Compose(model);
        int fixedCount = model.FixedRegionRows.Count; // should be 2

        // Step 2: scrollback PrePaint (uses rows − fixedCount as live-window cap).
        scrollback.PrePaint(model);

        // Live window should be bounded by rows − fixedCount = 10 − 2 = 8.
        Assert.Equal(fixedCount, model.FixedRegionRows.Count);
        Assert.True(model.LiveWindowRows.Count <= model.Rows - fixedCount,
            $"Live window {model.LiveWindowRows.Count} exceeds budget {model.Rows - fixedCount}");
    }

    // ── 10.5 — Caret in combined frame ───────────────────────────────────────

    [Fact]
    public async Task CaretPositionAccountsForLiveWindowOffset()
    {
        // Drive via LoopEngine to test the full paint step (compose → PrePaint → caret offset).
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        // Append scrollback content to create a live window with rows.
        engine.Post(new AppendScrollbackCommand(PlainLine("scrollback-line")));

        // Type a character into the editor (editing key → consumed, buffer mutated).
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('A')), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // CaretPosition.Row should be ≥ LiveWindowRows.Count (the caret is in the fixed region).
        Assert.NotNull(sink.LastModel?.CaretPosition);
        (int caretRow, _) = sink.LastModel!.CaretPosition!.Value;
        int liveRows = sink.LastModel.LiveWindowRows.Count;
        Assert.True(caretRow >= liveRows,
            $"Caret row {caretRow} should be >= live window row count {liveRows}");
        Assert.True(sink.LastModel.IsCursorVisible);
    }

    // ── 10.5 — Key routing ───────────────────────────────────────────────────

    [Fact]
    public async Task PrintableKeyInsertsIntoEditor()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('H')), Modifiers.None));
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('i')), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // Printable keys are consumed — no KeyPressed events. InputChanged is emitted for each
        // edit that changes the buffer ('H' → "H", 'i' → "Hi" = 2 events).
        Assert.Equal(2, engine.OutboundEvents.Count);
        Assert.All(Enumerable.Range(0, 2), _ =>
        {
            Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? ev));
            Assert.IsType<InputChanged>(ev);
        });

        // The fixed region should reflect the typed text.
        Assert.NotNull(sink.LastModel);
        // Editor's rendered content should include "Hi".
        string fixedText = string.Concat(
            sink.LastModel!.FixedRegionRows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.Contains("Hi", fixedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArrowKeyMovesCaretWithoutEmittingEvent()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('A')), Modifiers.None));
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Left), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // 'A' is consumed and emits InputChanged (text changed); Left moves the caret only and
        // does not emit. Net result: exactly one InputChanged event on the outbound channel.
        Assert.Equal(1, engine.OutboundEvents.Count);
        Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? arrowEv));
        Assert.IsType<InputChanged>(arrowEv);
    }

    [Fact]
    public async Task EnterKeyFallsThroughToOutboundChannel()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // Enter with no modifier and no overlay: emits InputSubmitted (not KeyPressed).
        Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? ev));
        InputSubmitted submitted = Assert.IsType<InputSubmitted>(ev);
        Assert.Equal(string.Empty, submitted.Text); // buffer was empty
    }

    [Fact]
    public async Task TabKeyFallsThroughToOutboundChannel()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Tab), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? ev));
        Assert.IsType<KeyPressed>(ev);
    }

    [Fact]
    public async Task CtrlKeyFallsThrough()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        // Ctrl+C — should fall through, not be inserted.
        engine.InputWriter.TryWrite(new KeyEvent(
            KeyCode.FromRune(new Rune('c')), Modifiers.Ctrl));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        Assert.True(engine.OutboundEvents.TryRead(out _));
    }

    // ── Wrap-boundary caret convention pin (Chunk-A reviewer nit) ────────────

    [Fact]
    public void CaretAtNonNewlineWrapBoundaryReportsNextVisualRowCol0()
    {
        // "AAAA" at width=2 wraps: row0="AA" [0..2), row1="AA" [2..4).
        // CaretIndex=2 is exactly at the wrap boundary (start of row1).
        // Convention: the caret belongs to the NEXT row (row index 1), col 0.
        TextBuffer buf = new();
        buf.Insert("AAAA");
        buf.SetCaretIndexForTest(2); // exactly at non-newline wrap boundary

        RenderResult result = buf.Render(width: 2);
        (int row, int col) = result.CaretPosition;

        Assert.Equal(1, row); // belongs to next row, not the earlier row
        Assert.Equal(0, col); // col 0 — start of that row
    }

    [Fact]
    public void CaretAtNewlineBoundaryBelongsToCurrentRow()
    {
        // "AB\nCD" at width=80. Row0="AB" ends with newline at char index 2.
        // CaretIndex=2 is at the '\n' — caret belongs to row0 (before the newline).
        TextBuffer buf = new();
        buf.Insert("AB\nCD");
        buf.SetCaretIndexForTest(2); // at the '\n'

        RenderResult result = buf.Render(width: 80);
        (int row, _) = result.CaretPosition;

        Assert.Equal(0, row); // belongs to row 0 (before the newline)
    }

    // ── Degenerate: status ≥ cap ─────────────────────────────────────────────

    [Fact]
    public void DegenerateStatusAloneGeCapCursorHidden()
    {
        // 5-row terminal: cap = 5. Status has 5 rows → statusCount >= cap → caret hidden.
        RenderModel model = MakeModel(cols: 80, rows: 5);
        StatusLine status = new()
        {
            Rows = Enumerable.Range(1, 5).Select(i => PlainLine($"s{i}")).ToList()
        };
        TextBuffer editor = new();
        editor.Insert("input");
        FixedRegionComposer composer = new(editor, status);

        composer.Compose(model);

        // Status shown in full (or clamped to terminal rows).
        Assert.True(model.FixedRegionRows.Count <= 5);
        Assert.False(model.IsCursorVisible, "Cursor must be hidden when status >= cap");
        Assert.Null(model.EditorCaretLocal);
    }

    // ── Nit 1 — EditorCaretLocal reset on degenerate transition ─────────────

    [Fact]
    public void DegenerateTransitionResetsEditorCaretLocal()
    {
        // Normal compose on the model so EditorCaretLocal is set non-null.
        RenderModel model = MakeModel(cols: 80, rows: 24);
        StatusLine statusNormal = new() { Rows = [PlainLine("status")] };
        TextBuffer editor = new();
        editor.Insert("hello");
        FixedRegionComposer composerNormal = new(editor, statusNormal);
        composerNormal.Compose(model);

        // Sanity: after a normal compose EditorCaretLocal must be non-null.
        Assert.NotNull(model.EditorCaretLocal);

        // Now compose a degenerate frame (status >= cap) on the SAME model.
        // 5-row terminal: cap = 5 (rows < 8). Status with 5 rows fills the cap.
        model.UpdateSize(80, 5);
        StatusLine statusDegenerate = new()
        {
            Rows = Enumerable.Range(1, 5).Select(i => PlainLine($"s{i}")).ToList()
        };
        FixedRegionComposer composerDegenerate = new(editor, statusDegenerate);
        composerDegenerate.Compose(model);

        // All three caret-related fields must be null/false — no contradiction.
        Assert.Null(model.EditorCaretLocal);
        Assert.Null(model.CaretPosition);
        Assert.False(model.IsCursorVisible, "Cursor must be hidden in degenerate frame");
    }

    // ── Nit 2 — Up/Down history-vs-move routing decision ────────────────────

    [Fact]
    public async Task UpOnFirstVisualRowRecallsHistory()
    {
        // History-nav convention (LoopEngine.cs:~343-347):
        // Up recalls the previous (older) history entry when the caret is on the FIRST visual row.
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        // Populate history with one entry, then clear the buffer so the editor is empty.
        engine.Post(new AddToHistoryCommand("previous-entry"));

        // Caret is on row 0 (first visual row) — single-line empty buffer.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Up), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // Up is consumed. It changes the text (history recall), so InputChanged is emitted.
        // No KeyPressed on the outbound channel.
        Assert.Equal(1, engine.OutboundEvents.Count);
        Assert.True(engine.OutboundEvents.TryRead(out TerminalEvent? upEv));
        Assert.IsType<InputChanged>(upEv);

        // The fixed region should now reflect the recalled history entry.
        Assert.NotNull(sink.LastModel);
        string fixedText = string.Concat(
            sink.LastModel!.FixedRegionRows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.Contains("previous-entry", fixedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpOnMiddleVisualRowMovesCaret()
    {
        // Up on a non-first visual row moves the caret up within the buffer — no history recall.
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        // Populate history (so history recall would be possible if incorrectly triggered).
        engine.Post(new AddToHistoryCommand("should-not-appear"));

        // Type two logical lines — caret ends up on the second visual row (row index 1).
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
        // Enter falls through (not consumed by editor), so type two lines via multiline insert.
        engine.Post(new SetEditorTextCommand("line1\nline2"));

        // Settle to get the model into a known state before the Up key.
        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts1 = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts1.Token);

        // Drain any fall-through events from the Enter key.
        while (engine.OutboundEvents.TryRead(out _)) { }

        // Caret is on the last visual row (row 1 of 2). Press Up → moves caret to row 0.
        engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Up), Modifiers.None));

        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts2 = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts2.Token);

        // Up was consumed (no KeyPressed emitted for Up).
        // Any remaining outbound events must NOT be Up-key events.
        while (engine.OutboundEvents.TryRead(out TerminalEvent? ev))
        {
            if (ev is KeyPressed kp)
                Assert.NotEqual(NamedKey.Up, kp.Key.Code.NamedValue);
        }

        // Buffer text must remain unchanged — no history recall occurred.
        Assert.NotNull(sink.LastModel);
        string fixedText = string.Concat(
            sink.LastModel!.FixedRegionRows.SelectMany(r => r.Segments).Select(s => s.Text));
        Assert.DoesNotContain("should-not-appear", fixedText, StringComparison.Ordinal);
        Assert.Contains("line1", fixedText, StringComparison.Ordinal);
        Assert.Contains("line2", fixedText, StringComparison.Ordinal);
    }

    // ── Loop engine integration (LoopEngine owns editor, status, composer) ───

    [Fact]
    public async Task LoopEngineFixedRegionComposedEveryPaint()
    {
        ConstantSizeSource size = new(80, 24);
        CapturingOutputSink sink = new();
        VirtualClock clock = new();
        using LoopEngine engine = new(size, clock, sink, minFrameInterval: TimeSpan.Zero);

        engine.Post(new DirtyCommand());
        clock.Advance(TimeSpan.FromMilliseconds(100));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        await engine.SettleAsync(cts.Token);

        // After a paint, the fixed region must be non-null (composed with at least 1 input row).
        Assert.NotNull(sink.LastModel);
        Assert.NotEmpty(sink.LastModel!.FixedRegionRows);
    }

    // ── Test infrastructure ───────────────────────────────────────────────────

    /// <summary>
    /// Captures the last <see cref="RenderModel"/> passed to <see cref="Paint"/>.
    /// </summary>
    private sealed class CapturingOutputSink : IOutputSink
    {
        internal RenderModel? LastModel { get; private set; }

        public void Paint(RenderModel model)
        {
            LastModel = model;
        }
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

    /// <summary>Helper command: appends a line to scrollback.</summary>
    private sealed class AppendScrollbackCommand(Line line) : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            model.Scrollback.Append(new TextBlock(line), model);
        }
    }

    /// <summary>Helper command: just marks the model dirty.</summary>
    private sealed class DirtyCommand : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model) => model.MarkDirty();
    }

    /// <summary>
    /// Helper command: calls <see cref="TextBuffer.AddToHistory"/> on the editor so routing
    /// tests can prime the history list without going through submit/Enter (§12).
    /// </summary>
    private sealed class AddToHistoryCommand(string entry) : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            model.FixedRegion.Editor.AddToHistory(entry);
            model.MarkDirty();
        }
    }

    /// <summary>
    /// Helper command: replaces the editor's buffer with <paramref name="text"/> and moves the
    /// caret to the end, so tests can position the caret on a known visual row.
    /// </summary>
    private sealed class SetEditorTextCommand(string text) : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model)
        {
            model.FixedRegion.Editor.SetText(text);
            model.MarkDirty();
        }
    }
}
