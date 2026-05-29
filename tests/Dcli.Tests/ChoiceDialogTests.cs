using System.Text;
using Dcli.Internal.FixedRegion;
using Dcli.Internal.RenderLoop;
using Dcli.Testing;
using Xunit;

namespace Dcli.Tests;

/// <summary>
/// Tests for §3 AllowBack on <see cref="ChoiceRequest"/>.
/// Mirrors the Select-side AllowBack tests in <see cref="DialogSelectionTests"/>.
/// </summary>
public sealed class ChoiceDialogTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private sealed class ConstantSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Columns, int Rows) GetSize() => (cols, rows);
    }

    private sealed class CapturingOutputSink : IOutputSink
    {
        public void Paint(RenderModel model) { }
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

    private static (LoopEngine Engine, VirtualClock Clock) CreateEngine(int cols = 80, int rows = 24)
    {
        VirtualClock clock = new(TimeSpan.Zero);
        CapturingOutputSink sink = new();
        LoopEngine engine = new(
            new ConstantSizeSource(cols, rows),
            clock,
            sink,
            minFrameInterval: TimeSpan.Zero);
        return (engine, clock);
    }

    private static Line PlainLine(string text) => new([new Segment(text)]);

    private static List<Line> Options(params string[] texts) =>
        texts.Select(PlainLine).ToList();

    private static Task SettleAsync(LoopEngine engine, VirtualClock clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(10));
        return engine.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Task<DialogResult<int>> PostChoiceDialog(
        LoopEngine engine,
        List<Line> options)
    {
        Dialog dialog = new(multiSelect: false, modal: true);
        dialog.List.SetItems(options);

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

    private static Task<DialogResult<int>> PostChoiceDialogAllowBack(
        LoopEngine engine,
        List<Line> options)
    {
        Dialog dialog = new(multiSelect: false, modal: true, allowBack: true);
        dialog.List.SetItems(options);

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

    // ── AllowBack tests ───────────────────────────────────────────────────────

    /// <summary>
    /// AllowBack=true + Backspace at empty (no movement) → DialogOutcome.Back.
    /// </summary>
    [Fact]
    public async Task ChoiceAllowBackBackspaceAtEmptyReturnsBack()
    {
        (LoopEngine engine, VirtualClock clock) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostChoiceDialogAllowBack(engine, Options("Yes", "No", "Maybe"));

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
    public async Task ChoiceAllowBackBackspaceAfterMovementIsNoOp()
    {
        (LoopEngine engine, VirtualClock clock) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostChoiceDialogAllowBack(engine, Options("Yes", "No", "Maybe"));

            // ↓ marks _hasMoved = true; subsequent Backspace must not close the dialog.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));

            await SettleAsync(engine, clock);

            // Task must not be complete — the dialog is still open.
            Assert.False(task.IsCompleted, "Choice dialog should still be open after Backspace post-movement");

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
    public async Task ChoiceAllowBackFalseDefaultBackspaceIsNoOp()
    {
        (LoopEngine engine, VirtualClock clock) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostChoiceDialog(engine, Options("Alpha", "Beta", "Gamma"));

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Backspace), Modifiers.None));

            await SettleAsync(engine, clock);

            // Task must still be pending — Backspace should have been swallowed by the modal catch-all.
            Assert.False(task.IsCompleted, "Choice dialog should still be open after Backspace when AllowBack=false");

            // Submit to close.
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Enter), Modifiers.None));
            await SettleAsync(engine, clock);

            DialogResult<int> result = await task;
            Assert.Equal(DialogOutcome.Submitted, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── §5.5 — Choice AllowBack=true accepts '[' as secondary Back key ───────────

    /// <summary>
    /// §5.5 — Choice AllowBack=true + '[' before moving → DialogOutcome.Back.
    /// </summary>
    [Fact]
    public async Task ChoiceAllowBackBracketBeforeMovingReturnsBack()
    {
        (LoopEngine engine, VirtualClock clock) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostChoiceDialogAllowBack(engine, Options("Yes", "No", "Maybe"));

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('[')), Modifiers.None));

            await SettleAsync(engine, clock);
            DialogResult<int> result = await task;

            Assert.Equal(DialogOutcome.Back, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    /// <summary>
    /// §5.5 — Choice AllowBack=true + ↓ then '[' → NOT Back (movement-suppression applies to '[').
    /// </summary>
    [Fact]
    public async Task ChoiceAllowBackBracketAfterMovementIsNoOp()
    {
        (LoopEngine engine, VirtualClock clock) = CreateEngine();
        try
        {
            Task<DialogResult<int>> task = PostChoiceDialogAllowBack(engine, Options("Yes", "No", "Maybe"));

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Down), Modifiers.None));
            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.FromRune(new Rune('[')), Modifiers.None));

            await SettleAsync(engine, clock);

            Assert.False(task.IsCompleted, "Choice dialog should still be open after '[' post-movement");

            engine.InputWriter.TryWrite(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
            await SettleAsync(engine, clock);

            DialogResult<int> result = await task;
            Assert.Equal(DialogOutcome.Cancelled, result.Outcome);
        }
        finally { engine.Dispose(); }
    }

    // ── §3.3 — Multi-line Prompt on ChoiceRequest renders all lines above options ─

    // Spec scenario: "Multi-line preamble renders all lines above the widget"

    /// <summary>
    /// §3.3 — A <see cref="ChoiceRequest"/> with a 3-line prompt renders all three preamble lines
    /// above the option rows in order.
    /// </summary>
    [Fact]
    public async Task ChoiceRequestMultiLinePromptRendersAllLinesAboveOptions()
    {
        await using HeadlessTerminal harness = await HeadlessTerminal.StartAsync(
            new HeadlessTerminalOptions { InitialColumns = 40, InitialRows = 12 });

        IReadOnlyList<Line> prompt = new Line[]
        {
            PlainLine("Confirm the action"),
            PlainLine("This cannot be undone"),
            PlainLine("Select an option:"),
        };
        ChoiceRequest req = new([PlainLine("yes"), PlainLine("no"), PlainLine("cancel")],
            Prompt: prompt);

        Task<DialogResult<int>> dialogTask = harness.Terminal.ChoiceAsync(req);
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        FrameSnapshot snap = harness.Snapshot;
        Assert.Equal(OverlayKind.Dialog, snap.Overlay.Kind);

        int idxLine1 = FindRowIndexContaining(snap.FixedRegionRows, "Confirm the action");
        int idxLine2 = FindRowIndexContaining(snap.FixedRegionRows, "This cannot be undone");
        int idxLine3 = FindRowIndexContaining(snap.FixedRegionRows, "Select an option:");
        int idxItem = FindRowIndexContaining(snap.FixedRegionRows, "yes");

        Assert.True(idxLine1 >= 0, "Prompt line 1 'Confirm the action' not found");
        Assert.True(idxLine2 >= 0, "Prompt line 2 'This cannot be undone' not found");
        Assert.True(idxLine3 >= 0, "Prompt line 3 'Select an option:' not found");
        Assert.True(idxItem >= 0, "Option 'yes' not found in FixedRegionRows");
        Assert.True(idxLine1 < idxLine2, $"Prompt lines out of order: {idxLine1},{idxLine2}");
        Assert.True(idxLine2 < idxLine3, $"Prompt lines out of order: {idxLine2},{idxLine3}");
        Assert.True(idxLine3 < idxItem,
            $"Prompt lines must appear before options; got rows: {idxLine1},{idxLine2},{idxLine3},{idxItem}");

        harness.SendKey(new KeyEvent(KeyCode.Named(NamedKey.Escape), Modifiers.None));
        await harness.SettleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await dialogTask;
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
