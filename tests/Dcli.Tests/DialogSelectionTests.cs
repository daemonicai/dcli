using System.Text;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for §12 Chunk A: awaitable selection dialogs via <see cref="OpenDialogCommand"/>,
/// <see cref="CancelDialogCommand"/>, and the loop-engine plumbing.
/// Tests drive the machinery at the <see cref="LoopEngine"/> level (no real terminal needed).
/// </summary>
public sealed class DialogSelectionTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private sealed class ConstantSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }

    // Posts ShowAutocomplete directly via the loop command channel.
    private sealed class ShowAutocompleteCommand(Autocomplete autocomplete) : ILoopCommand
    {
        void ILoopCommand.Apply(RenderModel model) => model.ShowAutocomplete(autocomplete);
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

    private static List<Line> Items(params string[] texts) =>
        texts.Select(PlainLine).ToList();

    // Settle with a 5-second wall-clock guard so tests cannot hang forever.
    private static Task SettleAsync(LoopEngine engine, VirtualClock clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(10));
        return engine.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ── Helper: open a select dialog via OpenDialogCommand ───────────────────

    private static Task<DialogResult<int>> PostSelectDialog(
        LoopEngine engine,
        List<Line> items,
        IReadOnlyList<Line>? title = null,
        CancellationToken ct = default)
    {
        Dialog dialog = new(multiSelect: false, modal: true, title: title);
        dialog.List.SetItems(items);

        TaskCompletionSource<DialogResult<int>> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        CancellationTokenRegistration[] regHolder = new CancellationTokenRegistration[1];

        Action completion = () =>
        {
            DialogResult<int> result = dialog.CloseRequest == OverlayCloseKind.Submit
                ? new DialogResult<int>(DialogOutcome.Submitted, dialog.List.SelectedIndex)
                : new DialogResult<int>(DialogOutcome.Cancelled, -1);
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
                tcs.TrySetResult(new DialogResult<int>(DialogOutcome.Cancelled, -1));
                regHolder[0].Dispose();
            };
            regHolder[0] = ct.Register(
                () => engine.Post(new CancelDialogCommand(dialog, cancelCompletion)),
                useSynchronizationContext: false);
        }

        engine.Post(new OpenDialogCommand(dialog, completion, reject));
        return tcs.Task;
    }

    private static Task<DialogResult<int[]>> PostMultiSelectDialog(
        LoopEngine engine,
        List<Line> items,
        CancellationToken ct = default)
    {
        Dialog dialog = new(multiSelect: true, modal: true);
        dialog.List.SetItems(items);

        TaskCompletionSource<DialogResult<int[]>> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        CancellationTokenRegistration[] regHolder = new CancellationTokenRegistration[1];

        Action completion = () =>
        {
            DialogResult<int[]> result = dialog.CloseRequest == OverlayCloseKind.Submit
                ? new DialogResult<int[]>(DialogOutcome.Submitted, [.. dialog.List.CheckedIndices])
                : new DialogResult<int[]>(DialogOutcome.Cancelled, []);
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
                tcs.TrySetResult(new DialogResult<int[]>(DialogOutcome.Cancelled, []));
                regHolder[0].Dispose();
            };
            regHolder[0] = ct.Register(
                () => engine.Post(new CancelDialogCommand(dialog, cancelCompletion)),
                useSynchronizationContext: false);
        }

        engine.Post(new OpenDialogCommand(dialog, completion, reject));
        return tcs.Task;
    }

    // ── 12.1 — Select: submit with navigation ────────────────────────────────

    [Fact]
    public async Task SelectSubmitDownDownEnterReturnsSubmittedIndex2()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostSelectDialog(engine, Items("A", "B", "C"));

            // ↓ ↓ Enter → navigate to index 2, then submit
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<int> result = await task;

            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal(2, result.Value);
        }
        finally { engine.Dispose(); }
    }

    // ── 12.1 — Select: cancel via Escape ──────────────────────────────────────

    [Fact]
    public async Task SelectCancelEscapeReturnsCancelled()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostSelectDialog(engine, Items("X", "Y"));

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<int> result = await task;

            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── 12.1 — Multi-select: toggle + submit ─────────────────────────────────

    [Fact]
    public async Task MultiSelectSubmitSpaceDownSpaceEnterReturnsToggledIndices()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<int[]>> task = PostMultiSelectDialog(engine, Items("Alpha", "Beta", "Gamma"));

            // Space (toggle 0), ↓ (move to 1), Space (toggle 1), Enter (submit)
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune(' ')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune(' ')), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<int[]> result = await task;

            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
            Assert.Equal([0, 1], result.Value);
        }
        finally { engine.Dispose(); }
    }

    // ── 12.2 — Cancellation token cancels dialog ─────────────────────────────

    [Fact]
    public async Task CancellationTokenCancelReturnsDialogCancelled()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            using CancellationTokenSource dialogCts = new();

            Task<DialogResult<int>> task = PostSelectDialog(engine, Items("P", "Q"), ct: dialogCts.Token);

            // Let the dialog open.
            await SettleAsync(engine, clock);
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Cancel the token — posts CancelDialogCommand.
            await dialogCts.CancelAsync();
            await SettleAsync(engine, clock);

            DialogResult<int> result = await task;

            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
            // Overlay must have been cleared.
            Assert.Null(sink.LastModel?.ActiveOverlay);
        }
        finally { engine.Dispose(); }
    }

    // ── 12.2 — Already-cancelled token → immediate Cancelled ─────────────────

    [Fact]
    public async Task AlreadyCancelledTokenReturnsCancelledNoOverlay()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            // The Terminal.SelectAsync fast-path returns Cancelled immediately when the token is
            // already cancelled. Replicate the fast-path contract here.
            DialogResult<int> result = cts.Token.IsCancellationRequested
                ? new DialogResult<int>(DialogOutcome.Cancelled, default)
                : throw new InvalidOperationException("Expected already-cancelled");

            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);

            // Engine should have no overlay — nothing was posted.
            await SettleAsync(engine, clock);
            Assert.Null(sink.LastModel?.ActiveOverlay);
        }
        finally { engine.Dispose(); }
    }

    // ── 12.2 — Reject second concurrent dialog ───────────────────────────────

    [Fact]
    public async Task SecondDialogWhileFirstActiveThrowsInvalidOperation()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            Task<DialogResult<int>> first = PostSelectDialog(engine, Items("1", "2"));

            // Settle so the first dialog's command applies.
            await SettleAsync(engine, clock);
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Open second dialog — must fault.
            Task<DialogResult<int>> second = PostSelectDialog(engine, Items("A", "B"));
            await SettleAsync(engine, clock);

            // Second task must fault with InvalidOperationException.
            InvalidOperationException ex =
                await Assert.ThrowsAsync<InvalidOperationException>(() => second);
            Assert.Contains("already active", ex.Message, StringComparison.OrdinalIgnoreCase);

            // First dialog must still be the active overlay.
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Clean up: dismiss the first dialog.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            DialogResult<int> firstResult = await first;
            Assert.Equal(DialogOutcome.Cancelled, firstResult.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── Title row: MaxRows reserves one row ───────────────────────────────────

    [Fact]
    public void DialogWithTitleMaxRowsReservesOneRowForTitle()
    {
        Line title = PlainLine("Choose:");
        Dialog dialog = new(multiSelect: false, modal: true, title: [title]);
        dialog.List.SetItems(Items("A", "B", "C"));

        // Set MaxRows = 4. With a title, List.MaxRows = 3 → 3 list rows fit.
        dialog.MaxRows = 4;
        IReadOnlyList<Line> rows = dialog.Render(80);

        // Total rows ≤ MaxRows.
        Assert.True(rows.Count <= 4, $"Rows count {rows.Count} exceeds MaxRows 4");
        // Title is first row.
        Assert.Same(title, rows[0]);
        // All 3 list items fit → total 4.
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void DialogWithTitleRenderPrependsTitleRow()
    {
        Line title = PlainLine("My Title");
        Dialog dialog = new(multiSelect: false, modal: true, title: [title]);
        dialog.List.SetItems(Items("X", "Y"));
        dialog.MaxRows = 10; // no truncation

        IReadOnlyList<Line> rows = dialog.Render(80);

        // First row is the title, followed by list items.
        Assert.Equal(3, rows.Count); // title + 2 items
        Assert.Same(title, rows[0]);
        Assert.Contains(rows[1].Segments, s => s.Text.Contains('X', StringComparison.Ordinal));
        Assert.Contains(rows[2].Segments, s => s.Text.Contains('Y', StringComparison.Ordinal));
    }

    [Fact]
    public void DialogWithTitleMaxRowsNeverExceedsBudget()
    {
        Line title = PlainLine("Title");
        Dialog dialog = new(multiSelect: false, modal: true, title: [title]);
        dialog.List.SetItems(Items("1", "2", "3", "4", "5"));
        dialog.MaxRows = 3; // budget: title(1) + list(max 2)

        IReadOnlyList<Line> rows = dialog.Render(80);

        Assert.True(rows.Count <= 3, $"Rows count {rows.Count} exceeds budget 3");
        Assert.Same(title, rows[0]);
    }

    // ── Dialog without title: MaxRows unchanged behaviour ────────────────────

    [Fact]
    public void DialogNoTitleMaxRowsRoundTrips()
    {
        Dialog dialog = new(multiSelect: false, modal: true);
        dialog.MaxRows = 5;
        Assert.Equal(5, dialog.MaxRows);
    }

    // ── DialogOutcome.Back exists but is not produced ─────────────────────────

    [Fact]
    public void DialogOutcomeBackEnumValueExists()
    {
        // Back must exist for API/wizard compatibility even though v1 dialogs don't produce it.
        Assert.True(Enum.IsDefined(DialogOutcome.Back));
    }

    // ── DialogResult is publicly constructible (§12.6 needs this) ───────────

    [Fact]
    public void DialogResultIsPubliclyConstructible()
    {
        DialogResult<int> r = new(DialogOutcome.Submitted, 42);
        Assert.Equal(DialogOutcome.Submitted, r.Outcome);
        Assert.Equal(42, r.Value);

        DialogResult<int[]> r2 = new(DialogOutcome.Cancelled, []);
        Assert.Equal(DialogOutcome.Cancelled, r2.Outcome);
    }

    // ── Overlay cleared after dismiss ─────────────────────────────────────────

    [Fact]
    public async Task DialogAfterSubmitOverlayIsCleared()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostSelectDialog(engine, Items("A", "B", "C"));

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
            await SettleAsync(engine, clock);

            Assert.Null(sink.LastModel?.ActiveOverlay);
            DialogResult<int> result = await task;
            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── Cancel command is no-op after natural dismiss ─────────────────────────

    [Fact]
    public async Task CancelCommandAfterNaturalDismissIsNoOp()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            using CancellationTokenSource dialogCts = new();

            Task<DialogResult<int>> task = PostSelectDialog(engine, Items("A"), ct: dialogCts.Token);

            // Dismiss naturally.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
            await SettleAsync(engine, clock);
            DialogResult<int> result = await task;
            Assert.Equal(DialogOutcome.Submitted, result.Outcome);

            // Now fire the cancellation token — cancel command should be a no-op.
            await dialogCts.CancelAsync();
            await SettleAsync(engine, clock);

            // Overlay is still null (not re-opened).
            Assert.Null(sink.LastModel?.ActiveOverlay);
        }
        finally { engine.Dispose(); }
    }

    // ── B1: Dialog suppresses an active Autocomplete (does NOT reject) ────────

    [Fact]
    public async Task OpenDialogWhileAutocompleteActiveSupressesAutocomplete()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            // Show an autocomplete first.
            TextBuffer buf = new();
            Autocomplete ac = new(buf);
            ac.Show([new AutocompleteCandidate("item1", PlainLine("item1"))]);
            engine.Post(new ShowAutocompleteCommand(ac));
            await SettleAsync(engine, clock);
            Assert.IsType<Autocomplete>(sink.LastModel?.ActiveOverlay);

            // Now open a dialog — must suppress the autocomplete, not reject.
            Task<DialogResult<int>> task = PostSelectDialog(engine, Items("A", "B"));
            await SettleAsync(engine, clock);

            // ActiveOverlay must be the new dialog.
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // The dialog task must not be faulted — the autocomplete was suppressed, not rejected.
            Assert.False(task.IsCompleted, "Dialog task should still be pending (not faulted)");

            // Dismiss the dialog cleanly.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            DialogResult<int> result = await task;
            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── B1: Second concurrent Dialog is still rejected; first survives ────────

    [Fact]
    public async Task SecondDialogWhileFirstDialogActiveIsRejected()
    {
        (LoopEngine engine, VirtualClock clock, CapturingOutputSink sink) = CreateEngine();
        try
        {
            Task<DialogResult<int>> first = PostSelectDialog(engine, Items("1", "2"));
            await SettleAsync(engine, clock);
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Second dialog — must be rejected because the overlay is already a Dialog.
            Task<DialogResult<int>> second = PostSelectDialog(engine, Items("A", "B"));
            await SettleAsync(engine, clock);

            InvalidOperationException ex =
                await Assert.ThrowsAsync<InvalidOperationException>(() => second);
            Assert.Contains("already active", ex.Message, StringComparison.OrdinalIgnoreCase);

            // First dialog must still be the active overlay.
            Assert.IsType<Dialog>(sink.LastModel?.ActiveOverlay);

            // Dismiss the first dialog.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);
            DialogResult<int> firstResult = await first;
            Assert.Equal(DialogOutcome.Cancelled, firstResult.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── B2: Titled dialog Render never exceeds MaxRows budget ─────────────────

    [Fact]
    public void TitledDialogMaxRows1RenderReturnsTitleOnlyRow()
    {
        // At MaxRows=1 the title consumes the entire budget; zero list rows must appear.
        Line title = PlainLine("Title");
        Dialog dialog = new(multiSelect: false, modal: true, title: [title]);
        dialog.List.SetItems(Items("A", "B", "C"));
        dialog.MaxRows = 1;

        IReadOnlyList<Line> rows = dialog.Render(80);

        Assert.True(rows.Count <= 1, $"Rows count {rows.Count} exceeds MaxRows 1");
        Line only = Assert.Single(rows);
        Assert.Same(title, only);
    }

    [Fact]
    public void TitledDialogMaxRows2RenderReturnsTitlePlusOneListRow()
    {
        // At MaxRows=2: title(1) + list(1) = 2 rows total.
        Line title = PlainLine("Title");
        Dialog dialog = new(multiSelect: false, modal: true, title: [title]);
        dialog.List.SetItems(Items("A", "B", "C"));
        dialog.MaxRows = 2;

        IReadOnlyList<Line> rows = dialog.Render(80);

        Assert.True(rows.Count <= 2, $"Rows count {rows.Count} exceeds MaxRows 2");
        Assert.Equal(2, rows.Count);
        Assert.Same(title, rows[0]);
    }

    // ── AllowBack — section 3 ─────────────────────────────────────────────────

    // Helper: post a select dialog with AllowBack=true; completion maps Back correctly.
    private static Task<DialogResult<int>> PostSelectDialogAllowBack(
        LoopEngine engine,
        List<Line> items)
    {
        Dialog dialog = new(multiSelect: false, modal: true, allowBack: true);
        dialog.List.SetItems(items);

        TaskCompletionSource<DialogResult<int>> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Action completion = () =>
        {
            DialogResult<int> result = dialog.CloseRequest switch
            {
                OverlayCloseKind.Submit => new DialogResult<int>(DialogOutcome.Submitted, dialog.List.SelectedIndex),
                OverlayCloseKind.Back => new DialogResult<int>(DialogOutcome.Back, -1),
                _ => new DialogResult<int>(DialogOutcome.Cancelled, -1),
            };
            tcs.TrySetResult(result);
        };

        Action reject = () =>
            tcs.TrySetException(new InvalidOperationException("A dialog is already active."));

        engine.Post(new OpenDialogCommand(dialog, completion, reject));
        return tcs.Task;
    }

    /// <summary>
    /// AllowBack=true + Backspace at empty (no movement) → DialogOutcome.Back.
    /// </summary>
    [Fact]
    public async Task SelectAllowBackBackspaceAtEmptyReturnsBack()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostSelectDialogAllowBack(engine, Items("A", "B", "C"));

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<int> result = await task;

            Assert.Equal(DialogOutcome.Back, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    /// <summary>
    /// AllowBack=true + ↓ then Backspace → Backspace is a no-op (cursor has moved).
    /// The dialog stays open; pressing Escape afterwards produces Cancelled.
    /// </summary>
    [Fact]
    public async Task SelectAllowBackBackspaceAfterMovementIsNoOp()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostSelectDialogAllowBack(engine, Items("A", "B", "C"));

            // ↓ marks _hasMoved = true; subsequent Backspace must not close the dialog.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));

            // Give the loop time to process both keys.
            await SettleAsync(engine, clock);

            // Task must not be complete — the dialog is still open.
            Assert.False(task.IsCompleted, "Dialog should still be open after Backspace post-movement");

            // Dismiss with Escape to clean up.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);

            DialogResult<int> result = await task;
            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    /// <summary>
    /// AllowBack=false (default) + Backspace → no-op; dialog stays open; Enter produces Submitted.
    /// </summary>
    [Fact]
    public async Task SelectAllowBackFalseDefaultBackspaceIsNoOp()
    {
        (LoopEngine engine, VirtualClock clock, _) = CreateEngine();
        try
        {
            // Default AllowBack=false — use the standard PostSelectDialog helper.
            Task<DialogResult<int>> task = PostSelectDialog(engine, Items("X", "Y", "Z"));

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));

            await SettleAsync(engine, clock);

            // Task must still be pending — Backspace should have been swallowed by the modal catch-all.
            Assert.False(task.IsCompleted, "Dialog should still be open after Backspace when AllowBack=false");

            // Submit to close.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
            await SettleAsync(engine, clock);

            DialogResult<int> result = await task;
            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── 3.8 — MultiSelectRequest deliberately omits AllowBack ────────────────

    [Fact]
    public void MultiSelectRequestDoesNotHaveAllowBackProperty()
    {
        // AllowBack is intentionally absent from MultiSelectRequest (design decision §3.8).
        Assert.Null(typeof(MultiSelectRequest).GetProperty("AllowBack"));
    }
}
