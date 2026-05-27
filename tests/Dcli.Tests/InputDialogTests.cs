using System.Text;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for §12 Chunk B: <see cref="InputDialog"/>, <see cref="InputRequest"/>,
/// <see cref="IModalOverlay"/> generalisation, and seam #1 (cursor in overlay).
/// Tests drive the machinery at the <see cref="LoopEngine"/> level (no real terminal needed).
/// </summary>
public sealed class InputDialogTests
{
    // ── Test infrastructure (mirrored from DialogSelectionTests) ──────────────

    private sealed class ConstantSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }

    private sealed class CapturingOutputSink : IOutputSink
    {
        internal RenderModel? LastModel { get; private set; }
        public void Paint(RenderModel model) { LastModel = model; }
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

    private static (LoopEngine Engine, VirtualClock Clock, CapturingOutputSink Sink) CreateEngine(
        int cols = 80, int rows = 24)
    {
        VirtualClock clock = new(TimeSpan.Zero);
        CapturingOutputSink sink = new();
        LoopEngine engine = new(
            new ConstantSizeSource(cols, rows),
            clock,
            sink,
            minFrameInterval: TimeSpan.Zero);
        return (engine, clock, sink);
    }

    private static Line PlainLine(string text) => new([new Segment(text)]);

    private static Task SettleAsync(LoopEngine engine, VirtualClock clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(10));
        return engine.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ── Helper: open an InputDialog via OpenDialogCommand ────────────────────

    private static Task<DialogResult<string>> PostInputDialog(
        LoopEngine engine,
        Line? prompt = null,
        string? @default = null,
        bool isSecret = false,
        CancellationToken ct = default)
    {
        InputDialog dialog = new(prompt, @default, isSecret);

        TaskCompletionSource<DialogResult<string>> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        CancellationTokenRegistration[] regHolder = new CancellationTokenRegistration[1];

        Action completion = () =>
        {
            DialogResult<string> result = dialog.CloseRequest == OverlayCloseKind.Submit
                ? new DialogResult<string>(DialogOutcome.Submitted, dialog.Text)
                : new DialogResult<string>(DialogOutcome.Cancelled, default!);
            tcs.TrySetResult(result);
            regHolder[0].Dispose();
        };

        Action reject = () =>
        {
            tcs.TrySetException(new InvalidOperationException("A dialog is already active."));
            regHolder[0].Dispose();
        };

        if (ct.CanBeCanceled)
        {
            Action cancelCompletion = () =>
            {
                tcs.TrySetResult(new DialogResult<string>(DialogOutcome.Cancelled, default!));
                regHolder[0].Dispose();
            };
            regHolder[0] = ct.Register(
                () => engine.Post(new CancelDialogCommand(dialog, cancelCompletion)),
                useSynchronizationContext: false);
        }

        engine.Post(new OpenDialogCommand(dialog, completion, reject));
        return tcs.Task;
    }

    // ── Type text + Enter → Submitted ────────────────────────────────────────

    [Fact]
    public async Task TypeHelloThenEnterReturnsSubmittedHello()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine);

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('h')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('e')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('l')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('l')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('o')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<string> result = await task;

            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal("hello", result.Value);
        }
        finally { engine.Dispose(); }
    }

    // ── Seeded Default + Enter → Submitted with default text ─────────────────

    [Fact]
    public async Task SeededDefaultPlusEnterReturnsSubmittedDefault()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine, @default: "preset");
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<string> result = await task;

            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal("preset", result.Value);
        }
        finally { engine.Dispose(); }
    }

    // ── IsSecret: rendered rows masked, result is real text ──────────────────

    [Fact]
    public async Task IsSecretMasksRenderedRowsButResultIsRealText()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine, isSecret: true);

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('p')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('w')), Modifiers.None));

            // Settle so the frame is painted and the sink captures a model.
            await SettleAsync(engine, clock);

            // The rendered overlay rows must contain the mask glyph, not the plaintext.
            RenderModel? model = sink.LastModel;
            Assert.NotNull(model);
            IReadOnlyList<Line> fixedRows = model.FixedRegionRows;
            string renderedText = string.Concat(fixedRows.SelectMany(r => r.Segments.Select(s => s.Text)));
            Assert.Contains("•", renderedText, StringComparison.Ordinal);
            Assert.DoesNotContain("pw", renderedText, StringComparison.Ordinal);

            // Submit and verify the real text comes through.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
            await SettleAsync(engine, clock);
            DialogResult<string> result = await task;

            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal("pw", result.Value);
        }
        finally { engine.Dispose(); }
    }

    // ── Escape → Cancelled; overlay cleared ──────────────────────────────────

    [Fact]
    public async Task EscapeReturnsCancelledAndOverlayCleared()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine);
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<string> result = await task;

            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
            Assert.Null(sink.LastModel?.ActiveOverlay);
        }
        finally { engine.Dispose(); }
    }

    // ── CancellationToken cancel → Cancelled; overlay cleared ─────────────────

    [Fact]
    public async Task CancellationTokenCancelReturnsCancelledAndOverlayCleared()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            using CancellationTokenSource cts = new();

            Task<DialogResult<string>> task = PostInputDialog(engine, ct: cts.Token);

            // Let the dialog open.
            await SettleAsync(engine, clock);
            Assert.IsType<InputDialog>(sink.LastModel?.ActiveOverlay);

            // Cancel the token.
            await cts.CancelAsync();
            await SettleAsync(engine, clock);

            DialogResult<string> result = await task;

            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
            Assert.Null(sink.LastModel?.ActiveOverlay);
        }
        finally { engine.Dispose(); }
    }

    // ── Backspace edits the buffer ────────────────────────────────────────────

    [Fact]
    public async Task BackspaceDeletesLastCharacter()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine);

            // Type "abc", Backspace → buffer should be "ab"
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('a')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('b')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('c')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<string> result = await task;

            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal("ab", result.Value);
        }
        finally { engine.Dispose(); }
    }

    // ── Seam #1: cursor visible at overlay caret; not hidden ─────────────────

    [Fact]
    public async Task InputDialogCursorIsVisibleInOverlayRegion()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine(cols: 80, rows: 24);
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine);

            // Settle so a frame paints.
            await SettleAsync(engine, clock);

            RenderModel? model = sink.LastModel;
            Assert.NotNull(model);

            // The cursor must be visible — InputDialog owns it, not the main editor.
            Assert.True(model.IsCursorVisible, "Expected cursor visible for InputDialog");

            // The caret must be in the overlay region (above the input rows).
            // With no text, the overlay occupies the topmost rows of the fixed region,
            // so CaretPosition.Row should be 0 (first row).
            Assert.NotNull(model.CaretPosition);
            Assert.Equal(0, model.CaretPosition!.Value.Row);

            // Clean up.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            await task;
        }
        finally { engine.Dispose(); }
    }

    // ── Seam #1 contrast: select Dialog hides cursor ─────────────────────────

    [Fact]
    public async Task SelectDialogHidesCursorContrastWithInputDialog()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            // Open a select dialog (HidesCursor=true) and verify cursor is hidden.
            Dialog selectDialog = new(multiSelect: false, modal: true);
            selectDialog.List.SetItems([PlainLine("A"), PlainLine("B")]);

            TaskCompletionSource<int> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Post(new OpenDialogCommand(
                selectDialog,
                () => tcs.TrySetResult(selectDialog.CloseRequest == OverlayCloseKind.Submit ? 1 : 0),
                () => tcs.TrySetResult(-1)));

            await SettleAsync(engine, clock);

            RenderModel? model = sink.LastModel;
            Assert.NotNull(model);
            Assert.False(model.IsCursorVisible, "Select dialog must hide the cursor");

            // Dismiss.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            await tcs.Task;
        }
        finally { engine.Dispose(); }
    }

    // ── Reject: InputDialog while select Dialog active ────────────────────────

    [Fact]
    public async Task OpenInputDialogWhileSelectDialogActiveThrowsInvalidOperation()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            // Open a select dialog first.
            Dialog selectDialog = new(multiSelect: false, modal: true);
            selectDialog.List.SetItems([PlainLine("A")]);

            TaskCompletionSource<int> firstTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Post(new OpenDialogCommand(
                selectDialog,
                () => firstTcs.TrySetResult(0),
                () => firstTcs.TrySetException(new InvalidOperationException("rejected"))));

            await SettleAsync(engine, clock);
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Now try to open an InputDialog — must fault.
            Task<DialogResult<string>> inputTask = PostInputDialog(engine);
            await SettleAsync(engine, clock);

            InvalidOperationException ex =
                await Assert.ThrowsAsync<InvalidOperationException>(() => inputTask);
            Assert.Contains("already active", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The first overlay must still be active.
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Clean up.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            await firstTcs.Task;
        }
        finally { engine.Dispose(); }
    }

    // ── Reject: select Dialog while InputDialog active ────────────────────────

    [Fact]
    public async Task OpenSelectDialogWhileInputDialogActiveThrowsInvalidOperation()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            Task<DialogResult<string>> inputTask = PostInputDialog(engine);
            await SettleAsync(engine, clock);
            Assert.IsType<InputDialog>(sink.LastModel?.ActiveOverlay);

            // Try to open a select Dialog — must fault.
            Dialog selectDialog = new(multiSelect: false, modal: true);
            selectDialog.List.SetItems([PlainLine("X")]);

            TaskCompletionSource<int> secondTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Post(new OpenDialogCommand(
                selectDialog,
                () => secondTcs.TrySetResult(0),
                () => secondTcs.TrySetException(new InvalidOperationException("A dialog is already active."))));

            await SettleAsync(engine, clock);

            InvalidOperationException ex =
                await Assert.ThrowsAsync<InvalidOperationException>(() => secondTcs.Task);
            Assert.Contains("already active", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The InputDialog must still be active.
            Assert.IsType<InputDialog>(sink.LastModel?.ActiveOverlay);

            // Clean up.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            DialogResult<string> result = await inputTask;
            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── InputDialog render: prompt row is prepended ───────────────────────────

    [Fact]
    public void InputDialogRenderPrependsPromptRow()
    {
        Line prompt = PlainLine("Enter value:");
        InputDialog dialog = new(prompt, @default: null, isSecret: false);
        dialog.MaxRows = 10;

        IReadOnlyList<Line> rows = dialog.Render(80);

        Assert.True(rows.Count >= 1);
        Assert.Same(prompt, rows[0]);
    }

    // ── InputDialog: MaxRows limits total output ──────────────────────────────

    [Fact]
    public void InputDialogRenderNeverExceedsMaxRows()
    {
        Line prompt = PlainLine("Prompt:");
        InputDialog dialog = new(prompt, @default: null, isSecret: false);
        dialog.MaxRows = 2;

        IReadOnlyList<Line> rows = dialog.Render(80);

        Assert.True(rows.Count <= 2, $"Rows {rows.Count} exceeded MaxRows 2");
    }

    // ── InputDialog: CaretInOverlay null before first render ──────────────────

    [Fact]
    public void InputDialogCaretInOverlayNullBeforeFirstRender()
    {
        InputDialog dialog = new(prompt: null, @default: null, isSecret: false);
        Assert.Null(dialog.CaretInOverlay);
    }

    // ── InputDialog: CaretInOverlay set after render; offset by prompt row ────

    [Fact]
    public void InputDialogCaretInOverlayOffsetByPromptRow()
    {
        Line prompt = PlainLine("Prompt:");
        InputDialog dialog = new(prompt, @default: null, isSecret: false);
        dialog.MaxRows = 10;

        dialog.Render(80);

        (int Row, int Col)? caret = dialog.CaretInOverlay;
        Assert.NotNull(caret);
        // With a prompt row, caret must be on row 1 (or higher), not row 0 (which is the prompt).
        Assert.True(caret!.Value.Row >= 1, $"Expected caret row ≥ 1 with prompt, got {caret.Value.Row}");
    }

    // ── Ctrl+C is NOT inserted as text (modifier gate) ───────────────────────

    [Fact]
    public async Task CtrlCIsNotInsertedAsTextAndIsConsumedByModalCatchAll()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine);

            // Type 'a', then Ctrl+C (which must NOT insert 'c'), then Enter.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('a')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('c')), Modifiers.Ctrl));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<string> result = await task;

            // Result must be "a", not "ac" — the Ctrl+C was consumed silently.
            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal("a", result.Value);
        }
        finally { engine.Dispose(); }
    }

    // ── InputDialog: IsDismissed transitions ─────────────────────────────────

    [Fact]
    public void InputDialogIsDismissedAfterEnter()
    {
        InputDialog dialog = new(prompt: null, @default: null, isSecret: false);
        Assert.False(dialog.IsDismissed);

        dialog.HandleKey(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

        Assert.True(dialog.IsDismissed);
        Assert.Equal(OverlayCloseKind.Submit, dialog.CloseRequest);
    }

    [Fact]
    public void InputDialogIsDismissedAfterEscape()
    {
        InputDialog dialog = new(prompt: null, @default: null, isSecret: false);
        dialog.HandleKey(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));

        Assert.True(dialog.IsDismissed);
        Assert.Equal(OverlayCloseKind.Cancel, dialog.CloseRequest);
    }
}
