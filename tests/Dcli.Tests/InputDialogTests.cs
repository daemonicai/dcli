using System.Text;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;
using Dcli.Testing;
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
        public void EmitRestoreSequence() { }
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
        IReadOnlyList<Line>? prompt = null,
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
        InputDialog dialog = new([prompt], @default: null, isSecret: false);
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
        InputDialog dialog = new([prompt], @default: null, isSecret: false);
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
        InputDialog dialog = new([prompt], @default: null, isSecret: false);
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

    // ── §4 Secret-default masking ─────────────────────────────────────────────

    // Repro test: was the "seeded default leaks clear-text" bug real?
    // Expected: PASS — the existing MaskRows path already masks unconditionally when
    // _isSecret=true, so the seeded default never leaks on first paint. The spec/design
    // comment about this being a bug is inaccurate for the current codebase.
    [Fact]
    public void SecretDefaultIsMaskedOnFirstPaintRegressionGuard()
    {
        InputDialog dlg = new(prompt: null, @default: "secret123", isSecret: true);
        IReadOnlyList<Line> rows = dlg.Render(width: 40);
        string allText = string.Concat(rows.SelectMany(l => l.Segments).Select(s => s.Text));
        Assert.DoesNotContain("secret123", allText, StringComparison.Ordinal);
        Assert.Contains("•", allText, StringComparison.Ordinal);
    }

    // Spec scenario: "Secret default is masked before first edit"
    // IsSecret=true, Default="hunter2", no edits → render shows bullets, not "hunter2".
    [Fact]
    public void SecretDefaultMaskedBeforeFirstEdit()
    {
        InputDialog dlg = new(prompt: null, @default: "hunter2", isSecret: true);
        IReadOnlyList<Line> rows = dlg.Render(width: 40);
        string allText = string.Concat(rows.SelectMany(l => l.Segments).Select(s => s.Text));
        Assert.DoesNotContain("hunter2", allText, StringComparison.Ordinal);
        Assert.Contains("•", allText, StringComparison.Ordinal);
        // 7 ASCII chars → 7 bullets.
        int bulletCount = allText.Count(c => c == '•');
        Assert.Equal(7, bulletCount);
    }

    // Spec scenario: "Secret default reveals real text on submit"
    // Text property returns the unmasked buffer content regardless of _isSecret.
    [Fact]
    public void SecretDefaultTextPropertyReturnsRealDefault()
    {
        InputDialog dlg = new(prompt: null, @default: "hunter2", isSecret: true);
        Assert.Equal("hunter2", dlg.Text);
    }

    // Spec scenario: "Secret + default + one edit (and possibly revert)"
    // _userEdited is sticky: once set it stays true even if the buffer content reverts to
    // equal the original default. Masking still applies (the _isSecret path runs regardless),
    // but the bullet count reflects the CURRENT buffer width, not the original default's width.
    [Fact]
    public void SecretDefaultOneEditThenRevertUserEditedIsSticky()
    {
        InputDialog dlg = new(prompt: null, @default: "abc", isSecret: true);

        // Step 1: render before any edit — 3 bullets for "abc".
        IReadOnlyList<Line> rows1 = dlg.Render(width: 40);
        string text1 = string.Concat(rows1.SelectMany(l => l.Segments).Select(s => s.Text));
        Assert.DoesNotContain("abc", text1, StringComparison.Ordinal);
        int bullets1 = text1.Count(c => c == '•');
        Assert.Equal(3, bullets1);
        Assert.False(dlg.UserEdited);

        // Step 2: insert 'x' → buffer is "abcx", _userEdited=true.
        dlg.HandleKey(new KeyEvent(KeyCode.FromRune(new Rune('x')), Modifiers.None));
        IReadOnlyList<Line> rows2 = dlg.Render(width: 40);
        string text2 = string.Concat(rows2.SelectMany(l => l.Segments).Select(s => s.Text));
        Assert.DoesNotContain("abcx", text2, StringComparison.Ordinal);
        int bullets2 = text2.Count(c => c == '•');
        Assert.Equal(4, bullets2); // "abcx" = 4 display columns
        Assert.True(dlg.UserEdited);

        // Step 3: Backspace twice → buffer becomes "ab", _userEdited stays true (sticky).
        dlg.HandleKey(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));
        dlg.HandleKey(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));
        IReadOnlyList<Line> rows3 = dlg.Render(width: 40);
        string text3 = string.Concat(rows3.SelectMany(l => l.Segments).Select(s => s.Text));
        Assert.DoesNotContain("ab", text3, StringComparison.Ordinal);
        int bullets3 = text3.Count(c => c == '•');
        Assert.Equal(2, bullets3); // "ab" = 2 display columns
        // _userEdited remains true even though buffer width is now less than the original default.
        Assert.True(dlg.UserEdited);
    }

    // Spec scenario: "Non-secret default renders as plain text" (regression guard, §4.5)
    [Fact]
    public void NonSecretDefaultRendersAsPlainText()
    {
        InputDialog dlg = new(prompt: null, @default: "hello", isSecret: false);
        IReadOnlyList<Line> rows = dlg.Render(width: 40);
        string allText = string.Concat(rows.SelectMany(l => l.Segments).Select(s => s.Text));
        Assert.Contains("hello", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("•", allText, StringComparison.Ordinal);
    }

    // ── §3.4 — Multi-line Prompt on InputRequest renders all lines above input field ─

    // Spec scenario: "Multi-line preamble renders all lines above the widget"

    /// <summary>
    /// §3.4 — An <see cref="InputRequest"/> with a 3-line prompt renders all three preamble lines
    /// above the input field (the last FixedRegionRow for the overlay).
    /// </summary>
    [Fact]
    public async Task InputRequestMultiLinePromptRendersAllLinesAboveInputField()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 12 });

        InputRequest req = new(
            (IReadOnlyList<Line>)[
                new Line([new Segment("Enter your name")]),
                new Line([new Segment("Max 20 chars")]),
                new Line([new Segment("Press Enter to submit")]),
            ]);

        Task<DialogResult<string>> dialogTask = harness.Terminal.InputAsync(req);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        Assert.Equal(OverlayKind.Input, snap.Overlay.Kind);

        int idxLine1 = FindRowIndexContaining(snap.FixedRegionRows, "Enter your name");
        int idxLine2 = FindRowIndexContaining(snap.FixedRegionRows, "Max 20 chars");
        int idxLine3 = FindRowIndexContaining(snap.FixedRegionRows, "Press Enter to submit");

        Assert.True(idxLine1 >= 0, "Prompt line 1 'Enter your name' not found in FixedRegionRows");
        Assert.True(idxLine2 >= 0, "Prompt line 2 'Max 20 chars' not found");
        Assert.True(idxLine3 >= 0, "Prompt line 3 'Press Enter to submit' not found");
        Assert.True(idxLine1 < idxLine2, $"Prompt lines out of order: {idxLine1},{idxLine2}");
        Assert.True(idxLine2 < idxLine3, $"Prompt lines out of order: {idxLine2},{idxLine3}");

        // The input field row is after the last prompt line (the overlay has rows beyond preamble).
        Assert.True(snap.Overlay.VisibleRowCount > 3,
            $"Expected more than 3 visible overlay rows (3 prompt + at least 1 input); got {snap.Overlay.VisibleRowCount}");

        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await dialogTask;
    }

    // ── §3.7 — Oversized preamble on InputRequest truncates and keeps widget visible ─

    /// <summary>
    /// §3.7 (input variant) — A preamble taller than the overlay budget truncates, and the input
    /// overlay remains active (widget remains usable). Caret-in-frame is NOT asserted.
    /// </summary>
    [Fact]
    public async Task InputRequestOversizedPreambleTruncatesAndKeepsWidgetVisible()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 10 });

        IReadOnlyList<Line> bigPrompt = Enumerable.Range(1, 30)
            .Select(i => new Line([new Segment($"prompt line {i}")]))
            .ToList();

        InputRequest req = new(bigPrompt);

        Task<DialogResult<string>> dialogTask = harness.Terminal.InputAsync(req);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;

        // The overlay must still be active.
        Assert.Equal(OverlayKind.Input, snap.Overlay.Kind);

        // The fixed-region row count must not exceed the terminal height.
        Assert.True(snap.FixedRegionRows.Count <= snap.Size.Rows,
            $"FixedRegionRows ({snap.FixedRegionRows.Count}) exceeded terminal rows ({snap.Size.Rows})");

        // The overlay visible row count must be positive.
        Assert.True(snap.Overlay.VisibleRowCount > 0,
            "Overlay must render at least one row even with an oversized preamble");

        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await dialogTask;
    }

    // ── §4 PasteEvent routing ─────────────────────────────────────────────────

    // 4.4 — Paste inserts text at the caret in the base editor and the caret advances.
    [Fact]
    public async Task PasteInsertsTextAtCaretInBaseEditor()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            engine.InputWriter.TryWrite(new PasteEvent("hello"));
            await SettleAsync(engine, clock);

            Assert.NotNull(sink.LastModel);
            TextBuffer editor = sink.LastModel.FixedRegion.Editor;
            Assert.Equal("hello", editor.Text);
            // Caret must have advanced to the end of the inserted text.
            Assert.Equal("hello".Length, editor.CaretIndex);
        }
        finally { engine.Dispose(); }
    }

    // 4.5 — Paste of text wider than the available width wraps and caret lands correctly.
    [Fact]
    public async Task PasteWiderThanWidthWrapsAndCaretLandsAtEnd()
    {
        // Use a narrow width so a long paste forces wrapping.
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine(cols: 10);
        try
        {
            // 25 chars — more than 2 rows at width=10.
            string payload = "ABCDEFGHIJKLMNOPQRSTUVWXY";
            engine.InputWriter.TryWrite(new PasteEvent(payload));
            await SettleAsync(engine, clock);

            Assert.NotNull(sink.LastModel);
            TextBuffer editor = sink.LastModel.FixedRegion.Editor;
            Assert.Equal(payload, editor.Text);
            Assert.Equal(payload.Length, editor.CaretIndex);

            // Render at width=10: must produce more than one visual row.
            IReadOnlyList<Line> rows = editor.Render(width: 10, allottedHeight: 10).VisibleRows;
            Assert.True(rows.Count > 1,
                $"Expected wrapping to produce >1 row for {payload.Length}-char paste at width 10; got {rows.Count}");

            // The caret visual position reported by the model must be in the last row.
            (int caretRow, _) = sink.LastModel.CaretPosition!.Value;
            // The fixed region's rows start at 0 in the frame; the editor occupies the last rows.
            // We just need the caret row to be positive (beyond the first row) — confirming wrap.
            Assert.True(caretRow > 0, $"Caret row should be > 0 after wrap; was {caretRow}");
        }
        finally { engine.Dispose(); }
    }

    // 4.6 — Paste as first interaction on IsSecret=true+Default flips masking and Submit returns
    //        the real edited buffer (not the seeded default).
    [Fact]
    public async Task PasteAsFirstInteractionOnSecretDefaultFlipsMaskingAndSubmitReturnsBuffer()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            Task<DialogResult<string>> task = PostInputDialog(engine, @default: "original", isSecret: true);
            await SettleAsync(engine, clock);

            // Before paste: default is seeded; _userEdited=false → masking uses default length (8 bullets).
            RenderModel? beforeModel = sink.LastModel;
            Assert.NotNull(beforeModel);

            // Paste as the first user interaction.
            engine.InputWriter.TryWrite(new PasteEvent("newpass"));
            await SettleAsync(engine, clock);

            // After paste: _userEdited=true → masking uses buffer width (original 8 + new 7 = 15 chars).
            RenderModel? afterModel = sink.LastModel;
            Assert.NotNull(afterModel);
            string renderedText = string.Concat(
                afterModel.FixedRegionRows.SelectMany(l => l.Segments).Select(s => s.Text));

            // The rendered overlay must show bullets, not the clear-text content.
            Assert.DoesNotContain("original", renderedText, StringComparison.Ordinal);
            Assert.DoesNotContain("newpass", renderedText, StringComparison.Ordinal);
            Assert.Contains("•", renderedText, StringComparison.Ordinal);

            // The bullets must reflect the buffer content ("originalnewpass" = 15 chars → 15 bullets).
            int bulletCount = renderedText.Count(c => c == '•');
            Assert.Equal("original".Length + "newpass".Length, bulletCount);

            // Submit must return the actual buffer text (not the original default).
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
            await SettleAsync(engine, clock);
            DialogResult<string> result = await task;

            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal("originalnewpass", result.Value);
        }
        finally { engine.Dispose(); }
    }

    // 4.7 — Paste is consumed by an active modal dialog and does NOT leak to the base editor.
    [Fact]
    public async Task PasteConsumedByModalDialogDoesNotLeakToBaseEditor()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            // Type something into the base editor first so we have a baseline.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('x')), Modifiers.None));
            await SettleAsync(engine, clock);
            Assert.Equal("x", sink.LastModel!.FixedRegion.Editor.Text);

            // Open a modal select dialog.
            Dialog selectDialog = new(multiSelect: false, modal: true);
            selectDialog.List.SetItems([PlainLine("A"), PlainLine("B")]);
            TaskCompletionSource<int> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Post(new OpenDialogCommand(
                selectDialog,
                () => tcs.TrySetResult(selectDialog.CloseRequest == OverlayCloseKind.Submit ? 1 : 0),
                () => tcs.TrySetResult(-1)));
            await SettleAsync(engine, clock);

            // Paste while modal dialog is active.
            engine.InputWriter.TryWrite(new PasteEvent("should-not-appear"));
            await SettleAsync(engine, clock);

            // The base editor must be unchanged — paste was consumed by the modal dialog.
            Assert.Equal("x", sink.LastModel!.FixedRegion.Editor.Text);

            // Dismiss the dialog.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            await tcs.Task;
        }
        finally { engine.Dispose(); }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static int FindRowIndexContaining(IReadOnlyList<Line> rows, string needle)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Segments.Any(s => s.Text.Contains(needle, StringComparison.Ordinal)))
                return i;
        }
        return -1;
    }
}
